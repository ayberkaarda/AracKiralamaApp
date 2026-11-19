using AracKiralama.Business;
using AracKiralama.Business.Services;
using AracKiralama.Data.Persistence;
using AracKiralama.Domain.Entities;
using AracKiralama.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using static AracKiralama.Data.Tests.TestServices;

namespace AracKiralama.Data.Tests;

/// <summary>The real services and repositories on SQLite: booking rules, cancellations, returns, ownership and admin rules.</summary>
public sealed class SqliteServiceTests : IAsyncLifetime
{
    // Demonstration data: cars 1-5; the setup below adds the admin (1) and a customer (2).
    private const int CarId = 1;
    private const int OtherCarId = 3;

    private readonly SqliteDatabase _db = new();
    private UserSession _admin = null!;
    private UserSession _customer = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _db.InitializeAsync();
        _admin = await SetUpAdminAsync(_db.Services);
        _customer = await RegisterAsync(_db.Services, "ornek", 1);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task DemoData_AddsCarsButNoAccount()
    {
        await using var db = _db.CreateContext();
        Assert.Equal(DemoSeedOutcome.Seeded, _db.SeedOutcome);
        Assert.Equal(5, await db.Cars.CountAsync(Ct));
        Assert.All(await db.Cars.ToListAsync(Ct), c => Assert.StartsWith("00 ", c.Plate, StringComparison.Ordinal));

        // The only accounts are the ones this test class created itself.
        Assert.Equal(2, await db.Users.CountAsync(Ct));
    }

    [Fact]
    public async Task Rent_StoresTheHalfOpenRangeAndThePrice()
    {
        var receipt = await RentAsync(_db.Services, _customer, CarId, startOffset: 2, days: 4);

        await using var db = _db.CreateContext();
        var rental = await db.Rentals.SingleAsync(r => r.Id == receipt.RentalId, Ct);
        Assert.Equal(RentalStatus.Active, rental.Status);
        Assert.Equal(Today.AddDays(2), rental.StartDate);
        Assert.Equal(Today.AddDays(6), rental.EndDate);
        Assert.Null(rental.ReturnedOn);
        Assert.Equal(4 * 850m, rental.Price);
    }

