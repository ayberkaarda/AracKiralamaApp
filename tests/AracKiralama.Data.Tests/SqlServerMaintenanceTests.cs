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
/// Races of the maintenance log with bookings and with other admins on a real SQL Server instance (see
/// <see cref="SqlServerTestDatabase"/>), each side with its own services and connection pool, as two
/// application instances.
/// </summary>
public sealed class SqlServerMaintenanceTests
{
    private const int Rounds = 6;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A customer books and an admin logs maintenance for overlapping days at once: exactly one of them gets the days.</summary>
    [Fact]
    public async Task RentAndMaintenance_ForOverlappingDaysAtOnce_ExactlyOneGetsThem()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        var admin = await SetUpAdminAsync(a);
        var customer = await RegisterAsync(a, "musteri", 1);

        for (var round = 0; round < Rounds; round++)
        {
            var carId = await AddCarAsync(a, admin, "00 BK " + round.ToString("00", CultureInfo.InvariantCulture));

            // Rental days 1-3, maintenance days 3-4: one shared day.
            var results = await RaceAsync(
            [
                () => RentAsync(a, customer, carId, startOffset: 1, days: 3),
                () => b.GetRequiredService<IMaintenanceService>().AddAsync(admin, carId, Input(database, 3, 2), Ct),
            ]);

            Assert.Single(results, r => r is null);
            var refusal = Assert.IsType<BusinessRuleException>(Assert.Single(results, r => r is not null)).Message;
            Assert.Contains(refusal, new[] { RentalService.Overlaps, MaintenanceService.OverlapsRental });
            Assert.Equal(1, await database.ScalarAsync(
                """
                SELECT (SELECT COUNT(*) FROM dbo.Kiralama WHERE AracID = @id AND Durum = 0)
                     + (SELECT COUNT(*) FROM dbo.AracBakim WHERE AracID = @id AND Durum = 0)
                """,
                Ct,
                ("@id", carId)));
        }
    }

    /// <summary>
    /// A booking holds the car's lock, just before its INSERT, when an admin logs maintenance on the same
    /// days. The maintenance waits instead of deciding on the old days and is then refused.
    /// </summary>
    [Fact]
    public async Task AddMaintenance_WhileABookingOfTheDaysCommits_WaitsAndIsRefused()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        var admin = await SetUpAdminAsync(a);
        var customer = await RegisterAsync(a, "musteri", 1);
        var carId = await AddCarAsync(a, admin, "00 BK 10");
        var gate = CommandGate.BeforeCommandWith("INSERT INTO [Kiralama]");
        await using var booking = database.BuildServices(configureContext: o => o.AddInterceptors(gate));

        var rent = RentAsync(booking, customer, carId, startOffset: 2, days: 3);
        await gate.WaitUntilReachedAsync(Ct);
        var logging = Task.Run(() => a.GetRequiredService<IMaintenanceService>().AddAsync(admin, carId, Input(database, 3, 1), Ct), Ct);
        await database.WaitForBlockedRequestsAsync(1, Ct);
        Assert.False(logging.IsCompleted);

        gate.Release();
        await rent;
        Assert.Equal(MaintenanceService.OverlapsRental, (await Assert.ThrowsAsync<BusinessRuleException>(() => logging)).Message);
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.AracBakim", Ct));
    }

    /// <summary>
    /// The cancellation of a maintenance record holds the car's lock, just before its UPDATE, when a
    /// customer books the days. The booking waits for the cancellation and then gets them.
    /// </summary>
    [Fact]
    public async Task Rent_WhileMaintenanceIsCancelled_WaitsAndThenGetsTheDays()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        var admin = await SetUpAdminAsync(a);
        var customer = await RegisterAsync(a, "musteri", 1);
        var carId = await AddCarAsync(a, admin, "00 BK 11");
        var recordId = await a.GetRequiredService<IMaintenanceService>().AddAsync(admin, carId, Input(database, 2, 3), Ct);
        var gate = CommandGate.BeforeCommandWith("UPDATE", "[AracBakim]");
        await using var cancelling = database.BuildServices(configureContext: o => o.AddInterceptors(gate));

        var cancel = cancelling.GetRequiredService<IMaintenanceService>().CancelAsync(admin, recordId, 1, Ct);
        await gate.WaitUntilReachedAsync(Ct);
        var rent = Task.Run(() => RentAsync(a, customer, carId, startOffset: 3, days: 1), Ct);
        await database.WaitForBlockedRequestsAsync(1, Ct);
        Assert.False(rent.IsCompleted);

        gate.Release();
        await cancel;
        await rent;
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kiralama WHERE AracID = @id AND Durum = 0", Ct, ("@id", carId)));
    }

    /// <summary>Two admins save the same record loaded at the same version at once: one change is stored, the other is told to reload.</summary>
    [Fact]
    public async Task Update_TwoAdminsSaveTheSameVersionAtOnce_OneWins()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        var admin = await SetUpAdminAsync(a);
        var carId = await AddCarAsync(a, admin, "00 BK 12");

        for (var round = 0; round < Rounds; round++)
        {
            var recordId = await a.GetRequiredService<IMaintenanceService>().AddAsync(admin, carId, Input(database, 10 * round, 2, "Kayıt"), Ct);

            var results = await RaceAsync(
            [
                () => a.GetRequiredService<IMaintenanceService>().UpdateAsync(admin, recordId, 1, Input(database, 10 * round, 3, "Birinci"), Ct),
                () => b.GetRequiredService<IMaintenanceService>().UpdateAsync(admin, recordId, 1, Input(database, 10 * round, 4, "İkinci"), Ct),
            ]);

            Assert.Single(results, r => r is null);
            Assert.Equal(MaintenanceService.ChangedElsewhere, Assert.IsType<BusinessRuleException>(Assert.Single(results, r => r is not null)).Message);
            Assert.Equal(2, await database.ScalarAsync("SELECT Surum FROM dbo.AracBakim WHERE BakimID = @id", Ct, ("@id", recordId)));
        }
    }

    /// <summary>An admin demoted while the maintenance write waits for the car's lock writes nothing.</summary>
    [Fact]
    public async Task AddMaintenance_ByAnAdminDemotedMeanwhile_IsRefused()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        var founder = await SetUpAdminAsync(a);
        var accounts = a.GetRequiredService<IAccountService>();
        var b = await accounts.AddAsync(founder, new AccountInput("badmin", Password, null, null, UserRole.Admin), Ct);
        var bSession = await a.GetRequiredService<IAuthService>().SignInAsync("badmin", Password, Ct);
        var carId = await AddCarAsync(a, founder, "00 BK 13");
        var gate = CommandGate.BeforeAppLock("AracKiralama.Car." + carId.ToString(CultureInfo.InvariantCulture));
        await using var bApp = database.BuildServices(configureContext: o => o.AddInterceptors(gate));

        var logging = bApp.GetRequiredService<IMaintenanceService>().AddAsync(bSession, carId, Input(database, 2, 2), Ct);
        await gate.WaitUntilReachedAsync(Ct);
        await accounts.UpdateAsync(founder, b, await AccountVersionAsync(a, b), new AccountInput("badmin", null, null, null, UserRole.User), Ct);
        gate.Release();

        await Assert.ThrowsAsync<AccessDeniedException>(() => logging);
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.AracBakim", Ct));
    }

    /// <summary>
    /// The car's lock is held by another session for longer than a writer waits: the maintenance write
    /// ends with the lock's refusal (the busy answer), not with a command timeout, and writes nothing.
    /// </summary>
    [Fact]
    public async Task AddMaintenance_WhileTheCarsLockIsHeld_EndsBusyAndWritesNothing()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        var admin = await SetUpAdminAsync(a);
        var carId = await AddCarAsync(a, admin, "00 BK 14");
        await using var holder = new SqlConnection(database.ConnectionString);
        await holder.OpenAsync(Ct);
        await using var held = (SqlTransaction)await holder.BeginTransactionAsync(Ct);
        await using (var take = new SqlCommand("EXEC sp_getapplock @Resource = @r, @LockMode = 'Exclusive', @LockOwner = 'Transaction';", holder, held))
        {
            take.Parameters.AddWithValue("@r", "AracKiralama.Car." + carId.ToString(CultureInfo.InvariantCulture));
            await take.ExecuteNonQueryAsync(Ct);
        }

        // A short command timeout on the application's side: the lock request raises it for itself.
        await using var impatient = database.BuildServices(configureContext: o => o.UseSqlServer(database.ConnectionString, sql => sql.CommandTimeout(2)));
        await Assert.ThrowsAsync<AppLockNotGrantedException>(
            () => impatient.GetRequiredService<IMaintenanceService>().AddAsync(admin, carId, Input(database, 2, 2), Ct));

        await held.RollbackAsync(Ct);
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.AracBakim", Ct));
    }

    private static MaintenanceInput Input(SqlServerTestDatabase database, int startOffset, int days, string description = "Bakım") =>
        new(MaintenanceKind.Maintenance, description, Today(database, startOffset), Today(database, startOffset + days), null);

    /// <summary>Today by the computer clock of the services, which starts at the database's time.</summary>
    private static DateOnly Today(SqlServerTestDatabase database, int offset) => DateOnly.FromDateTime(database.Clock.GetUtcNow().UtcDateTime).AddDays(offset);

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
