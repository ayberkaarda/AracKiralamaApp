using AracKiralama.Business.Services;
using AracKiralama.Business.Validation;
using AracKiralama.Domain.Entities;
using AracKiralama.Domain.Repositories;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace AracKiralama.Business.Tests;

/// <summary>The revenue sums, the report's limits, the calendar's day states and the maintenance input rules.</summary>
public sealed class ReportAndCalendarTests
{
    private static readonly DateOnly Today = new(2026, 3, 2);

    private readonly Mock<IUserRepository> _users = new(MockBehavior.Strict);
    private readonly Mock<IReportRepository> _reports = new(MockBehavior.Strict);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly RecordingAuditLog _audit = new();

    /// <summary>The database's clock, more than a year ahead of the workstation's.</summary>
    private static FakeTimeProvider DatabaseTime => new(new DateTimeOffset(2027, 5, 10, 9, 0, 0, TimeSpan.Zero));

    public ReportAndCalendarTests()
    {
        _users.Setup(u => u.GetSessionStateAsync(Sessions.Admin.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new SessionState(UserRole.Admin, 0));
        _users.Setup(u => u.GetSessionStateAsync(Sessions.User.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new SessionState(UserRole.User, 0));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Build_ByMonth_ListsEveryMonthAndSumsByPickUpDayAndCar()
    {
        RevenueRow[] rows =
        [
            Row(1, 1, new DateOnly(2026, 1, 31), 3, 300m),
            Row(2, 2, new DateOnly(2026, 1, 5), 1, 50.25m),
            Row(3, 1, new DateOnly(2026, 3, 1), 2, 200m),
        ];

        var report = ReportService.Build(new DateOnly(2026, 1, 15), new DateOnly(2026, 3, 10), RevenueGrouping.Month, rows, DateTime.MinValue, new DateOnly(2026, 3, 10));

        Assert.Equal(
            [new RevenueLine("Ocak 2026 (15.01-31.01)", null, 2, 4, 350.25m), new RevenueLine("Şubat 2026", null, 0, 0, 0m), new RevenueLine("Mart 2026 (01.03-10.03)", null, 1, 2, 200m)],
            report.Periods);
        Assert.Equal(
            [new RevenueLine("Anka Kent", "00 AR 01", 2, 5, 500m), new RevenueLine("Anka Kent", "00 AR 02", 1, 1, 50.25m)],
            report.Cars);
        Assert.Equal(new RevenueLine("Toplam", null, 3, 6, 550.25m), report.Total);
    }

    [Fact]
    public void Build_ByDay_HasOneLinePerDay()
    {
        var report = ReportService.Build(Today, Today.AddDays(2), RevenueGrouping.Day, [Row(1, 1, Today.AddDays(1), 2, 10m)], DateTime.MinValue, Today.AddDays(2));

        Assert.Equal(["02.03.2026", "03.03.2026", "04.03.2026"], report.Periods.Select(p => p.Label));
        Assert.Equal([0, 1, 0], report.Periods.Select(p => p.Rentals));
    }

    /// <summary>
    /// Only rentals picked up by today (the database's) count: a later one can still be cancelled without
    /// charge. A range in the past asks for its own half-open range, one that reaches beyond today stops at
    /// today, and one in the future reads nothing.
    /// </summary>
    [Fact]
    public async Task GetRevenue_CountsOnlyRentalsPickedUpByToday()
    {
        _reports.Setup(r => r.GetRevenueRowsAsync(Today, Today.AddDays(1), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _reports.Setup(r => r.GetRevenueRowsAsync(Today.AddDays(-40), Today.AddDays(-9), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var report = await Reports().GetRevenueAsync(Sessions.Admin, Today, Today.AddDays(30), RevenueGrouping.Day, Ct);
        var past = await Reports().GetRevenueAsync(Sessions.Admin, Today.AddDays(-40), Today.AddDays(-10), RevenueGrouping.Day, Ct);
        var future = await Reports().GetRevenueAsync(Sessions.Admin, Today.AddDays(1), Today.AddDays(5), RevenueGrouping.Day, Ct);

        Assert.Equal((Today, Today.AddDays(-10), Today), (report.CountedUntil, past.CountedUntil, future.CountedUntil));
        Assert.Equal(0, future.Total.Rentals);
        Assert.Contains("02.03.2026 tarihine kadar teslim alınan", report.Note, StringComparison.Ordinal);
        _reports.Verify(r => r.GetRevenueRowsAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Equal(31, report.Periods.Count);
        Assert.Equal(new DateTime(2026, 3, 2, 10, 0, 0), report.CreatedAt);
        Assert.Equal(3, _audit.Entries.Count(e => e == "report.revenue.created admin"));
    }

    [Theory]
    [InlineData(-1, RevenueGrouping.Month, ReportService.RangeInvalid)]
    [InlineData(3660, RevenueGrouping.Month, ReportService.RangeTooLong)]
    [InlineData(366, RevenueGrouping.Day, ReportService.DailyRangeTooLong)]
    public async Task GetRevenue_RangesOutsideTheLimits_AreRefusedBeforeAnyRead(int lastOffset, RevenueGrouping grouping, string message)
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Reports().GetRevenueAsync(Sessions.Admin, Today, Today.AddDays(lastOffset), grouping, Ct));

        Assert.Equal(message, error.Message);
        _reports.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(3659, RevenueGrouping.Month)]
    [InlineData(365, RevenueGrouping.Day)]
    [InlineData(0, RevenueGrouping.Day)]
    public async Task GetRevenue_TheLongestAllowedRanges_AreRead(int lastOffset, RevenueGrouping grouping)
    {
        _reports.Setup(r => r.GetRevenueRowsAsync(Today, Today.AddDays(1), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await Reports().GetRevenueAsync(Sessions.Admin, Today, Today.AddDays(lastOffset), grouping, Ct);
    }

    [Fact]
    public async Task GetRevenue_ForACustomer_IsDenied()
    {
        await Assert.ThrowsAsync<AccessDeniedException>(() => Reports().GetRevenueAsync(Sessions.User, Today, Today, RevenueGrouping.Day, Ct));

        _reports.VerifyNoOtherCalls();
    }

    [Fact]
    public void Calendar_DayStates_FollowTheRangesAndToday()
    {
        var first = new DateOnly(2026, 3, 1);
        BlockedRange[] blocked =
        [
            new(new DateOnly(2026, 2, 27), new DateOnly(2026, 3, 2), BlockKind.Rental),
            new(new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 7), BlockKind.Maintenance),
            new(new DateOnly(2026, 3, 6), new DateOnly(2026, 3, 8), BlockKind.Rental),
        ];

        var days = AvailabilityService.Build(first, first.AddMonths(1), Today, blocked, showPast: true);

        Assert.Equal(31, days.Count);
        Assert.Equal(DayState.Booked, days[0].State);      // 1 March: past but rented
        Assert.Equal(DayState.Free, days[1].State);        // 2 March: today, the rental ended
        Assert.Equal(DayState.Maintenance, days[4].State); // 5 March
        Assert.Equal(DayState.Maintenance, days[5].State); // 6 March: maintenance wins
        Assert.Equal(DayState.Booked, days[6].State);      // 7 March
        Assert.Equal(DayState.Free, days[7].State);        // 8 March: back
        Assert.Equal(new DateOnly(2026, 3, 31), days[^1].Date);
    }

    /// <summary>
    /// A rental under way since 27 February: the admin sees its past days as booked, a customer sees every
    /// day before today as past and only the days still taken as booked.
    /// </summary>
    [Theory]
    [InlineData(true, DayState.Booked)]
    [InlineData(false, DayState.Past)]
    public async Task Calendar_PastDaysOfACurrentRental_AreShownToAdminsOnly(bool admin, DayState firstOfMarch)
    {
        var cars = new Mock<ICarRepository>(MockBehavior.Strict);
        cars.Setup(c => c.FindAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new Car { Id = 1, IsListed = true });
        var rentals = new Mock<IRentalRepository>(MockBehavior.Strict);
        rentals.Setup(r => r.GetBlockedRangesAsync(1, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BlockedRange(new DateOnly(2026, 2, 27), new DateOnly(2026, 3, 4), BlockKind.Rental), new BlockedRange(new DateOnly(2026, 2, 20), new DateOnly(2026, 2, 27), BlockKind.Maintenance)]);
        var service = new AvailabilityService(cars.Object, rentals.Object, Guard(), _time, new TestDatabaseClock(_time));

        var days = (await service.GetCalendarAsync(admin ? Sessions.Admin : Sessions.User, 1, Today, Ct)).Days;

        Assert.Equal(firstOfMarch, days[0].State);
        Assert.Equal(DayState.Booked, days[1].State); // today
        Assert.Equal(DayState.Booked, days[2].State);
        Assert.Equal(DayState.Free, days[3].State);
    }

    [Fact]
    public void Calendar_FreeDaysBeforeToday_ArePast()
    {
        var first = new DateOnly(2026, 2, 1);

        var days = AvailabilityService.Build(first, first.AddMonths(1), Today, [], showPast: true);

        Assert.Equal(28, days.Count);
        Assert.All(days, d => Assert.Equal(DayState.Past, d.State));
    }

    [Theory]
    [InlineData(120, true)]
    [InlineData(-120, true)]
    [InlineData(121, false)]
    [InlineData(-121, false)]
    public async Task Calendar_MonthsFarAway_AreRefused(int months, bool allowed)
    {
        var cars = new Mock<ICarRepository>(MockBehavior.Strict);
        cars.Setup(c => c.FindAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new Car { Id = 1, IsListed = true });
        var rentals = new Mock<IRentalRepository>(MockBehavior.Strict);
        rentals.Setup(r => r.GetBlockedRangesAsync(1, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var service = new AvailabilityService(cars.Object, rentals.Object, Guard(), _time, new TestDatabaseClock(_time));

        var month = Today.AddMonths(months);
        if (allowed)
        {
            Assert.Equal(new DateOnly(month.Year, month.Month, 1), (await service.GetCalendarAsync(Sessions.User, 1, month, Ct)).Month);
        }
        else
        {
            Assert.Equal(AvailabilityService.MonthOutOfRange, (await Assert.ThrowsAsync<BusinessRuleException>(() => service.GetCalendarAsync(Sessions.User, 1, month, Ct))).Message);
        }
    }

    /// <summary>On a workstation whose clock is a year behind, the calendar still opens on the database's month.</summary>
    [Fact]
    public async Task Calendar_WithoutAMonth_OpensOnTheDatabasesMonth()
    {
        var cars = new Mock<ICarRepository>(MockBehavior.Strict);
        cars.Setup(c => c.FindAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new Car { Id = 1, IsListed = true });
        var rentals = new Mock<IRentalRepository>(MockBehavior.Strict);
        rentals.Setup(r => r.GetBlockedRangesAsync(1, new DateOnly(2027, 5, 1), new DateOnly(2027, 6, 1), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var service = new AvailabilityService(cars.Object, rentals.Object, Guard(), _time, new TestDatabaseClock(DatabaseTime));

        var calendar = await service.GetCalendarAsync(Sessions.User, 1, null, Ct);

        Assert.Equal((new DateOnly(2027, 5, 1), new DateOnly(2027, 5, 10)), (calendar.Month, calendar.Today));
    }

    [Fact]
    public async Task Report_DefaultRange_IsThisMonthUpToTodayByTheDatabasesClock()
    {
        var reports = new ReportService(_reports.Object, Guard(), _time, new TestDatabaseClock(DatabaseTime), _audit);

        Assert.Equal(new ReportRange(new DateOnly(2027, 5, 1), new DateOnly(2027, 5, 10)), await reports.GetDefaultRangeAsync(Sessions.Admin, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => reports.GetDefaultRangeAsync(Sessions.User, Ct));
    }

    [Theory]
    [InlineData((MaintenanceKind)7, "Bakım", null, MaintenanceFieldsValidator.KindInvalid)]
    [InlineData(MaintenanceKind.Damage, "   ", null, MaintenanceFieldsValidator.DescriptionRequired)]
    [InlineData(MaintenanceKind.Damage, "Satır\u0007zil", null, MaintenanceFieldsValidator.DescriptionInvalid)]
    [InlineData(MaintenanceKind.Damage, "Ön tampon\r\n\tboya", "-1", MaintenanceFieldsValidator.CostInvalid)]
    [InlineData(MaintenanceKind.Damage, "Ön tampon", "1.250", MaintenanceFieldsValidator.CostInvalid)]
    [InlineData(MaintenanceKind.Damage, "Ön tampon", "12,345", MaintenanceFieldsValidator.CostInvalid)]
    [InlineData(MaintenanceKind.Damage, "Ön tampon", "100000000", MaintenanceFieldsValidator.CostInvalid)]
    [InlineData(MaintenanceKind.Damage, "Ön tampon\r\n\tboya", "99999999,99", null)]
    [InlineData(MaintenanceKind.Maintenance, "Bakım", "", null)]
    public void MaintenanceInput_Rules(MaintenanceKind kind, string description, string? cost, string? message)
    {
        var result = new MaintenanceFieldsValidator().Validate(new MaintenanceFields(new MaintenanceInput(kind, description, Today, Today.AddDays(1), cost), Today));

        Assert.Equal(message, result.IsValid ? null : result.Errors[0].ErrorMessage);
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void MaintenanceDescription_Length(int length, bool valid) =>
        Assert.Equal(valid, InputRules.IsValidMaintenanceDescription(new string('a', length)));

    private static RevenueRow Row(int id, int car, DateOnly start, int days, decimal price) =>
        new(id, car, "Anka", "Kent", "00 AR 0" + car.ToString(System.Globalization.CultureInfo.InvariantCulture), start, start.AddDays(days), price);

    private AccessGuard Guard() => new(_users.Object, _audit);

    private ReportService Reports() => new(_reports.Object, Guard(), _time, new TestDatabaseClock(_time), _audit);
}
