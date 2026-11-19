using AracKiralama.Business;
using AracKiralama.Business.Security;
using AracKiralama.Business.Services;
using AracKiralama.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using static AracKiralama.Data.Tests.TestServices;

namespace AracKiralama.Data.Tests;

/// <summary>
/// Boundaries of the critical paths on the real services and SQLite: the shortest and longest rental,
/// the booking horizon, a quote that went stale overnight, the day a cancellation becomes a return, the
/// end of a sign-in lock, the kept values of an original account and refused input that must not write
/// anything.
/// </summary>
public sealed class SqliteBoundaryTests : IAsyncLifetime
{
    // Demonstration data: cars 1-5, car 1 at 850 a day.
    private const int CarId = 1;
    private const decimal DailyPrice = 850m;

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

    /// <summary>
    /// The settings allow 1 to 30 days starting from today to 60 days ahead: the limits are accepted,
    /// one step beyond each is refused with its own message before anything is written.
    /// </summary>
    [Fact]
    public async Task RentalLengthAndHorizon_AreLimitedByTheSettings()
    {
        var rentals = _db.Get<IRentalService>();

        Assert.Equal("Gün sayısı 1 ile 30 arasında olmalıdır.", Refusal(() => rentals.Quote(CarId, DailyPrice, Today, 0, Today)));
        Assert.Equal("Gün sayısı 1 ile 30 arasında olmalıdır.", Refusal(() => rentals.Quote(CarId, DailyPrice, Today, 31, Today)));
        Assert.Equal(RentalService.StartInPast, Refusal(() => rentals.Quote(CarId, DailyPrice, Today.AddDays(-1), 1, Today)));
        Assert.Equal("Kiralama en fazla 60 gün sonrası için yapılabilir.", Refusal(() => rentals.Quote(CarId, DailyPrice, Today.AddDays(61), 1, Today)));
        Assert.Equal(0, await RentalCountAsync());

        var shortest = await RentAsync(_db.Services, _customer, CarId, startOffset: 0, days: 1);
        var longest = await RentAsync(_db.Services, _customer, CarId, startOffset: 1, days: 30);
        var furthest = await RentAsync(_db.Services, _customer, CarId, startOffset: 60, days: 1);

        Assert.Equal((Today, Today.AddDays(1), DailyPrice), (shortest.StartDate, shortest.EndDate, shortest.Total));
        Assert.Equal((Today.AddDays(1), Today.AddDays(31), 30 * DailyPrice), (longest.StartDate, longest.EndDate, longest.Total));
        Assert.Equal(Today.AddDays(60), furthest.StartDate);
        Assert.Equal(3, await RentalCountAsync());

        static string Refusal(Func<RentalQuote> quote) => Assert.Throws<BusinessRuleException>(() => quote()).Message;
    }

    /// <summary>A quote for today that is booked after midnight starts in the past by then and is refused.</summary>
    [Fact]
    public async Task QuoteForToday_BookedTheNextDay_IsRefusedAndWritesNothing()
    {
        var rentals = _db.Get<IRentalService>();
        var quote = rentals.Quote(CarId, DailyPrice, Today, 2, Today);

        _db.Clock.Advance(TimeSpan.FromDays(1));
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => rentals.RentAsync(_customer, quote, Ct));

