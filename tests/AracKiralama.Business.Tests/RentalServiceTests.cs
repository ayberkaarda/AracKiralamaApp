using AracKiralama.Business.Services;
using AracKiralama.Domain.Entities;
using AracKiralama.Domain.Repositories;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace AracKiralama.Business.Tests;

public sealed class RentalServiceTests
{
    /// <summary>Monday 2 March 2026 on the local clock (the fake clock's zone is UTC).</summary>
    private static readonly DateOnly Today = new(2026, 3, 2);

    private readonly Mock<IRentalRepository> _rentals = new(MockBehavior.Strict);
    private readonly Mock<IUserRepository> _users = new(MockBehavior.Strict);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly RecordingAuditLog _audit = new();
    private RentalPolicy _policy = new() { MinRentalDays = 1, MaxRentalDays = 30, MaxAdvanceDays = 60 };
    private IDatabaseClock? _databaseClock;

    public RentalServiceTests()
    {
        _users.Setup(u => u.GetSessionStateAsync(Sessions.Admin.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new SessionState(UserRole.Admin, 0));
        _users.Setup(u => u.GetSessionStateAsync(Sessions.User.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new SessionState(UserRole.User, 0));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Quote_IsTheDailyPriceTimesTheDaysFromTheStartDay()
    {
        var quote = Service().Quote(3, 850.5m, Today.AddDays(2), 4, Today);

        Assert.Equal(3402m, quote.Total);
        Assert.Equal(Today.AddDays(2), quote.StartDate);
        Assert.Equal(Today.AddDays(6), quote.EndDate);
        Assert.False(quote.Discounted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(31)]
    public void Quote_DaysOutsideTheSettings_AreRefused(int days)
    {
        var error = Assert.Throws<BusinessRuleException>(() => Service().Quote(3, 12.5m, Today, days, Today));

        Assert.Equal("Gün sayısı 1 ile 30 arasında olmalıdır.", error.Message);
    }

    [Fact]
    public void Quote_ShortestRentalFromTheSettings_IsEnforced()
    {
        _policy = new RentalPolicy { MinRentalDays = 3, MaxRentalDays = 30 };

        Assert.Throws<BusinessRuleException>(() => Service().Quote(3, 100m, Today, 2, Today));
        Assert.Equal(300m, Service().Quote(3, 100m, Today, 3, Today).Total);
    }

    [Fact]
    public void Quote_StartInThePast_IsRefused()
    {
        var error = Assert.Throws<BusinessRuleException>(() => Service().Quote(3, 100m, Today.AddDays(-1), 2, Today));

        Assert.Equal(RentalService.StartInPast, error.Message);
    }

    [Fact]
    public void Quote_StartBeyondTheBookingHorizon_IsRefused()
    {
        Assert.Equal(Today.AddDays(60), Service().Quote(3, 100m, Today.AddDays(60), 1, Today).StartDate);
        Assert.Throws<BusinessRuleException>(() => Service().Quote(3, 100m, Today.AddDays(61), 1, Today));
    }

    [Fact]
    public void Quote_TotalBeyondTheMoneyColumn_IsRefused()
    {
        Assert.Throws<BusinessRuleException>(() => Service().Quote(3, 99_999_999m, Today, 2, Today));
    }

    [Fact]
    public async Task Rent_SendsTheAccountOfTheSessionAndRecomputedValues()
    {
        RentalRequest? sent = null;
        _rentals.Setup(r => r.RentAsync(It.IsAny<RentalRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RentalRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(new RentResult(RentOutcome.Rented, 11));

        // The screen's total and end day are not trusted: only car, start, days and price are taken from it.
        var forged = new RentalQuote(3, 2, 10m, 0.01m, Today.AddDays(1), Today.AddDays(30), false);
        var receipt = await Service().RentAsync(Sessions.User, forged, Ct);

        Assert.Equal(new RentalRequest(3, Sessions.User.UserId, Today.AddDays(1), Today.AddDays(3), 10m, 20m, 0, 60), sent);
        Assert.Equal(11, receipt.RentalId);
        Assert.Equal(20m, receipt.Total);
        Assert.Equal(["rental.created user"], _audit.Entries);
    }

    [Fact]
    public async Task Rent_ForgedStartInThePast_IsRefusedBeforeTheRepository()
    {
        var forged = new RentalQuote(3, 2, 10m, 20m, Today.AddDays(-3), Today.AddDays(-1), false);

        await Assert.ThrowsAsync<BusinessRuleException>(() => Service().RentAsync(Sessions.User, forged, Ct));

        _rentals.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(RentOutcome.Overlaps, RentalService.Overlaps)]
    [InlineData(RentOutcome.NotAvailable, RentalService.NotAvailable)]
    [InlineData(RentOutcome.PriceChanged, RentalService.PriceChanged)]
    [InlineData(RentOutcome.CarNotFound, RentalService.CarNotFound)]
    [InlineData(RentOutcome.StartInPast, RentalService.StartInPast)]
    [InlineData(RentOutcome.StartTooFar, "Kiralama en fazla 60 gün sonrası için yapılabilir.")]
    public async Task Rent_RefusedByTheRepository_BecomesARuleMessage(RentOutcome outcome, string message)
    {
        _rentals.Setup(r => r.RentAsync(It.IsAny<RentalRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RentResult(outcome, 0));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().RentAsync(Sessions.User, Service().Quote(3, 10m, Today, 1, Today), Ct));

        Assert.Equal(message, error.Message);
        Assert.Equal(outcome == RentOutcome.Overlaps ? ["rental.overlap_refused user"] : [], _audit.Entries);
    }

    /// <summary>IDOR: a user's end request carries their own id as owner filter, so another account's rental is "missing".</summary>
    [Fact]
    public async Task End_ByAUser_IsLimitedToTheirOwnRentals()
    {
        _rentals.Setup(r => r.EndAsync(9, new RentalActor(Sessions.User.UserId, AsAdmin: false, SessionVersion: 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EndRentalResult(EndRentalOutcome.NotFound));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().EndAsync(Sessions.User, 9, Ct));

        Assert.Equal(RentalService.RentalNotFound, error.Message);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task End_ByAnAdmin_HasNoOwnerFilter()
    {
        _rentals.Setup(r => r.EndAsync(9, new RentalActor(Sessions.Admin.UserId, AsAdmin: true, SessionVersion: 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EndRentalResult(EndRentalOutcome.Returned));

        Assert.Equal(RentalEnding.Returned, await Service().EndAsync(Sessions.Admin, 9, Ct));
        Assert.Equal(["rental.ended admin"], _audit.Entries);
    }

    /// <summary>A demoted admin's open session ends only its own rentals: the owner filter comes from the stored role.</summary>
    [Fact]
    public async Task End_ByADemotedAdmin_IsLimitedToTheirOwnRentals()
    {
        _users.Setup(u => u.GetSessionStateAsync(Sessions.Admin.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new SessionState(UserRole.User, 0));
        _rentals.Setup(r => r.EndAsync(9, new RentalActor(Sessions.Admin.UserId, AsAdmin: false, SessionVersion: 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EndRentalResult(EndRentalOutcome.NotFound));

        await Assert.ThrowsAsync<BusinessRuleException>(() => Service().EndAsync(Sessions.Admin, 9, Ct));

        _rentals.Verify(r => r.EndAsync(9, new RentalActor(Sessions.Admin.UserId, AsAdmin: false, SessionVersion: 0), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ActiveRentals_OfEveryone_AreForAdminsOnly()
    {
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service().GetActiveRentalsAsync(Sessions.User, CarFilter.None, Ct));
        _rentals.VerifyNoOtherCalls();

        _rentals.Setup(r => r.GetActiveAsync(Sessions.User.UserId, CarFilter.None, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        await Service().GetMyActiveRentalsAsync(Sessions.User, Ct);
        _rentals.Verify(r => r.GetActiveAsync(Sessions.User.UserId, CarFilter.None, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// The computer's clock is two days behind the database's. A booking from "tomorrow" by the
    /// computer's clock starts in the past by the database's and is refused. Whether ending a rental
    /// cancels or returns it is left to the repository, which reads the database's date under the car's lock.
    /// </summary>
    [Fact]
    public async Task Operations_DecideWithTheDatabaseClock()
    {
        _databaseClock = new TestDatabaseClock(new FakeTimeProvider(new DateTimeOffset(2026, 3, 4, 10, 0, 0, TimeSpan.Zero)));
        var service = Service();
        var quote = service.Quote(3, 10m, Today.AddDays(1), 2, Today);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RentAsync(Sessions.User, quote, Ct));
        Assert.Equal(RentalService.StartInPast, error.Message);

        _rentals.Setup(r => r.EndAsync(9, new RentalActor(Sessions.User.UserId, AsAdmin: false, SessionVersion: 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EndRentalResult(EndRentalOutcome.Returned));
        Assert.Equal(RentalEnding.Returned, await service.EndAsync(Sessions.User, 9, Ct));
    }

    /// <summary>The booking screen's today is the database's date, here two days after the computer's.</summary>
    [Fact]
    public async Task Today_ForTheBookingScreen_IsTheDatabasesDate()
    {
        _databaseClock = new TestDatabaseClock(new FakeTimeProvider(new DateTimeOffset(2026, 3, 4, 10, 0, 0, TimeSpan.Zero)));

        Assert.Equal(Today.AddDays(2), await Service().GetTodayAsync(Sessions.User, Ct));
    }

    [Fact]
    public void IsUnderway_FromThePickUpDayOn()
    {
        var service = Service();

        Assert.False(service.IsUnderway(Rental(Today.AddDays(1))));
        Assert.True(service.IsUnderway(Rental(Today)));
        Assert.True(service.IsUnderway(Rental(Today.AddDays(-2))));
    }

    [Theory]
    [InlineData(0, 30, 365, 0, 0, 1)]
    [InlineData(1, 0, 365, 0, 0, 1)]
    [InlineData(5, 3, 365, 0, 0, 1)]
    [InlineData(1, 30, -1, 0, 0, 1)]
    [InlineData(1, 30, 365, -1, 0, 1)]
    [InlineData(1, 30, 365, 7, 91, 1)]
    [InlineData(1, 30, 365, 7, 10, 0)]
    public void Policy_Validate_ReportsInvalidSettings(int min, int max, int advance, int longDays, int percent, int expectedProblems)
    {
        var policy = new RentalPolicy
        {
            MinRentalDays = min,
            MaxRentalDays = max,
            MaxAdvanceDays = advance,
            LongRentalDays = longDays,
            LongRentalDiscountPercent = percent,
        };

        Assert.Equal(expectedProblems, policy.Validate().Count);
    }

    private static RentalDetails Rental(DateOnly start) =>
        new(9, 3, "Anka", "Kent", "Otomatik", "Benzin", "00 DMO 001", null, 2, "ornek", start, start.AddDays(2), 20m, RentalStatus.Active);

    private RentalService Service() => new(_rentals.Object, new AccessGuard(_users.Object, _audit), _time, _databaseClock ?? new TestDatabaseClock(_time), _policy, _audit, TestValidators.Quote(_policy));
}