    /// <summary>Boundary values of the overlap rule with an existing booking of days 5, 6, 7 and 8 (end day 9).</summary>
    [Theory]
    [InlineData(9, 2, true)]   // starts on the day the other ends: adjacent, allowed
    [InlineData(2, 3, true)]   // ends on the day the other starts: adjacent, allowed
    [InlineData(8, 2, false)]  // shares one day at the end
    [InlineData(3, 3, false)]  // shares one day at the start
    [InlineData(5, 1, false)]  // the same start day
    [InlineData(6, 1, false)]  // inside
    [InlineData(1, 20, false)] // around
    public async Task Rent_OverlapBoundaries(int startOffset, int days, bool allowed)
    {
        await RentAsync(_db.Services, _customer, CarId, startOffset: 5, days: 4);
        var other = await RegisterAsync(_db.Services, "baska", 2);

        if (allowed)
        {
            await RentAsync(_db.Services, other, CarId, startOffset, days);
            Assert.Equal(2, await ActiveAsync(CarId));
        }
        else
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => RentAsync(_db.Services, other, CarId, startOffset, days));
            Assert.Equal(RentalService.Overlaps, error.Message);
            Assert.Equal(1, await ActiveAsync(CarId));
        }
    }

    [Fact]
    public async Task Rent_OtherCarOnTheSameDays_IsAllowed()
    {
        await RentAsync(_db.Services, _customer, CarId, days: 5);

        await RentAsync(_db.Services, _customer, OtherCarId, days: 5);

        Assert.Equal(1, await ActiveAsync(OtherCarId));
    }

    [Fact]
    public async Task Rent_CancelledDays_AreFreeAgain()
    {
        var first = await RentAsync(_db.Services, _customer, CarId, startOffset: 3, days: 3);
        Assert.Equal(RentalEnding.Cancelled, await _db.Get<IRentalService>().EndAsync(_customer, first.RentalId, Ct));

        await RentAsync(_db.Services, _customer, CarId, startOffset: 3, days: 3);

        Assert.Equal(1, await ActiveAsync(CarId));
    }

    [Fact]
    public async Task Rent_PriceChangedAfterTheQuote_IsRefused()
    {
        var rentals = _db.Get<IRentalService>();
        var quote = rentals.Quote(CarId, 850m, Today, 2);
        await UpdateCarAsync(CarId, price: "900");

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => rentals.RentAsync(_customer, quote, Ct));

        Assert.Equal(RentalService.PriceChanged, error.Message);
        Assert.Equal(0, await ActiveAsync(CarId));
    }

    [Fact]
    public async Task Rent_UnlistedCar_IsNotAvailable()
    {
        await UpdateCarAsync(CarId, listed: false);

        var rentals = _db.Get<IRentalService>();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => rentals.RentAsync(_customer, rentals.Quote(CarId, 850m, Today, 1), Ct));

        Assert.Equal(RentalService.NotAvailable, error.Message);
        Assert.DoesNotContain(await _db.Get<ICarService>().GetCatalogAsync(_customer, CarFilter.None, Ct), c => c.Id == CarId);
    }

    [Fact]
    public async Task End_UnderwayRental_IsReturnedTodayAndFreesTheRestOfTheDays()
    {
        var receipt = await RentAsync(_db.Services, _customer, CarId, days: 10);
        _db.Clock.Advance(TimeSpan.FromDays(3));

        Assert.Equal(RentalEnding.Returned, await _db.Get<IRentalService>().EndAsync(_customer, receipt.RentalId, Ct));

        await using var db = _db.CreateContext();
        var rental = await db.Rentals.SingleAsync(r => r.Id == receipt.RentalId, Ct);
        Assert.Equal(RentalStatus.Returned, rental.Status);
        Assert.Equal(Today.AddDays(3), rental.ReturnedOn);
        Assert.Equal(10 * 850m, rental.Price);
        await RentAsync(_db.Services, _customer, CarId, days: 2);
    }

    /// <summary>IDOR: another account's rental cannot be seen or ended, and the answer is that of a missing one.</summary>
    [Fact]
    public async Task End_OtherAccountsRental_GetsTheSameAnswerAsAMissingOneAndChangesNothing()
    {
        var other = await RegisterAsync(_db.Services, "baska", 2);
        var receipt = await RentAsync(_db.Services, _customer, CarId);
        var rentals = _db.Get<IRentalService>();

        var foreign = await Assert.ThrowsAsync<BusinessRuleException>(() => rentals.EndAsync(other, receipt.RentalId, Ct));
        var missing = await Assert.ThrowsAsync<BusinessRuleException>(() => rentals.EndAsync(other, 999, Ct));

        Assert.Equal(missing.Message, foreign.Message);
        Assert.Empty(await rentals.GetMyActiveRentalsAsync(other, Ct));
        Assert.Single(await rentals.GetMyActiveRentalsAsync(_customer, Ct));
        Assert.Equal(1, await ActiveAsync(CarId));

        // An admin may end any rental.
        await rentals.EndAsync(_admin, receipt.RentalId, Ct);
        Assert.Equal(0, await ActiveAsync(CarId));
    }

    [Fact]
    public async Task End_Twice_TheSecondIsRefused()
    {
        var receipt = await RentAsync(_db.Services, _customer, CarId, startOffset: 1);
        var rentals = _db.Get<IRentalService>();
        await rentals.EndAsync(_customer, receipt.RentalId, Ct);

        await Assert.ThrowsAsync<BusinessRuleException>(() => rentals.EndAsync(_customer, receipt.RentalId, Ct));
    }

    [Fact]
    public async Task ActiveRentals_AdminSeesEveryoneAndCanFilterByCar()
    {
        var other = await RegisterAsync(_db.Services, "baska", 2);
        await RentAsync(_db.Services, _customer, CarId);
        await RentAsync(_db.Services, other, OtherCarId);
        var rentals = _db.Get<IRentalService>();

        Assert.Equal(2, (await rentals.GetActiveRentalsAsync(_admin, CarFilter.None, Ct)).Count);
        var diesel = await rentals.GetActiveRentalsAsync(_admin, new CarFilter(FuelType: "Dizel"), Ct);
        Assert.Equal(OtherCarId, Assert.Single(diesel).CarId);
        await Assert.ThrowsAsync<AccessDeniedException>(() => rentals.GetActiveRentalsAsync(_customer, CarFilter.None, Ct));
    }

    [Fact]
    public async Task BookedRanges_ShowTheTakenDaysFromToday()
    {
        await RentAsync(_db.Services, _customer, CarId, startOffset: 5, days: 2);
        await RentAsync(_db.Services, _customer, CarId, startOffset: 1, days: 2);

        var ranges = await _db.Get<IRentalService>().GetBookedRangesAsync(_customer, CarId, Ct);

        Assert.Equal([new BookedRange(Today.AddDays(1), Today.AddDays(3)), new BookedRange(Today.AddDays(5), Today.AddDays(7))], ranges);
    }

    /// <summary>A demoted admin's session loses the admin rights at once, also on a real database.</summary>
    [Fact]
    public async Task DemotedAdminSession_CannotManageVehiclesOrAccounts()
    {
        var accounts = _db.Get<IAccountService>();
        var second = await accounts.AddAsync(_admin, new AccountInput("ikinci", Password, null, null, UserRole.Admin), Ct);
        var secondSession = await _db.Get<IAuthService>().SignInAsync("ikinci", Password, Ct);
        Assert.True(secondSession.IsAdmin);

        await accounts.UpdateAsync(_admin, second, new AccountInput("ikinci", null, null, null, UserRole.User), Ct);

        await Assert.ThrowsAsync<AccessDeniedException>(() => AddCarAsync(_db.Services, secondSession, "00 YNT 01"));
        await Assert.ThrowsAsync<AccessDeniedException>(() => accounts.UpdateAsync(secondSession, second, new AccountInput("ikinci", null, null, null, UserRole.Admin), Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => accounts.GetAllAsync(secondSession, Ct));
        Assert.Equal(UserRole.User, (await accounts.GetAllAsync(_admin, Ct)).Single(u => u.Id == second).Role);
    }

    /// <summary>
    /// An admin demoted and promoted again keeps the role, but the session from before the change stays
    /// closed for good; only a new sign-in works again.
    /// </summary>
    [Fact]
    public async Task DemotedAndPromotedAgain_OldSessionStaysClosed()
    {
        var accounts = _db.Get<IAccountService>();
        var second = await accounts.AddAsync(_admin, new AccountInput("ikinci", Password, null, null, UserRole.Admin), Ct);
        var old = await _db.Get<IAuthService>().SignInAsync("ikinci", Password, Ct);

        await accounts.UpdateAsync(_admin, second, new AccountInput("ikinci", null, null, null, UserRole.User), Ct);
        await accounts.UpdateAsync(_admin, second, new AccountInput("ikinci", null, null, null, UserRole.Admin), Ct);

        await Assert.ThrowsAsync<AccessDeniedException>(() => AddCarAsync(_db.Services, old, "00 SRM 01"));
        await Assert.ThrowsAsync<AccessDeniedException>(() => accounts.GetAllAsync(old, Ct));
        var fresh = await _db.Get<IAuthService>().SignInAsync("ikinci", Password, Ct);
        Assert.Equal(2, fresh.SessionVersion);
        Assert.True(await AddCarAsync(_db.Services, fresh, "00 SRM 02") > 0);
    }

    /// <summary>A password an admin sets ends the account's open sessions; a change of only the contact data does not.</summary>
    [Fact]
    public async Task PasswordSetByAnAdmin_EndsTheAccountsSessions()
    {
        var accounts = _db.Get<IAccountService>();
        await accounts.UpdateAsync(_admin, _customer.UserId, new AccountInput("ornek", null, "yeni@example.com", "5540000001", UserRole.User), Ct);
        Assert.Empty(await _db.Get<IRentalService>().GetMyActiveRentalsAsync(_customer, Ct));

        await accounts.UpdateAsync(_admin, _customer.UserId, new AccountInput("ornek", "Yeni.Sifre123", "yeni@example.com", "5540000001", UserRole.User), Ct);

        await Assert.ThrowsAsync<AccessDeniedException>(() => _db.Get<IRentalService>().GetMyActiveRentalsAsync(_customer, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => RentAsync(_db.Services, _customer, CarId, startOffset: 1));
        await using var db = _db.CreateContext();
        Assert.Equal(0, await db.Rentals.CountAsync(Ct));
    }

    /// <summary>A sign-in verified before a role change cannot issue a session with the old version.</summary>
    [Fact]
    public async Task SignIn_RecordedAfterARoleChange_IsRefused()
    {
        var users = _db.Get<IUserRepository>();
        var verified = (await users.FindByUserNameAsync("ornek", Ct))!;
        await _db.Get<IAccountService>().UpdateAsync(_admin, _customer.UserId, new AccountInput("ornek", null, "ornek@example.com", "5540000001", UserRole.Admin), Ct);

        Assert.False(await users.RecordSuccessfulLoginAsync(verified, Start.UtcDateTime, null, Ct));
    }

    /// <summary>
    /// Every repository write checks the acting account itself, whatever the service checked before:
    /// a customer's id acting as an admin, or an account that does not exist, is refused and nothing is
    /// written.
    /// </summary>
    [Fact]
    public async Task Repositories_RefuseAnActorWithoutTheRoleAndWriteNothing()
    {
        var users = _db.Get<IUserRepository>();
        var cars = _db.Get<ICarRepository>();
        var rentals = _db.Get<IRentalRepository>();
        var receipt = await RentAsync(_db.Services, _customer, CarId, startOffset: 1);
        var customer = _customer.UserId;
        var asCustomer = _customer.Actor;
        var car = (await cars.FindAsync(CarId, Ct))!;
        car.DailyPrice = 1m;
        var newcomer = new User { UserName = "yeni", PasswordHash = "x", Role = UserRole.Admin };
        List<Func<Task>> refused =
        [
            () => users.AddAsync(newcomer, asCustomer, Ct),
            () => users.UpdateAsync(asCustomer, new AccountUpdate(customer, "ornek", null, null, UserRole.Admin, null), Ct),
            () => users.DeleteAsync(asCustomer, _admin.UserId, Ct),
            () => cars.AddAsync(asCustomer, new Car { Brand = "A", Model = "B", Year = 2020, Transmission = "Manuel", FuelType = "Benzin", Color = "Gri", Plate = "00 YOK 01", DailyPrice = 1m }, Ct),
            () => cars.UpdateAsync(asCustomer, car, Ct),
            () => cars.DeleteAsync(asCustomer, OtherCarId, Ct),
            () => rentals.EndAsync(receipt.RentalId, new RentalActor(customer, AsAdmin: true), Today, Ct),
            () => rentals.EndAsync(receipt.RentalId, new RentalActor(999, AsAdmin: false), Today, Ct),
        ];

        foreach (var operation in refused)
        {
            await Assert.ThrowsAsync<ActorRevokedException>(operation);
        }

        await using var db = _db.CreateContext();
        Assert.Equal(2, await db.Users.CountAsync(Ct));
        Assert.Equal(UserRole.User, await db.Users.Where(u => u.Id == customer).Select(u => u.Role).SingleAsync(Ct));
        Assert.Equal(5, await db.Cars.CountAsync(Ct));
        Assert.Equal(850m, await db.Cars.Where(c => c.Id == CarId).Select(c => c.DailyPrice).SingleAsync(Ct));
        Assert.Equal(1, await ActiveAsync(CarId));
    }

    [Fact]
    public async Task AddCar_TakenPlate_IsRefused()
    {
        await AddCarAsync(_db.Services, _admin, "00 YNI 01");

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => AddCarAsync(_db.Services, _admin, "00 yni 01"));

        Assert.Equal(CarService.PlateTaken, error.Message);
    }

    [Fact]
    public async Task DeleteCar_WithRentalHistory_IsRefusedAndWithoutIsDone()
    {
        var receipt = await RentAsync(_db.Services, _customer, CarId, startOffset: 1);
        await _db.Get<IRentalService>().EndAsync(_customer, receipt.RentalId, Ct);
        var cars = _db.Get<ICarService>();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => cars.DeleteAsync(_admin, CarId, Ct));
        Assert.Equal(CarService.HasRentals, error.Message);

        var id = await AddCarAsync(_db.Services, _admin, "00 SIL 01");
        await cars.DeleteAsync(_admin, id, Ct);
        await using var db = _db.CreateContext();
        Assert.False(await db.Cars.AnyAsync(c => c.Id == id, Ct));
    }

    [Fact]
    public async Task DeleteAccount_AdminOrWithRentals_IsRefused()
    {
        var accounts = _db.Get<IAccountService>();
        var unused = await RegisterAsync(_db.Services, "bos", 3);
        await RentAsync(_db.Services, _customer, CarId);

        Assert.Equal(AccountService.AdminNotDeletable, (await Assert.ThrowsAsync<BusinessRuleException>(() => accounts.DeleteAsync(_admin, _admin.UserId, Ct))).Message);
        Assert.Equal(AccountService.HasRentals, (await Assert.ThrowsAsync<BusinessRuleException>(() => accounts.DeleteAsync(_admin, _customer.UserId, Ct))).Message);

        await accounts.DeleteAsync(_admin, unused.UserId, Ct);
        Assert.DoesNotContain(await accounts.GetAllAsync(_admin, Ct), u => u.Id == unused.UserId);
    }

    [Fact]
    public async Task UpdateAccount_LastAdminKeepsTheRole()
    {
        var accounts = _db.Get<IAccountService>();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => accounts.UpdateAsync(_admin, _admin.UserId, new AccountInput("yonetici", null, null, null, UserRole.User), Ct));

        Assert.Equal(AccountService.LastAdmin, error.Message);
    }

    [Fact]
    public async Task SignIn_FiveFailures_LockForFifteenMinutes()
    {
        var auth = _db.Get<IAuthService>();
        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("ornek", "Yanlis.Sifre1", Ct));
        }

        var locked = await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("ornek", Password, Ct));
        Assert.Equal(AuthService.CredentialsInvalid, locked.Message);
        _db.Clock.Advance(TimeSpan.FromMinutes(15));
        await auth.SignInAsync("ornek", Password, Ct);
    }

    private async Task<int> ActiveAsync(int carId)
    {
        await using var db = _db.CreateContext();
        return await db.Rentals.CountAsync(r => r.CarId == carId && r.Status == RentalStatus.Active, Ct);
    }

    private async Task UpdateCarAsync(int carId, string? price = null, bool? listed = null)
    {
        var cars = _db.Get<ICarService>();
        var car = await cars.GetAsync(_admin, carId, Ct);
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        await cars.UpdateAsync(
            _admin,
            carId,
            new CarInput(
                car.Brand,
                car.Model,
                car.Year.ToString(culture),
                car.Transmission,
                car.FuelType,
                car.Mileage.ToString(culture),
                car.Color,
                car.Plate,
                price ?? car.DailyPrice.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("tr-TR")),
                car.ImageUrl,
                listed ?? car.IsListed),
            Ct);
    }
}

