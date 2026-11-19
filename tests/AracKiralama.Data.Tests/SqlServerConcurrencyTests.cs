using System.Globalization;
using AracKiralama.Business;
using AracKiralama.Business.Services;
using AracKiralama.Data.Persistence;
using AracKiralama.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using static AracKiralama.Data.Tests.TestServices;

namespace AracKiralama.Data.Tests;

/// <summary>
/// Races on a real SQL Server instance (see <see cref="SqlServerTestDatabase"/>), each side with its own
/// services and connection pool, as two application instances: customers race for overlapping days of
/// one car, adjacent bookings race, a rental is ended twice at once, failed sign-ins arrive together and
/// two admins demote each other.
/// </summary>
public sealed class SqlServerConcurrencyTests
{
    private const int Rounds = 6;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Rent_TwoCustomersBookOverlappingDaysOfOneCar_ExactlyOneGetsThem()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        var admin = await SetUpAdminAsync(a);
        var first = await RegisterAsync(a, "birinci", 1);
        var second = await RegisterAsync(a, "ikinci", 2);

        for (var round = 0; round < Rounds; round++)
        {
            var carId = await AddCarAsync(a, admin, Plate("OV", round));

            // Days 1-3 and 3-5: one shared day.
            var results = await RaceAsync(
                [() => RentAsync(a, first, carId, startOffset: 1, days: 3), () => RentAsync(b, second, carId, startOffset: 3, days: 3)]);

            Assert.Single(results, r => r is null);
            Assert.Equal(RentalService.Overlaps, Assert.IsType<BusinessRuleException>(Assert.Single(results, r => r is not null)).Message);
            Assert.Equal(1, await ActiveAsync(database, carId));
        }
    }

    [Fact]
    public async Task Rent_ManyCustomersRaceForTheSameDays_OnlyOneBookingIsStored()
    {
        const int Customers = 10;
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        var admin = await SetUpAdminAsync(a);
        var sessions = new List<UserSession>();
        for (var i = 0; i < Customers; i++)
        {
            sessions.Add(await RegisterAsync(a, "musteri" + i.ToString(CultureInfo.InvariantCulture), 100 + i));
        }

        for (var round = 0; round < Rounds; round++)
        {
            var carId = await AddCarAsync(a, admin, Plate("MN", round));

            // Every customer wants a different range, but all of them share day 5.
            var results = await RaceAsync(sessions.Select((s, i) => (Func<Task>)(() => RentAsync(i % 2 == 0 ? a : b, s, carId, startOffset: 5 - (i % 3), days: 1 + (i % 3) + (i % 2)))).ToList());

            Assert.Single(results, r => r is null);
            Assert.All(results.Where(r => r is not null), r => Assert.Equal(RentalService.Overlaps, Assert.IsType<BusinessRuleException>(r).Message));
            Assert.Equal(1, await ActiveAsync(database, carId));
            Assert.Equal(0, await database.ScalarAsync(
                """
                SELECT COUNT(*) FROM dbo.Kiralama k JOIN dbo.Kiralama o
                  ON o.AracID = k.AracID AND o.KiralamaID <> k.KiralamaID AND o.Durum = 0 AND k.Durum = 0
                 AND o.AlisTarihi < k.IadeTarihi AND k.AlisTarihi < o.IadeTarihi
                WHERE k.AracID = @id
                """,
                Ct,
                ("@id", carId)));
        }
    }

    [Fact]
    public async Task Rent_AdjacentRangesAtOnce_AreBothStored()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        var admin = await SetUpAdminAsync(a);
        var first = await RegisterAsync(a, "birinci", 1);
        var second = await RegisterAsync(a, "ikinci", 2);

        for (var round = 0; round < Rounds; round++)
        {
            var carId = await AddCarAsync(a, admin, Plate("AD", round));

            // Days 1-2 (returned on day 3) and 3-4: they touch, they do not overlap.
            var results = await RaceAsync(
                [() => RentAsync(a, first, carId, startOffset: 1, days: 2), () => RentAsync(b, second, carId, startOffset: 3, days: 2)]);

            Assert.All(results, Assert.Null);
            Assert.Equal(2, await ActiveAsync(database, carId));
        }
    }

    [Fact]
    public async Task End_SameRentalTwiceAtOnce_EndsItOnce()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        var admin = await SetUpAdminAsync(a);
        var customer = await RegisterAsync(a, "iade", 1);

        for (var round = 0; round < Rounds; round++)
        {
            var carId = await AddCarAsync(a, admin, Plate("EN", round));
            var receipt = await RentAsync(a, customer, carId, startOffset: 1);

            var results = await RaceAsync(
            [
                () => a.GetRequiredService<IRentalService>().EndAsync(customer, receipt.RentalId, Ct),
                () => b.GetRequiredService<IRentalService>().EndAsync(admin, receipt.RentalId, Ct),
            ]);

            Assert.Single(results, r => r is null);
            Assert.Equal(RentalService.RentalNotFound, Assert.IsType<BusinessRuleException>(Assert.Single(results, r => r is not null)).Message);
            Assert.Equal(0, await ActiveAsync(database, carId));
            Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kiralama WHERE AracID = @id AND Durum = 2", Ct, ("@id", carId)));
        }
    }

    [Fact]
    public async Task SignIn_ConcurrentFailures_AreAllCountedAndLockTheAccount()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        await RegisterAsync(a, "kilit", 1);

        await RaceAsync(Enumerable.Range(0, 5).Select(i => (Func<Task>)(() => (i % 2 == 0 ? a : b).GetRequiredService<IAuthService>().SignInAsync("kilit", "Yanlis.Sifre1", Ct))).ToList());

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => a.GetRequiredService<IAuthService>().SignInAsync("kilit", Password, Ct));
        Assert.Equal(AuthService.CredentialsInvalid, error.Message);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciAdi = N'kilit' AND KilitBitis IS NOT NULL AND BasarisizGiris = 0", Ct));
    }

    [Fact]
    public async Task UpdateAccount_TwoAdminsDemoteEachOther_OneAdminRemains()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        var founder = await SetUpAdminAsync(a);
        var accounts = a.GetRequiredService<IAccountService>();
        var auth = a.GetRequiredService<IAuthService>();
        var x = await accounts.AddAsync(founder, new AccountInput("xadmin", Password, null, null, UserRole.Admin), Ct);
        var y = await accounts.AddAsync(founder, new AccountInput("yadmin", Password, null, null, UserRole.Admin), Ct);
        var xSession = await auth.SignInAsync("xadmin", Password, Ct);
        var ySession = await auth.SignInAsync("yadmin", Password, Ct);

        // The founder steps down, so x and y are the only admins.
        await accounts.UpdateAsync(xSession, founder.UserId, new AccountInput("yonetici", null, null, null, UserRole.User), Ct);

        var results = await RaceAsync(
        [
            () => a.GetRequiredService<IAccountService>().UpdateAsync(xSession, y, new AccountInput("yadmin", null, null, null, UserRole.User), Ct),
            () => b.GetRequiredService<IAccountService>().UpdateAsync(ySession, x, new AccountInput("xadmin", null, null, null, UserRole.User), Ct),
        ]);

        Assert.Single(results, r => r is null);
        Assert.Contains(results, r => r is BusinessRuleException { Message: AccountService.LastAdmin } or AccessDeniedException);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE Rol = N'Admin'", Ct));
    }

    private static string Plate(string letters, int round) => "00 " + letters + " " + round.ToString("00", CultureInfo.InvariantCulture);

    private static Task<int> ActiveAsync(SqlServerTestDatabase database, int carId) =>
        database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kiralama WHERE AracID = @id AND Durum = 0", Ct, ("@id", carId));

    /// <summary>
    /// Starts every operation at the same moment and returns, per operation, the exception it ended with
    /// or <see langword="null"/>. A database error other than a refusal fails the test.
    /// </summary>
    private static async Task<IReadOnlyList<Exception?>> RaceAsync(IReadOnlyList<Func<Task>> operations)
    {
        using var start = new ManualResetEventSlim(false);
        var tasks = operations
            .Select(operation => Task.Run(
                async () =>
                {
                    start.Wait(Ct);
                    try
                    {
                        await operation();
                        return (Exception?)null;
                    }
                    catch (Exception ex) when (ex is BusinessRuleException or AccessDeniedException)
                    {
                        return ex;
                    }
                },
                Ct))
            .ToList();
        start.Set();
        var results = await Task.WhenAll(tasks);
        Assert.DoesNotContain(results, r => r is SqlException);
        return results;
    }
}
