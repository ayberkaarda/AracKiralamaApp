using System.Globalization;
using AracKiralama.Business;
using AracKiralama.Business.Services;
using AracKiralama.Data.Persistence;
using AracKiralama.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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

    /// <summary>
    /// Admin B has passed the service's role check and is about to promote a customer when the founder
    /// demotes B. The role is checked again inside B's write, so B's promotion is refused and the
    /// customer stays a customer.
    /// </summary>
    [Fact]
    public async Task UpdateAccount_ByAnAdminDemotedMeanwhile_IsRefused()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        var founder = await SetUpAdminAsync(a);
        var accounts = a.GetRequiredService<IAccountService>();
        var b = await accounts.AddAsync(founder, new AccountInput("badmin", Password, null, null, UserRole.Admin), Ct);
        var bSession = await a.GetRequiredService<IAuthService>().SignInAsync("badmin", Password, Ct);
        var customer = await RegisterAsync(a, "uye", 1);
        var gate = CommandGate.BeforeAppLock("AracKiralama.AdminRoles");
        await using var bApp = database.BuildServices(configureContext: o => o.AddInterceptors(gate));

        var promoting = bApp.GetRequiredService<IAccountService>().UpdateAsync(
            bSession, customer.UserId, new AccountInput("uye", null, "uye@example.com", "5540000001", UserRole.Admin), Ct);
        await gate.WaitUntilReachedAsync(Ct);
        await accounts.UpdateAsync(founder, b, new AccountInput("badmin", null, null, null, UserRole.User), Ct);
        gate.Release();

        await Assert.ThrowsAsync<AccessDeniedException>(() => promoting);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE Rol = N'Admin'", Ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciAdi = N'uye' AND Rol = N'Admin'", Ct));
    }

    /// <summary>The same for a car: an admin demoted while the change waits for the car's lock changes nothing.</summary>
    [Fact]
    public async Task UpdateCar_ByAnAdminDemotedMeanwhile_IsRefused()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        var founder = await SetUpAdminAsync(a);
        var accounts = a.GetRequiredService<IAccountService>();
        var b = await accounts.AddAsync(founder, new AccountInput("badmin", Password, null, null, UserRole.Admin), Ct);
        var bSession = await a.GetRequiredService<IAuthService>().SignInAsync("badmin", Password, Ct);
        var carId = await AddCarAsync(a, founder, "00 DMT 01");
        var gate = CommandGate.BeforeAppLock("AracKiralama.Car." + carId.ToString(CultureInfo.InvariantCulture));
        await using var bApp = database.BuildServices(configureContext: o => o.AddInterceptors(gate));

        var changing = bApp.GetRequiredService<ICarService>().UpdateAsync(
            bSession, carId, new CarInput("Anka", "Deneme", "2022", "Manuel", "Benzin", "1000", "Gri", "00 DMT 01", "1", null, true), Ct);
        await gate.WaitUntilReachedAsync(Ct);
        await accounts.UpdateAsync(founder, b, new AccountInput("badmin", null, null, null, UserRole.User), Ct);
        gate.Release();

        await Assert.ThrowsAsync<AccessDeniedException>(() => changing);
        Assert.Equal(100, await database.ScalarAsync("SELECT CAST(GunlukUcret AS int) FROM dbo.Araclar WHERE AracID = @id", Ct, ("@id", carId)));
    }

    /// <summary>
    /// Admin B's car change waits at the car's lock while the founder demotes B and promotes B again. B is
    /// an admin again when the change gets the lock, but its session is from before the change: the write
    /// checks the session version inside its transaction and changes nothing.
    /// </summary>
    [Fact]
    public async Task UpdateCar_ByAnAdminDemotedAndPromotedAgainMeanwhile_IsRefused()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        var founder = await SetUpAdminAsync(a);
        var accounts = a.GetRequiredService<IAccountService>();
        var b = await accounts.AddAsync(founder, new AccountInput("badmin", Password, null, null, UserRole.Admin), Ct);
        var bSession = await a.GetRequiredService<IAuthService>().SignInAsync("badmin", Password, Ct);
        var carId = await AddCarAsync(a, founder, "00 SRM 01");
        var gate = CommandGate.BeforeAppLock("AracKiralama.Car." + carId.ToString(CultureInfo.InvariantCulture));
        await using var bApp = database.BuildServices(configureContext: o => o.AddInterceptors(gate));

        var changing = bApp.GetRequiredService<ICarService>().UpdateAsync(
            bSession, carId, new CarInput("Anka", "Deneme", "2022", "Manuel", "Benzin", "1000", "Gri", "00 SRM 01", "1", null, true), Ct);
        await gate.WaitUntilReachedAsync(Ct);
        await accounts.UpdateAsync(founder, b, new AccountInput("badmin", null, null, null, UserRole.User), Ct);
        await accounts.UpdateAsync(founder, b, new AccountInput("badmin", null, null, null, UserRole.Admin), Ct);
        gate.Release();

        await Assert.ThrowsAsync<AccessDeniedException>(() => changing);
        Assert.Equal(100, await database.ScalarAsync("SELECT CAST(GunlukUcret AS int) FROM dbo.Araclar WHERE AracID = @id", Ct, ("@id", carId)));
        Assert.Equal(2, await database.ScalarAsync("SELECT OturumSurumu FROM dbo.Kullanicilar WHERE KullaniciID = @id", Ct, ("@id", b)));
    }

    /// <summary>
    /// A cancellation holds the car's lock, just before its UPDATE, when another customer books days
    /// that the cancelled rental held. The booking waits for the cancellation instead of deciding on the
    /// old days, and then gets them.
    /// </summary>
    [Fact]
    public async Task Rent_WhileAnOverlappingRentalIsCancelled_WaitsAndThenGetsTheDays()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        var admin = await SetUpAdminAsync(a);
        var first = await RegisterAsync(a, "birinci", 1);
        var second = await RegisterAsync(a, "ikinci", 2);
        var carId = await AddCarAsync(a, admin, "00 YRS 01");
        var held = await RentAsync(a, first, carId, startOffset: 2, days: 3);
        var gate = CommandGate.BeforeCommandWith("UPDATE", "[Kiralama]");
        await using var ending = database.BuildServices(configureContext: o => o.AddInterceptors(gate));

        var cancel = ending.GetRequiredService<IRentalService>().EndAsync(first, held.RentalId, Ct);
        await gate.WaitUntilReachedAsync(Ct);
        var booking = Task.Run(() => RentAsync(a, second, carId, startOffset: 3, days: 1), Ct);
        await database.WaitForBlockedRequestsAsync(1, Ct);
        Assert.False(booking.IsCompleted);

        gate.Release();
        Assert.Equal(RentalEnding.Cancelled, await cancel);
        var receipt = await booking;
        Assert.Equal(1, await ActiveAsync(database, carId));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kiralama WHERE KiralamaID = @id AND Durum = 0", Ct, ("@id", receipt.RentalId)));
    }

    /// <summary>
    /// A price change holds the car's lock, just before its UPDATE, when a customer books at the old
    /// price. The booking waits and is refused as changed; nothing is booked at the old price.
    /// </summary>
    [Fact]
    public async Task Rent_WhileThePriceChanges_WaitsAndIsRefusedAsChanged()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        var admin = await SetUpAdminAsync(a);
        var customer = await RegisterAsync(a, "musteri", 1);
        var carId = await AddCarAsync(a, admin, "00 YRS 02", dailyPrice: "100");
        var rentals = a.GetRequiredService<IRentalService>();
        var quote = rentals.Quote(carId, 100m, rentals.Today.AddDays(1), 2);
        var gate = CommandGate.BeforeCommandWith("UPDATE", "[Araclar]");
        await using var changing = database.BuildServices(configureContext: o => o.AddInterceptors(gate));

        var change = changing.GetRequiredService<ICarService>().UpdateAsync(
            admin, carId, new CarInput("Anka", "Deneme", "2022", "Manuel", "Benzin", "1000", "Gri", "00 YRS 02", "200", null, true), Ct);
        await gate.WaitUntilReachedAsync(Ct);
        var booking = Task.Run(() => rentals.RentAsync(customer, quote, Ct), Ct);
        await database.WaitForBlockedRequestsAsync(1, Ct);
        Assert.False(booking.IsCompleted);

        gate.Release();
        await change;
        Assert.Equal(RentalService.PriceChanged, (await Assert.ThrowsAsync<BusinessRuleException>(() => booking)).Message);
        Assert.Equal(0, await ActiveAsync(database, carId));
    }

    /// <summary>
    /// A deletion holds the car's lock, just before its DELETE, when a customer books the car. The
    /// booking waits and finds no car; no rental points at the deleted car.
    /// </summary>
    [Fact]
    public async Task Rent_WhileTheCarIsDeleted_WaitsAndFindsNoCar()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        var admin = await SetUpAdminAsync(a);
        var customer = await RegisterAsync(a, "musteri", 1);
        var carId = await AddCarAsync(a, admin, "00 YRS 03", dailyPrice: "100");
        var rentals = a.GetRequiredService<IRentalService>();
        var quote = rentals.Quote(carId, 100m, rentals.Today.AddDays(1), 2);
        var gate = CommandGate.BeforeCommandWith("DELETE", "[Araclar]");
        await using var deleting = database.BuildServices(configureContext: o => o.AddInterceptors(gate));

        var delete = deleting.GetRequiredService<ICarService>().DeleteAsync(admin, carId, Ct);
        await gate.WaitUntilReachedAsync(Ct);
        var booking = Task.Run(() => rentals.RentAsync(customer, quote, Ct), Ct);
        await database.WaitForBlockedRequestsAsync(1, Ct);
        Assert.False(booking.IsCompleted);

        gate.Release();
        await delete;
        Assert.Equal(RentalService.CarNotFound, (await Assert.ThrowsAsync<BusinessRuleException>(() => booking)).Message);
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kiralama", Ct));
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