/// <summary>A default installation: no demonstration data, no account, the first-run setup.</summary>
public sealed class FirstRunTests : IAsyncLifetime
{
    private readonly SqliteDatabase _db = new(demo: false);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => _db.InitializeAsync();

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task EmptyDatabase_HasNoAccountAndAsksForTheSetup()
    {
        Assert.Equal(DemoSeedOutcome.NotRequested, _db.SeedOutcome);
        await using var db = _db.CreateContext();
        Assert.False(await db.Users.AnyAsync(Ct));
        Assert.True(await _db.Get<ISetupService>().IsSetupRequiredAsync(Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => _db.Get<IAuthService>().SignInAsync("admin", "admin", Ct));
    }

    [Fact]
    public async Task Setup_OnAnEmptyDatabase_CreatesTheFirstAdminOnce()
    {
        var setup = _db.Get<ISetupService>();

        var session = await setup.CreateFirstAdminAsync(null, "kurucu", Password, Password, Ct);
        var again = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync(null, "ikinci", Password, Password, Ct));

        Assert.True(session.IsAdmin);
        Assert.Equal(SetupService.AlreadyDone, again.Message);
        Assert.True((await _db.Get<IAuthService>().SignInAsync("kurucu", Password, Ct)).IsAdmin);
    }