        Assert.Equal(RentalService.StartInPast, error.Message);
        Assert.Equal(0, await RentalCountAsync());
    }

    /// <summary>
    /// Ended the day before its start, a rental is cancelled and keeps no return day; ended on its start
    /// day, it is under way and returned that day.
    /// </summary>
    [Fact]
    public async Task End_TheDayBeforeTheStartCancels_AndOnTheStartDayReturns()
    {
        var rentals = _db.Get<IRentalService>();
        var early = await RentAsync(_db.Services, _customer, CarId, startOffset: 2, days: 3);
        var onTime = await RentAsync(_db.Services, _customer, CarId, startOffset: 5, days: 3);

        _db.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(RentalEnding.Cancelled, await rentals.EndAsync(_customer, early.RentalId, Ct));
        _db.Clock.Advance(TimeSpan.FromDays(4));
        Assert.Equal(RentalEnding.Returned, await rentals.EndAsync(_customer, onTime.RentalId, Ct));

        await using var db = _db.CreateContext();
        var cancelled = await db.Rentals.SingleAsync(r => r.Id == early.RentalId, Ct);
        var returned = await db.Rentals.SingleAsync(r => r.Id == onTime.RentalId, Ct);
        Assert.Equal((RentalStatus.Cancelled, (DateOnly?)null), (cancelled.Status, cancelled.ReturnedOn));
        Assert.Equal((RentalStatus.Returned, (DateOnly?)Today.AddDays(5)), (returned.Status, returned.ReturnedOn));
    }

    /// <summary>
    /// After the fifth failure the account stays locked, also for the right password, until exactly the
    /// end of the fifteen minutes; refusals during the lock neither extend nor shorten it.
    /// </summary>
    [Fact]
    public async Task Lock_LastsExactlyFifteenMinutes()
    {
        var auth = _db.Get<IAuthService>();
        for (var i = 0; i < LoginPolicy.MaxFailedAttempts; i++)
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("ornek", "Yanlis.Sifre1", Ct));
        }

        var lockedUntil = await LockoutEndAsync(_customer.UserId);
        Assert.Equal(Start.UtcDateTime + LoginPolicy.LockoutDuration, lockedUntil);

        _db.Clock.Advance(LoginPolicy.LockoutDuration - TimeSpan.FromSeconds(1));
        var locked = await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("ornek", Password, Ct));
        Assert.Equal(AuthService.CredentialsInvalid, locked.Message);
        await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("ornek", "Yanlis.Sifre1", Ct));
        Assert.Equal(lockedUntil, await LockoutEndAsync(_customer.UserId));

        _db.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(_customer.UserId, (await auth.SignInAsync("ornek", Password, Ct)).UserId);
    }

    /// <summary>Four failures do not lock; a success clears the count, so four more do not lock either.</summary>
    [Fact]
    public async Task Lock_NeedsFiveFailuresInARow()
    {
        var auth = _db.Get<IAuthService>();

        for (var round = 0; round < 2; round++)
        {
            for (var i = 0; i < LoginPolicy.MaxFailedAttempts - 1; i++)
            {
                await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("ornek", "Yanlis.Sifre1", Ct));
            }

            await auth.SignInAsync("ornek", Password, Ct);
        }

        Assert.Null(await LockoutEndAsync(_customer.UserId));
    }

    /// <summary>
    /// An account of the original application with a name, e-mail address and phone number today's rules
    /// refuse can get a new password when the form sends those values unchanged; they are stored exactly
    /// as they were. A changed e-mail address follows the rules and nothing is written.
    /// </summary>
    [Fact]
    public async Task OriginalAccount_KeepsItsValuesWhenSentUnchanged()
    {
        int id;
        await using (var db = _db.CreateContext())
        {
            var legacy = new User
            {
                UserName = "Ali Veli",
                PasswordHash = "eski1",
                PasswordFormat = PasswordFormat.LegacyPlainText,
                Role = UserRole.User,
                Email = "eski adres",
                Phone = "212555",
            };
            db.Users.Add(legacy);
            await db.SaveChangesAsync(Ct);
            id = legacy.Id;
        }

        var accounts = _db.Get<IAccountService>();
        await accounts.UpdateAsync(_admin, id, await AccountVersionAsync(_db.Services, id), new AccountInput(" Ali Veli ", "Yeni.Sifre12", "eski adres ", " 212555", UserRole.User), Ct);
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(
            async () => await accounts.UpdateAsync(_admin, id, await AccountVersionAsync(_db.Services, id), new AccountInput("Ali Veli", "Baska.Sifre12", "yeni adres", "212555", UserRole.User), Ct));

        Assert.Equal("Lütfen geçerli bir e-posta adresi girin.", refused.Message);
        var stored = (await accounts.GetAllAsync(_admin, Ct)).Single(u => u.Id == id);
        Assert.Equal(("Ali Veli", "eski adres", "212555"), (stored.UserName, stored.Email, stored.Phone));
        Assert.Equal(id, (await _db.Get<IAuthService>().SignInAsync("Ali Veli", "Yeni.Sifre12", Ct)).UserId);
    }

    /// <summary>Refused input of every service writes nothing.</summary>
    [Fact]
    public async Task RefusedInput_WritesNothing()
    {
        var auth = _db.Get<IAuthService>();
        var cars = _db.Get<ICarService>();
        var accounts = _db.Get<IAccountService>();
        var setup = _db.Get<ISetupService>();
        int users, carCount, rentalCount;
        await using (var before = _db.CreateContext())
        {
            (users, carCount, rentalCount) = (await before.Users.CountAsync(Ct), await before.Cars.CountAsync(Ct), await before.Rentals.CountAsync(Ct));
        }

        await Assert.ThrowsAsync<BusinessRuleException>(() => auth.RegisterAsync("yeni", "kisa", "yeni@example.com", "5551234567", Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => auth.RegisterAsync("admin", Password, "yeni@example.com", "5551234567", Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => auth.RegisterAsync("yeni", Password, "eposta", "5551234567", Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => accounts.AddAsync(_admin, new AccountInput("yeni", Password, "yeni@example.com", "555", UserRole.User), Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync(null, "kurucu", Password, "Baska.Sifre1", Ct));
        var car = await Assert.ThrowsAsync<BusinessRuleException>(() => cars.AddAsync(
            _admin,
            new CarInput("Anka", "Deneme", "2099", "Manuel", "Benzin", "1000", "Gri", "Ç", "0", "file:///C:/arac.png", true),
            Ct));
        Assert.Equal([nameof(CarInput.Year), nameof(CarInput.Plate), nameof(CarInput.DailyPrice), nameof(CarInput.ImageUrl)], car.FieldErrors.Keys);
        await Assert.ThrowsAsync<BusinessRuleException>(() => auth.ChangePasswordAsync(_customer, Password, "kisa", "kisa", Ct));

        await using var after = _db.CreateContext();
        Assert.Equal(users, await after.Users.CountAsync(Ct));
        Assert.Equal(carCount, await after.Cars.CountAsync(Ct));
        Assert.Equal(rentalCount, await after.Rentals.CountAsync(Ct));
        await auth.SignInAsync("ornek", Password, Ct);
    }

    private async Task<int> RentalCountAsync()
    {
        await using var db = _db.CreateContext();
        return await db.Rentals.CountAsync(r => r.CarId == CarId, Ct);
    }

    private async Task<DateTime?> LockoutEndAsync(int userId)
    {
        await using var db = _db.CreateContext();
        return await db.Users.Where(u => u.Id == userId).Select(u => u.LockoutEnd).SingleAsync(Ct);
    }
}
