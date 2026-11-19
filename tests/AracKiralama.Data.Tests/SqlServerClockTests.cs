using AracKiralama.Business;
using AracKiralama.Business.Services;
using AracKiralama.Data.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using static AracKiralama.Data.Tests.TestServices;

namespace AracKiralama.Data.Tests;

/// <summary>
/// Two application instances on one SQL Server database (see <see cref="SqlServerTestDatabase"/>),
/// one of them on a computer whose clock is wrong. Sign-in locks, the first day a rental may start and
/// whether ending a rental cancels or returns it are decided by the database's clock, so the wrong
/// clock changes none of them.
/// </summary>
public sealed class SqlServerClockTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A rental that started today (by the database) is ended from a computer whose clock is two days
    /// behind: it is returned today, not cancelled as if it had never started.
    /// </summary>
    [Fact]
    public async Task End_ClientClockBehind_StillReturnsAnUnderwayRental()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        var admin = await SetUpAdminAsync(a);
        var customer = await RegisterAsync(a, "musteri", 1);
        var carId = await AddCarAsync(a, admin, "00 SAT 01");
        var receipt = await RentAsync(a, customer, carId, startOffset: 0, days: 3);
        await using var behind = database.BuildServices(clock: await ClockAsync(database, TimeSpan.FromDays(-2)));

        var ending = await behind.GetRequiredService<IRentalService>().EndAsync(customer, receipt.RentalId, Ct);

        Assert.Equal(RentalEnding.Returned, ending);
        Assert.Equal(1, await database.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.Kiralama WHERE KiralamaID = @id AND Durum = 1 AND GercekIadeTarihi = CAST(SYSUTCDATETIME() AS date)",
            Ct,
            ("@id", receipt.RentalId)));
    }

    /// <summary>A computer whose clock is behind cannot book days that are already past by the database.</summary>
    [Fact]
    public async Task Rent_ClientClockBehind_CannotStartInThePast()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        var admin = await SetUpAdminAsync(a);
        var customer = await RegisterAsync(a, "musteri", 1);
        var carId = await AddCarAsync(a, admin, "00 SAT 02");
        var behindClock = await ClockAsync(database, TimeSpan.FromDays(-2));
        await using var behind = database.BuildServices(clock: behindClock);
        var rentals = behind.GetRequiredService<IRentalService>();

        // Tomorrow by the wrong clock is yesterday by the database's; a screen that trusted the wrong
        // clock would offer it.
        var wrongToday = DateOnly.FromDateTime(behindClock.GetUtcNow().UtcDateTime);
        var quote = rentals.Quote(carId, 100m, wrongToday.AddDays(1), 2, wrongToday);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => rentals.RentAsync(customer, quote, Ct));

        Assert.Equal(RentalService.StartInPast, error.Message);
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kiralama", Ct));
    }

    /// <summary>
    /// A computer whose clock is a day ahead does not see a sign-in lock as over, and one whose clock is
    /// a day behind does not see a lock that is over as still running.
    /// </summary>
    [Fact]
    public async Task SignIn_ClientClockAheadOrBehind_FollowsTheLockByTheDatabaseClock()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await RegisterAsync(a, "kilit", 1);
        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() => a.GetRequiredService<IAuthService>().SignInAsync("kilit", "Yanlis.Sifre1", Ct));
        }

        await using var ahead = database.BuildServices(clock: await ClockAsync(database, TimeSpan.FromDays(1)));
        var locked = await Assert.ThrowsAsync<BusinessRuleException>(() => ahead.GetRequiredService<IAuthService>().SignInAsync("kilit", Password, Ct));
        Assert.Equal(AuthService.CredentialsInvalid, locked.Message);
        Assert.Equal(1, await database.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciAdi = N'kilit' AND KilitBitis > DATEADD(MINUTE, 14, SYSUTCDATETIME())", Ct));

        // The lock ends by the database's clock.
        await database.ExecuteAsync("UPDATE dbo.Kullanicilar SET KilitBitis = DATEADD(SECOND, -1, SYSUTCDATETIME()) WHERE KullaniciAdi = N'kilit'", Ct);
        await using var behind = database.BuildServices(clock: await ClockAsync(database, TimeSpan.FromDays(-1)));
        Assert.Equal("kilit", (await behind.GetRequiredService<IAuthService>().SignInAsync("kilit", Password, Ct)).UserName);
    }

    /// <summary>
    /// The newest model year a car may have is next year by the database's clock: a computer whose clock
    /// is years ahead cannot store a car from the future, and one whose clock is years behind can still
    /// store next year's model.
    /// </summary>
    [Fact]
    public async Task AddCar_ClientClockYearsAheadOrBehind_FollowsTheModelYearByTheDatabaseClock()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        var admin = await SetUpAdminAsync(a);
        var nextYear = (await database.ServerUtcNowAsync(Ct)).UtcDateTime.Year + 1;
        await using var ahead = database.BuildServices(clock: await ClockAsync(database, TimeSpan.FromDays(3 * 366)));
        await using var behind = database.BuildServices(clock: await ClockAsync(database, TimeSpan.FromDays(-3 * 366)));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => ahead.GetRequiredService<ICarService>().AddAsync(admin, Car("00 YIL 01", nextYear + 1), Ct));
        Assert.True(error.FieldErrors.ContainsKey(nameof(CarInput.Year)));
        await behind.GetRequiredService<ICarService>().AddAsync(admin, Car("00 YIL 02", nextYear), Ct);

        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Araclar", Ct));

        static CarInput Car(string plate, int year) =>
            new("Anka", "Deneme", year.ToString(System.Globalization.CultureInfo.InvariantCulture), "Manuel", "Benzin", "1000", "Gri", plate, "100", null, true);
    }

    /// <summary>A computer clock that is <paramref name="offset"/> away from the database server's clock.</summary>
    private static async Task<FakeTimeProvider> ClockAsync(SqlServerTestDatabase database, TimeSpan offset) =>
        new(await database.ServerUtcNowAsync(Ct) + offset);
}