    [Fact]
    public async Task Setup_WithAccountsButNoAdmin_NeedsAKeyThatWorksOnce()
    {
        await RegisterAsync(_db.Services, "musteri", 1);
        var setup = _db.Get<ISetupService>();

        Assert.False(await setup.IsSetupRequiredAsync(Ct));
        Assert.True(await setup.IsAdminMissingAsync(Ct));
        Assert.Equal(SetupService.SetupKeyRequired, (await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync(null, "kurucu", Password, Password, Ct))).Message);
        Assert.Equal(SetupService.SetupKeyInvalid, (await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync("0000-0000-0000-0000-0000-0000-0000-0000", "kurucu", Password, Password, Ct))).Message);

        var key = await AddSetupKeyAsync(_db.Services, Start);
        await setup.CreateFirstAdminAsync(key, "kurucu", Password, Password, Ct);

        Assert.Equal(SetupService.AlreadyDone, (await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync(key, "ikinci", Password, Password, Ct))).Message);
        await using var db = _db.CreateContext();
        Assert.True(await db.SetupKeys.AllAsync(k => k.Consumed, Ct));
    }

    [Fact]
    public async Task Setup_ExpiredKey_IsRefused()
    {
        await RegisterAsync(_db.Services, "musteri", 1);
        var key = await AddSetupKeyAsync(_db.Services, Start);
        _db.Clock.Advance(TimeSpan.FromHours(25));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => _db.Get<ISetupService>().CreateFirstAdminAsync(key, "kurucu", Password, Password, Ct));

        Assert.Equal(SetupService.SetupKeyInvalid, error.Message);
    }

    [Fact]
    public async Task DemoMode_OnADatabaseWithData_AddsNothing()
    {
        await _db.Get<ISetupService>().CreateFirstAdminAsync(null, "kurucu", Password, Password, Ct);

        await using var demo = _db.BuildServices(demo: true);
        var outcome = await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<IDatabaseInitializer>(demo).InitializeAsync(Ct);

        Assert.Equal(DemoSeedOutcome.SkippedExistingData, outcome);
        await using var db = _db.CreateContext();
        Assert.Equal(1, await db.Users.CountAsync(Ct));
        Assert.False(await db.Cars.AnyAsync(Ct));
    }
}
