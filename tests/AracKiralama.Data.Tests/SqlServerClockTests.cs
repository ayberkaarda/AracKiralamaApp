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
        await using var behind = database.BuildServices(clock: await ClockAsync(database, TimeSpan.FromDays(-2)));
        var rentals = behind.GetRequiredService<IRentalService>();

        // Tomorrow by the wrong clock is yesterday by the database's.
        var quote = rentals.Quote(carId, 100m, rentals.Today.AddDays(1), 2);
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

    /// <summary>A computer clock that is <paramref name="offset"/> away from the database server's clock.</summary>
    private static async Task<FakeTimeProvider> ClockAsync(SqlServerTestDatabase database, TimeSpan offset) =>
        new(await database.ServerUtcNowAsync(Ct) + offset);
}
