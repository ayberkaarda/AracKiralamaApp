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
        await accounts.UpdateAsync(xSession, founder.UserId, await AccountVersionAsync(a, founder.UserId), new AccountInput("yonetici", null, null, null, UserRole.User), Ct);

        var results = await RaceAsync(
        [
            () => a.GetRequiredService<IAccountService>().UpdateAsync(xSession, y, 0, new AccountInput("yadmin", null, null, null, UserRole.User), Ct),
            () => b.GetRequiredService<IAccountService>().UpdateAsync(ySession, x, 0, new AccountInput("xadmin", null, null, null, UserRole.User), Ct),
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
            bSession, customer.UserId, 0, new AccountInput("uye", null, "uye@example.com", "5540000001", UserRole.Admin), Ct);
        await gate.WaitUntilReachedAsync(Ct);
        await accounts.UpdateAsync(founder, b, await AccountVersionAsync(a, b), new AccountInput("badmin", null, null, null, UserRole.User), Ct);
        gate.Release();

        await Assert.ThrowsAsync<AccessDeniedException>(() => promoting);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE Rol = N'Admin'", Ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciAdi = N'uye' AND Rol = N'Admin'", Ct));
    }

    /// <summary>
    /// A phone change from a form loaded before has passed the service's version check and waits for the
    /// admin role lock while another admin corrects the e-mail address. The waiting change's conditional
    /// UPDATE finds a newer edit version and writes nothing, so the old address does not come back; the
    /// customer's sessions stay valid, as a change of contact data leaves the session version alone.
    /// </summary>
    [Fact]
    public async Task UpdateAccount_ContactDataEditedWhileWaitingForTheLock_IsRefusedAsStale()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        var founder = await SetUpAdminAsync(a);
        var customer = await RegisterAsync(a, "uye", 1);
        var version = await AccountVersionAsync(a, customer.UserId);
        var gate = CommandGate.BeforeAppLock("AracKiralama.AdminRoles");
        await using var bApp = database.BuildServices(configureContext: o => o.AddInterceptors(gate));

        var stale = bApp.GetRequiredService<IAccountService>().UpdateAsync(
            founder, customer.UserId, version, new AccountInput("uye", null, "uye@example.com", "5540000099", UserRole.User), Ct);
        await gate.WaitUntilReachedAsync(Ct);
        await a.GetRequiredService<IAccountService>().UpdateAsync(
            founder, customer.UserId, version, new AccountInput("uye", null, "duzeltilmis@example.com", "5540000001", UserRole.User), Ct);
        gate.Release();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => stale);
        Assert.Equal((AccountService.ChangedElsewhere, true), (error.Message, error.IsStale));
        Assert.Equal(1, await database.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciID = @id AND Eposta = N'duzeltilmis@example.com' AND Telefon = '5540000001' AND DuzenlemeSurumu = 1 AND OturumSurumu = 0",
            Ct,
            ("@id", customer.UserId)));
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
            bSession, carId, await CarVersionAsync(a, carId), new CarInput("Anka", "Deneme", "2022", "Manuel", "Benzin", "1000", "Gri", "00 DMT 01", "1", null, true), Ct);
        await gate.WaitUntilReachedAsync(Ct);
        await accounts.UpdateAsync(founder, b, await AccountVersionAsync(a, b), new AccountInput("badmin", null, null, null, UserRole.User), Ct);
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
            bSession, carId, await CarVersionAsync(a, carId), new CarInput("Anka", "Deneme", "2022", "Manuel", "Benzin", "1000", "Gri", "00 SRM 01", "1", null, true), Ct);
        await gate.WaitUntilReachedAsync(Ct);
        await accounts.UpdateAsync(founder, b, await AccountVersionAsync(a, b), new AccountInput("badmin", null, null, null, UserRole.User), Ct);
        await accounts.UpdateAsync(founder, b, await AccountVersionAsync(a, b), new AccountInput("badmin", null, null, null, UserRole.Admin), Ct);
        gate.Release();

        await Assert.ThrowsAsync<AccessDeniedException>(() => changing);
        Assert.Equal(100, await database.ScalarAsync("SELECT CAST(GunlukUcret AS int) FROM dbo.Araclar WHERE AracID = @id", Ct, ("@id", carId)));
        Assert.Equal(2, await database.ScalarAsync("SELECT OturumSurumu FROM dbo.Kullanicilar WHERE KullaniciID = @id", Ct, ("@id", b)));
    }

    /// <summary>
    /// Two admins loaded the same car and save at once from two application instances: exactly one change
    /// is stored, the other is refused as stale, and the version rises once.
    /// </summary>
    [Fact]
    public async Task UpdateCar_TwoAdminsSaveTheSameLoadedVersion_ExactlyOneWins()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        var admin = await SetUpAdminAsync(a);

        for (var round = 0; round < Rounds; round++)
        {
            var plate = Plate("VR", round);
            var carId = await AddCarAsync(a, admin, plate);
            var version = await CarVersionAsync(a, carId);

            var results = await RaceAsync(
            [
                () => a.GetRequiredService<ICarService>().UpdateAsync(admin, carId, version, new CarInput("Anka", "Deneme", "2022", "Manuel", "Benzin", "1000", "Gri", plate, "200", null, true), Ct),
                () => b.GetRequiredService<ICarService>().UpdateAsync(admin, carId, version, new CarInput("Anka", "Deneme", "2022", "Manuel", "Benzin", "1000", "Gri", plate, "300", null, false), Ct),
            ]);

            Assert.Single(results, r => r is null);
            var refused = Assert.IsType<BusinessRuleException>(Assert.Single(results, r => r is not null));
            Assert.Equal((CarService.ChangedElsewhere, true), (refused.Message, refused.IsStale));
            Assert.Equal(version + 1, await CarVersionAsync(a, carId));
        }
    }

    /// <summary>
    /// Admin A's change has passed the service's version check and waits for the car's lock while admin B
    /// raises the price. A's conditional UPDATE then finds a newer version and writes nothing.
    /// </summary>
    [Fact]
    public async Task UpdateCar_ChangedWhileWaitingForTheLock_IsRefusedAsStale()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        var admin = await SetUpAdminAsync(a);
        var carId = await AddCarAsync(a, admin, "00 VRS 01");
        var gate = CommandGate.BeforeAppLock("AracKiralama.Car." + carId.ToString(CultureInfo.InvariantCulture));
        await using var aApp = database.BuildServices(configureContext: o => o.AddInterceptors(gate));

        var stale = aApp.GetRequiredService<ICarService>().UpdateAsync(
            admin, carId, 1, new CarInput("Anka", "Deneme", "2022", "Manuel", "Benzin", "1000", "Kırmızı", "00 VRS 01", "100", null, true), Ct);
        await gate.WaitUntilReachedAsync(Ct);
        await a.GetRequiredService<ICarService>().UpdateAsync(
            admin, carId, 1, new CarInput("Anka", "Deneme", "2022", "Manuel", "Benzin", "1000", "Gri", "00 VRS 01", "1200", null, false), Ct);
        gate.Release();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => stale);
        Assert.Equal(CarService.ChangedElsewhere, error.Message);
        Assert.Equal(1200, await database.ScalarAsync("SELECT CAST(GunlukUcret AS int) FROM dbo.Araclar WHERE AracID = @id", Ct, ("@id", carId)));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Araclar WHERE AracID = @id AND (KiralamayaAcik = 1 OR Renk <> N'Gri')", Ct, ("@id", carId)));
        Assert.Equal(2, await CarVersionAsync(a, carId));
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
            admin, carId, await CarVersionAsync(a, carId), new CarInput("Anka", "Deneme", "2022", "Manuel", "Benzin", "1000", "Gri", "00 YRS 02", "200", null, true), Ct);
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

    /// <summary>
    /// Ending a rental takes the car's lock before it checks the acting account, in the same order as every
    /// other write of a car. While it waits for a busy car lock it holds no lock on the account row, so a
    /// change of that account (here a plain UPDATE with a short lock timeout) is not blocked behind it and
    /// no deadlock cycle can close through the row.
    /// </summary>
    [Fact]
    public async Task End_WaitingForTheCarLock_HoldsNoLockOnTheAccountRow()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        var admin = await SetUpAdminAsync(a);
        var customer = await RegisterAsync(a, "iade", 1);
        var carId = await AddCarAsync(a, admin, "00 SRA 01");
        var receipt = await RentAsync(a, customer, carId, startOffset: 1);

        await using var holder = new SqlConnection(database.ConnectionString);
        await holder.OpenAsync(Ct);
        await using var held = (SqlTransaction)await holder.BeginTransactionAsync(Ct);
        await using (var take = new SqlCommand("EXEC sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction';", holder, held))
        {
            take.Parameters.AddWithValue("@resource", "AracKiralama.Car." + carId.ToString(CultureInfo.InvariantCulture));
            await take.ExecuteNonQueryAsync(Ct);
        }

        var ending = Task.Run(() => a.GetRequiredService<IRentalService>().EndAsync(admin, receipt.RentalId, Ct), Ct);
        await database.WaitForBlockedRequestsAsync(1, Ct);

        // Error 1222 (lock request time out) here would mean the waiting end holds the admin's row.
        await database.ExecuteAsync(
            "SET LOCK_TIMEOUT 2000; UPDATE dbo.Kullanicilar SET Telefon = Telefon WHERE KullaniciID = @id;",
            Ct,
            ("@id", admin.UserId));
        Assert.False(ending.IsCompleted);

        await held.CommitAsync(Ct);
        Assert.Equal(RentalEnding.Cancelled, await ending);
        Assert.Equal(0, await ActiveAsync(database, carId));
    }

    /// <summary>
    /// A transaction chosen as a deadlock victim (error 1205) wrote nothing and can be tried again; it is
    /// recognised so the screen answers with the busy message instead of a database failure.
    /// </summary>
    [Fact]
    public async Task DeadlockVictim_IsRecognised()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        var admin = await SetUpAdminAsync(a);
        var customer = await RegisterAsync(a, "kilit", 1);

        await using var first = new SqlConnection(database.ConnectionString);
        await using var second = new SqlConnection(database.ConnectionString);
        await first.OpenAsync(Ct);
        await second.OpenAsync(Ct);
        await using var firstTransaction = (SqlTransaction)await first.BeginTransactionAsync(Ct);
        await using var secondTransaction = (SqlTransaction)await second.BeginTransactionAsync(Ct);
        await TouchAsync(first, firstTransaction, admin.UserId);
        await TouchAsync(second, secondTransaction, customer.UserId);

        // Each waits for the row the other holds: SQL Server ends one of them with error 1205.
        var results = await Task.WhenAll(
            CaptureAsync(() => TouchAsync(first, firstTransaction, customer.UserId)),
            CaptureAsync(() => TouchAsync(second, secondTransaction, admin.UserId)));

        var victim = Assert.Single(results, r => r is not null);
        Assert.True(DatabaseErrors.IsDeadlock(victim));
        Assert.True(DatabaseErrors.IsDeadlock(new InvalidOperationException("wrapped", victim)));
        Assert.False(DatabaseErrors.IsDeadlock(new TimeoutException()));

        static async Task TouchAsync(SqlConnection connection, SqlTransaction transaction, int id)
        {
            await using var command = new SqlCommand("UPDATE dbo.Kullanicilar SET Telefon = Telefon WHERE KullaniciID = @id;", connection, transaction);
            command.Parameters.AddWithValue("@id", id);
            await command.ExecuteNonQueryAsync(Ct);
        }

        static async Task<Exception?> CaptureAsync(Func<Task> operation)
        {
            try
            {
                await Task.Run(operation, Ct);
                return null;
            }
            catch (SqlException ex)
            {
                return ex;
            }
        }
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
