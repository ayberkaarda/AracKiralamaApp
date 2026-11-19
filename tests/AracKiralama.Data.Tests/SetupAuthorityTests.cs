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
/// The database owner's setup key script and the application login's rights on a real SQL Server
/// instance (see <see cref="SqlServerTestDatabase"/>).
/// </summary>
public sealed class SetupAuthorityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OwnersKey_CreatesTheFirstAdminOfADatabaseWithAccountsOnce()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        await RegisterAsync(a, "musteri", 1);

        var key = await database.IssueSetupKeyAsync(Ct);
        Assert.Matches("^([0-9A-F]{4}-){7}[0-9A-F]{4}$", key);
        // Only the hash of the key is stored.
        Assert.Equal(1, await database.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.KurulumAnahtarlari WHERE AnahtarOzeti = HASHBYTES('SHA2_256', CAST(REPLACE(@key, '-', '') AS varchar(32)))",
            Ct,
            ("@key", key)));

        // Two founders with the same key at the same moment: one admin, the key used once.
        var results = await Task.WhenAll(
            Try(() => a.GetRequiredService<ISetupService>().CreateFirstAdminAsync(key, "kurucu", Password, Password, Ct)),
            Try(() => b.GetRequiredService<ISetupService>().CreateFirstAdminAsync(key.ToLowerInvariant(), "kurucu2", Password, Password, Ct)));

        Assert.Single(results, r => r is null);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE Rol = N'Admin'", Ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.KurulumAnahtarlari WHERE Tuketildi = 1 AND TuketimTarihi IS NOT NULL", Ct));
    }

    [Fact]
    public async Task OwnersKey_ExpiredByTheDatabaseClock_IsRefused()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        var behind = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(database.Clock.GetUtcNow().AddDays(-1));
        await using var provider = database.BuildServices(clock: behind);
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await RegisterAsync(provider, "musteri", 1);
        var key = await database.IssueSetupKeyAsync(Ct);
        await database.ExecuteAsync("UPDATE dbo.KurulumAnahtarlari SET SonKullanma = DATEADD(SECOND, -1, SYSUTCDATETIME())", Ct);

        // The application's own clock still says the key is fresh; the database clock decides.
        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => provider.GetRequiredService<ISetupService>().CreateFirstAdminAsync(key, "kurucu", Password, Password, Ct));

        Assert.Equal(SetupService.SetupKeyInvalid, error.Message);
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE Rol = N'Admin'", Ct));
    }

    [Fact]
    public async Task OwnersScript_WithAnAdmin_IssuesNoKey()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var provider = database.BuildServices(demo: true);
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await SetUpAdminAsync(provider);

        var error = await Assert.ThrowsAsync<SqlException>(() => database.IssueSetupKeyAsync(Ct));

        Assert.Equal(50001, error.Number);
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.KurulumAnahtarlari", Ct));
    }

    [Fact]
    public async Task AppLoginRights_AllowTheApplicationButNoKeyChanges()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var provider = database.BuildServices();
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await database.ExecuteAsync("CREATE USER AracKiralamaApp WITHOUT LOGIN;", Ct);

        await database.ExecuteAsync(await File.ReadAllTextAsync(SqlServerTestDatabase.RepositoryFile("docs", "grant-app-login.sql"), Ct), Ct);

        const string Rights = """
            EXECUTE AS USER = N'AracKiralamaApp';
            SELECT HAS_PERMS_BY_NAME(N'dbo.Kiralama', N'OBJECT', N'INSERT')
                 + HAS_PERMS_BY_NAME(N'dbo.Kiralama', N'OBJECT', N'UPDATE')
                 + HAS_PERMS_BY_NAME(N'dbo.Araclar', N'OBJECT', N'DELETE')
                 + HAS_PERMS_BY_NAME(N'dbo.Kullanicilar', N'OBJECT', N'UPDATE')
                 + HAS_PERMS_BY_NAME(N'dbo.AracBakim', N'OBJECT', N'UPDATE')
                 + HAS_PERMS_BY_NAME(N'dbo.KurulumAnahtarlari', N'OBJECT', N'SELECT')
                 + HAS_PERMS_BY_NAME(N'dbo.KurulumAnahtarlari', N'OBJECT', N'UPDATE', N'Tuketildi', N'COLUMN')
                 + 10 * HAS_PERMS_BY_NAME(N'dbo.Kiralama', N'OBJECT', N'DELETE')
                 + 10 * HAS_PERMS_BY_NAME(N'dbo.AracBakim', N'OBJECT', N'DELETE')
                 + 10 * HAS_PERMS_BY_NAME(N'dbo.KurulumAnahtarlari', N'OBJECT', N'INSERT')
                 + 10 * HAS_PERMS_BY_NAME(N'dbo.KurulumAnahtarlari', N'OBJECT', N'UPDATE', N'SonKullanma', N'COLUMN');
            REVERT;
            """;
        Assert.Equal(7, await database.ScalarAsync(Rights, Ct));
    }

    /// <summary>
    /// The application really runs under the rights of docs/grant-app-login.sql: every command of the
    /// instance runs as that database user (EXECUTE AS). It starts (legacy check, migration history,
    /// migration lock), uses the owner's key once to create the first admin (locking reads and the
    /// column UPDATE of the key), manages cars and accounts (application locks, actor checks), books
    /// and ends a rental, counts a failed sign-in and reads the database clock. What the login must
    /// not do is still refused.
    /// </summary>
    [Fact]
    public async Task AppLogin_StartsSetsUpAndRunsWithOnlyItsGrantedRights()
    {
        const string AppUser = "AracKiralamaApp";
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using (var owner = database.BuildServices())
        {
            await owner.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
            await RegisterAsync(owner, "musteri", 1);
        }

        await database.ExecuteAsync($"CREATE USER [{AppUser}] WITHOUT LOGIN;", Ct);
        var grants = await File.ReadAllTextAsync(SqlServerTestDatabase.RepositoryFile("docs", "grant-app-login.sql"), Ct);
        Assert.Contains($"DECLARE @AppUser sysname = N'{AppUser}';", grants, StringComparison.Ordinal);
        await database.ExecuteAsync(grants, Ct);
        var key = await database.IssueSetupKeyAsync(Ct);

        await using var app = database.BuildServices(configureContext: o => o.AddInterceptors(new ImpersonateUser(AppUser)), pooling: false);
        await using (var db = await app.GetRequiredService<IDbContextFactory<AracDbContext>>().CreateDbContextAsync(Ct))
        {
            Assert.Equal(AppUser, await db.Database.SqlQueryRaw<string>("SELECT USER_NAME() AS [Value]").SingleAsync(Ct));
        }

        Assert.Equal(DemoSeedOutcome.NotRequested, await app.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct));
        var admin = await app.GetRequiredService<ISetupService>().CreateFirstAdminAsync(key, "kurucu", Password, Password, Ct);
        Assert.True(admin.IsAdmin);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.KurulumAnahtarlari WHERE Tuketildi = 1 AND TuketimTarihi IS NOT NULL", Ct));

        var carId = await AddCarAsync(app, admin, "00 YTK 01");
        var accounts = app.GetRequiredService<IAccountService>();
        var customer = Assert.Single(await accounts.GetAllAsync(admin, Ct), u => u.UserName == "musteri");
        await accounts.UpdateAsync(admin, customer.Id, new AccountInput("musteri", "Yeni.Sifre12", customer.Email, customer.Phone, UserRole.User), Ct);
        var session = await app.GetRequiredService<IAuthService>().SignInAsync("musteri", "Yeni.Sifre12", Ct);
        var receipt = await RentAsync(app, session, carId, startOffset: 1, days: 2);
        Assert.Equal(RentalEnding.Cancelled, await app.GetRequiredService<IRentalService>().EndAsync(session, receipt.RentalId, Ct));
        var contract = await app.GetRequiredService<IContractService>().CreateAsync(session, receipt.RentalId, Ct);
        Assert.NotEmpty(contract.Content);
        var maintenance = app.GetRequiredService<IMaintenanceService>();
        var today = app.GetRequiredService<IRentalService>().Today;
        var recordId = await maintenance.AddAsync(admin, carId, new MaintenanceInput(MaintenanceKind.Maintenance, "Bakım", today.AddDays(1), today.AddDays(3), "100"), Ct);
        await maintenance.UpdateAsync(admin, recordId, 1, new MaintenanceInput(MaintenanceKind.Damage, "Hasar", today.AddDays(1), today.AddDays(2), null), Ct);
        Assert.Equal(DayState.Maintenance, (await app.GetRequiredService<IAvailabilityService>().GetCalendarAsync(session, carId, today.AddDays(1), Ct)).Days.Single(d => d.Date == today.AddDays(1)).State);
        await maintenance.CancelAsync(admin, recordId, 2, Ct);
        var report = await app.GetRequiredService<IReportService>().GetRevenueAsync(admin, today, today.AddDays(10), RevenueGrouping.Day, Ct);
        Assert.Equal(0, report.Total.Rentals);
        await Assert.ThrowsAsync<BusinessRuleException>(() => app.GetRequiredService<IAuthService>().SignInAsync("musteri", "Yanlis.Sifre1", Ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT BasarisizGiris FROM dbo.Kullanicilar WHERE KullaniciAdi = N'musteri'", Ct));

        // Still refused under the same impersonation: rental or maintenance deletion, key creation or extension, schema changes.
        // 229 object, 230 column, 1088 ALTER on an object the user may not change.
        string[] forbidden =
        [
            "DELETE dbo.Kiralama",
            "DELETE dbo.AracBakim",
            "INSERT dbo.KurulumAnahtarlari (AnahtarOzeti, Olusturma, SonKullanma, Tuketildi) VALUES (HASHBYTES('SHA2_256', 'X'), SYSUTCDATETIME(), DATEADD(DAY, 1, SYSUTCDATETIME()), 0)",
            "UPDATE dbo.KurulumAnahtarlari SET SonKullanma = DATEADD(YEAR, 1, SonKullanma)",
            "ALTER TABLE dbo.Araclar ADD Deneme int NULL",
        ];
        var unpooled = new SqlConnectionStringBuilder(database.ConnectionString) { Pooling = false }.ConnectionString;
        foreach (var statement in forbidden)
        {
            var denied = await Assert.ThrowsAsync<SqlException>(() => SqlServerTestDatabase.ExecuteAsync(unpooled, $"EXECUTE AS USER = N'{AppUser}'; {statement}", Ct));
            Assert.True(denied.Number is 229 or 230 or 1088, $"Permission denied was expected, got {denied.Number}: {statement}");
        }
    }

    /// <summary>
    /// Two first-run setups of an empty database from two instances. The first is held just before its
    /// insert, with the locking read of the empty account table done; the second has to wait for it
    /// (it does not decide on an empty table too) and, once the first commits, is told that the setup is
    /// done. Then two setups race freely on another empty database: one admin.
    /// </summary>
    [Fact]
    public async Task EmptyDatabase_TwoSetupsAtOnce_CreateOneAdmin()
    {
        await using (var database = await SqlServerTestDatabase.ReserveAsync(Ct))
        {
            await using var plain = database.BuildServices();
            await plain.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
            var gate = CommandGate.BeforeCommandWith("INSERT INTO [Kullanicilar]");
            await using var first = database.BuildServices(configureContext: o => o.AddInterceptors(gate));

            var holding = first.GetRequiredService<ISetupService>().CreateFirstAdminAsync(null, "birinci", Password, Password, Ct);
            await gate.WaitUntilReachedAsync(Ct);
            var racing = Task.Run(() => plain.GetRequiredService<ISetupService>().CreateFirstAdminAsync(null, "ikinci", Password, Password, Ct), Ct);
            await database.WaitForBlockedRequestsAsync(1, Ct);
            Assert.False(racing.IsCompleted);

            gate.Release();
            Assert.True((await holding).IsAdmin);
            Assert.Equal(SetupService.AlreadyDone, (await Assert.ThrowsAsync<BusinessRuleException>(() => racing)).Message);
            Assert.Equal("birinci", await database.TextAsync("SELECT KullaniciAdi FROM dbo.Kullanicilar", Ct));
        }

        await using (var database = await SqlServerTestDatabase.ReserveAsync(Ct))
        {
            await using var a = database.BuildServices();
            await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
            await using var b = database.BuildServices();

            var results = await Task.WhenAll(
                Try(() => a.GetRequiredService<ISetupService>().CreateFirstAdminAsync(null, "birinci", Password, Password, Ct)),
                Try(() => b.GetRequiredService<ISetupService>().CreateFirstAdminAsync(null, "ikinci", Password, Password, Ct)));

            Assert.Single(results, r => r is null);
            Assert.Equal(SetupService.AlreadyDone, Assert.Single(results, r => r is not null)!.Message);
            Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar", Ct));
        }
    }

    private static async Task<Exception?> Try(Func<Task> operation)
    {
        try
        {
            await Task.Run(operation, Ct);
            return null;
        }
        catch (BusinessRuleException ex)
        {
            return ex;
        }
    }
}
