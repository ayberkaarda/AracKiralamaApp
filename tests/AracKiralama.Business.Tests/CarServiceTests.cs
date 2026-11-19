using AracKiralama.Business.Security;
using AracKiralama.Business.Services;
using AracKiralama.Domain.Entities;
using AracKiralama.Domain.Repositories;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace AracKiralama.Business.Tests;

public sealed class CarServiceTests
{
    private readonly Mock<ICarRepository> _cars = new(MockBehavior.Strict);
    private readonly Mock<IUserRepository> _users = new(MockBehavior.Strict);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly RecordingAuditLog _audit = new();

    public CarServiceTests()
    {
        _users.Setup(u => u.GetSessionStateAsync(Sessions.Admin.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new SessionState(UserRole.Admin, 0));
        _users.Setup(u => u.GetSessionStateAsync(Sessions.User.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new SessionState(UserRole.User, 0));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CarInput Valid => new("Anka", "Kent", "2021", "Otomatik", "Benzin", "32000", "Beyaz", " 00  dmo 001 ", "850,50", "https://example.com/arac.png", true);

    [Fact]
    public async Task AdminOperations_ForAUser_AreDeniedBeforeAnyRepositoryCall()
    {
        var service = Service();

        await Assert.ThrowsAsync<AccessDeniedException>(() => service.GetAllAsync(Sessions.User, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => service.AddAsync(Sessions.User, Valid, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => service.UpdateAsync(Sessions.User, 1, 0, Valid, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => service.DeleteAsync(Sessions.User, 1, Ct));

        _cars.VerifyNoOtherCalls();
        _users.VerifyNoOtherCalls();
        Assert.Equal(4, _audit.Entries.Count);
        Assert.All(_audit.Entries, e => Assert.Equal("access.denied user", e));
    }

    /// <summary>
    /// The role is read from the database at each operation: an admin demoted (or deleted) after signing in
    /// can neither manage cars nor accounts nor see every rental with the session that is still open.
    /// </summary>
    [Fact]
    public async Task DemotedAdminSession_CannotManageVehiclesOrAccounts()
    {
        _users.Setup(u => u.GetSessionStateAsync(Sessions.Admin.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new SessionState(UserRole.User, 0));
        var guard = new AccessGuard(_users.Object, _audit);
        var cars = Service();
        var accounts = new AccountService(_users.Object, new PasswordHasher(iterations: 1_000), guard, _audit, TestValidators.NewAccount, TestValidators.AccountChange);
        var rentals = new RentalService(new Mock<IRentalRepository>(MockBehavior.Strict).Object, guard, _time, new TestDatabaseClock(_time), new RentalPolicy(), _audit, TestValidators.Quote(new RentalPolicy()));

        await Assert.ThrowsAsync<AccessDeniedException>(() => cars.GetAllAsync(Sessions.Admin, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => cars.AddAsync(Sessions.Admin, Valid, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => cars.UpdateAsync(Sessions.Admin, 1, 0, Valid, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => cars.DeleteAsync(Sessions.Admin, 1, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => accounts.GetAllAsync(Sessions.Admin, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(
            () => accounts.UpdateAsync(Sessions.Admin, Sessions.Admin.UserId, 0, new AccountInput("yonetici", null, null, null, UserRole.Admin), Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => rentals.GetActiveRentalsAsync(Sessions.Admin, CarFilter.None, Ct));

        _cars.VerifyNoOtherCalls();
        Assert.Equal(7, _audit.Entries.Count);
    }

    [Fact]
    public async Task DeletedAccount_CannotEvenBrowseTheCars()
    {
        _users.Setup(u => u.GetSessionStateAsync(Sessions.User.UserId, It.IsAny<CancellationToken>())).ReturnsAsync((SessionState?)null);

        await Assert.ThrowsAsync<AccessDeniedException>(() => Service().GetCatalogAsync(Sessions.User, CarFilter.None, Ct));

        _cars.VerifyNoOtherCalls();
    }

    /// <summary>
    /// A session issued before the account's session version was raised is refused even when the stored
    /// role is the one the session has: the change (a demotion undone later, a new password) ended it.
    /// </summary>
    [Fact]
    public async Task SessionFromBeforeAVersionChange_IsRefusedWithTheRoleUnchanged()
    {
        _users.Setup(u => u.GetSessionStateAsync(Sessions.Admin.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new SessionState(UserRole.Admin, 1));
        _users.Setup(u => u.GetSessionStateAsync(Sessions.User.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new SessionState(UserRole.User, 3));

        await Assert.ThrowsAsync<AccessDeniedException>(() => Service().GetAllAsync(Sessions.Admin, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service().GetCatalogAsync(Sessions.User, CarFilter.None, Ct));

        _cars.VerifyNoOtherCalls();
        Assert.Equal(["access.denied admin", "access.denied user"], _audit.Entries);
    }

    [Fact]
    public async Task Add_StoresTheParsedCarWithANormalizedPlate()
    {
        Car? stored = null;
        _cars.Setup(c => c.AddAsync(Sessions.Admin.Actor, It.IsAny<Car>(), It.IsAny<CancellationToken>()))
            .Callback<Actor, Car, CancellationToken>((_, car, _) => stored = car)
            .ReturnsAsync(4);

        var id = await Service().AddAsync(Sessions.Admin, Valid with { Brand = "  Anka  " }, Ct);

        Assert.Equal(4, id);
        Assert.NotNull(stored);
        Assert.Equal("Anka", stored.Brand);
        Assert.Equal(2021, stored.Year);
        Assert.Equal(32000, stored.Mileage);
        Assert.Equal("00 DMO 001", stored.Plate);
        Assert.Equal(850.50m, stored.DailyPrice);
        Assert.True(stored.IsListed);
        Assert.Equal(["car.changed admin"], _audit.Entries);
    }

    [Fact]
    public async Task Add_EmptyForm_ReportsEveryRequiredField()
    {
        var empty = new CarInput(null, " ", "", null, "", "", "", "", "", null, true);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().AddAsync(Sessions.Admin, empty, Ct));

        Assert.Equal(
            [
                nameof(CarInput.Brand), nameof(CarInput.Model), nameof(CarInput.Color), nameof(CarInput.Transmission), nameof(CarInput.FuelType),
                nameof(CarInput.Year), nameof(CarInput.Mileage), nameof(CarInput.Plate), nameof(CarInput.DailyPrice),
            ],
            error.FieldErrors.Keys);
        _cars.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Year", "1949")]
    [InlineData("Year", "2028")]
    [InlineData("Year", "iki bin")]
    [InlineData("Mileage", "-5")]
    [InlineData("Mileage", "2000001")]
    [InlineData("DailyPrice", "0")]
    [InlineData("DailyPrice", "-5")]
    [InlineData("DailyPrice", "12,345")]
    [InlineData("DailyPrice", "1.250")]
    [InlineData("DailyPrice", "100000000")]
    [InlineData("Plate", "34-ÇÖ-12")]
    [InlineData("Plate", "34 ABC 1234567")]
    [InlineData("Transmission", "Yarı Otomatik")]
    [InlineData("FuelType", "Hidrojen")]
    [InlineData("ImageUrl", "C:\\arac.png")]
    [InlineData("ImageUrl", "\\\\sunucu\\pay\\arac.png")]
    [InlineData("ImageUrl", "file:///C:/arac.png")]
    public async Task Add_InvalidValue_IsReportedForItsFieldOnly(string field, string value)
    {
        var input = field switch
        {
            "Year" => Valid with { Year = value },
            "Mileage" => Valid with { Mileage = value },
            "DailyPrice" => Valid with { DailyPrice = value },
            "Plate" => Valid with { Plate = value },
            "Transmission" => Valid with { Transmission = value },
            "FuelType" => Valid with { FuelType = value },
            _ => Valid with { ImageUrl = value },
        };

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().AddAsync(Sessions.Admin, input, Ct));

        Assert.Equal([field], error.FieldErrors.Keys);
        _cars.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Add_TakenPlate_IsReportedOnThePlateField()
    {
        _cars.Setup(c => c.AddAsync(Sessions.Admin.Actor, It.IsAny<Car>(), It.IsAny<CancellationToken>())).ThrowsAsync(new DuplicatePlateException());

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().AddAsync(Sessions.Admin, Valid, Ct));

        Assert.Equal(CarService.PlateTaken, error.Message);
        Assert.Equal([nameof(CarInput.Plate)], error.FieldErrors.Keys);
        Assert.Empty(_audit.Entries);
    }

    /// <summary>
    /// A gearbox or fuel type of the original data that is not in the lists is kept when the admin
    /// leaves it as it is, so the car can still be changed (for example taken off the list). A changed
    /// value must be one of the lists.
    /// </summary>
    [Fact]
    public async Task Update_LegacyGearboxAndFuelKeptUnchanged_AreAccepted()
    {
        Car? sent = null;
        _cars.Setup(c => c.FindAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Car { Id = 3, Brand = "Anka", Model = "Kent", Transmission = "Manual", FuelType = "Benzinli", Plate = "00 DMO 001", DailyPrice = 850.5m });
        _cars.Setup(c => c.UpdateAsync(Sessions.Admin.Actor, It.IsAny<Car>(), It.IsAny<CancellationToken>()))
            .Callback<Actor, Car, CancellationToken>((_, car, _) => sent = car)
            .ReturnsAsync(CarUpdateOutcome.Updated);

        await Service().UpdateAsync(Sessions.Admin, 3, 0, Valid with { Transmission = "Manual", FuelType = "Benzinli", IsListed = false }, Ct);

        Assert.NotNull(sent);
        Assert.Equal(("Manual", "Benzinli", false), (sent.Transmission, sent.FuelType, sent.IsListed));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => Service().UpdateAsync(Sessions.Admin, 3, 0, Valid with { Transmission = "Yarı Otomatik", FuelType = "Benzinli" }, Ct));
        Assert.Equal([nameof(CarInput.Transmission)], error.FieldErrors.Keys);
    }

    /// <summary>A form loaded before another admin's change is refused before anything is written, and so is a change the repository finds outdated.</summary>
    [Fact]
    public async Task Update_FromAStaleForm_IsRefusedAsStale()
    {
        _cars.Setup(c => c.FindAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Car { Id = 3, Brand = "Anka", Model = "Kent", Transmission = "Otomatik", FuelType = "Benzin", Plate = "00 DMO 001", DailyPrice = 1200m, Version = 2 });

        var early = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().UpdateAsync(Sessions.Admin, 3, 1, Valid, Ct));

        _cars.Verify(c => c.UpdateAsync(It.IsAny<Actor>(), It.IsAny<Car>(), It.IsAny<CancellationToken>()), Times.Never);
        _cars.Setup(c => c.UpdateAsync(Sessions.Admin.Actor, It.Is<Car>(car => car.Id == 3 && car.Version == 2), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CarUpdateOutcome.ChangedElsewhere);
        var late = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().UpdateAsync(Sessions.Admin, 3, 2, Valid, Ct));

        Assert.All([early, late], e => Assert.Equal((CarService.ChangedElsewhere, true), (e.Message, e.IsStale)));
    }

    [Fact]
    public async Task Update_MissingCar_IsNotFound()
    {
        _cars.Setup(c => c.FindAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync((Car?)null);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().UpdateAsync(Sessions.Admin, 3, 0, Valid, Ct));

        Assert.Equal(CarService.NotFound, error.Message);
    }

    [Fact]
    public async Task Delete_CarWithRentals_IsRefused()
    {
        _cars.Setup(c => c.DeleteAsync(Sessions.Admin.Actor, 3, It.IsAny<CancellationToken>())).ReturnsAsync(CarDeleteOutcome.HasRentals);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().DeleteAsync(Sessions.Admin, 3, Ct));

        Assert.Equal(CarService.HasRentals, error.Message);
    }

    [Fact]
    public async Task Get_UnlistedCar_IsMissingForAUserButVisibleToAnAdmin()
    {
        var hidden = new Car { Id = 3, Brand = "Gizli", IsListed = false };
        _cars.Setup(c => c.FindAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(hidden);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().GetAsync(Sessions.User, 3, Ct));

        Assert.Equal(CarService.NotFound, error.Message);
        Assert.Same(hidden, await Service().GetAsync(Sessions.Admin, 3, Ct));
    }

    private CarService Service() => new(_cars.Object, new AccessGuard(_users.Object, _audit), _time, new TestDatabaseClock(_time), _audit, TestValidators.Car);
}
