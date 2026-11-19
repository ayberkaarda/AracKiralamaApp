using AracKiralama.Business;
using AracKiralama.Business.Services;
using AracKiralama.Data.Persistence;
using AracKiralama.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using static AracKiralama.Data.Tests.TestServices;

namespace AracKiralama.Data.Tests;

/// <summary>Runs against a real SQL Server instance; see <see cref="SqlServerTestDatabase"/>.</summary>
public sealed class LegacyUpgradeTests
{
    private const string BackupEnvironmentVariable = "ARAC_LEGACY_BACKUP";

    // Schema of the original application's database (read from its backup): system-named primary keys
    // and unique constraint on the plate, named defaults for the role ("Müsteri") and the car status
    // ("Müsait"), plain text passwords in NVARCHAR(50), nullable car columns, a foreign key only from
    // Kiralama to Kullanicilar, an unused Musteriler table, no migration history.
    internal const string LegacySchema = """
        CREATE TABLE dbo.Kullanicilar (
            KullaniciID INT IDENTITY(1,1) PRIMARY KEY,
            KullaniciAdi NVARCHAR(50) NOT NULL,
            Sifre NVARCHAR(50) NOT NULL,
            Rol NVARCHAR(20) NULL CONSTRAINT DF_Kullanicilar_Rol DEFAULT 'Müsteri',
            Eposta NVARCHAR(100) NULL,
            Telefon VARCHAR(15) NULL);
        CREATE TABLE dbo.Araclar (
            AracID INT IDENTITY(1,1) PRIMARY KEY,
            Marka NVARCHAR(50) NULL,
            Model NVARCHAR(50) NULL,
            Yil INT NULL,
            VitesTipi NVARCHAR(20) NULL,
            YakitTipi NVARCHAR(20) NULL,
            Km INT NULL,
            Renk NVARCHAR(30) NULL,
            Plaka NVARCHAR(20) NOT NULL UNIQUE,
            GunlukUcret DECIMAL(10, 2) NULL,
            Durum NVARCHAR(20) NULL CONSTRAINT DF_Araclar_Durum DEFAULT 'Müsait',
            Image VARCHAR(255) NULL);
        CREATE TABLE dbo.Kiralama (
            KiralamaID INT IDENTITY(1,1) PRIMARY KEY,
            AracID INT NULL,
            AlisTarihi DATE NOT NULL,
            IadeTarihi DATE NULL,
            Ucret DECIMAL(10, 2) NULL,
            KullaniciID INT NULL CONSTRAINT FK_Kiralama_KullaniciID REFERENCES dbo.Kullanicilar (KullaniciID));
        CREATE TABLE dbo.Musteriler (
            MusteriID INT IDENTITY(1,1) PRIMARY KEY,
            AdSoyad NVARCHAR(100) NOT NULL,
            Telefon NVARCHAR(20) NULL,
            Email NVARCHAR(100) NULL,
            TCNo NVARCHAR(11) NULL,
            DogumTarihi DATE NULL,
            Cinsiyet NVARCHAR(10) NULL);
        """;

    // Fictional rows. Identity values deliberately do not start at 1 so overwritten or re-seeded rows
    // would show. Every role spelling and car status the original application wrote is present.
    private const string LegacyData = """
        SET IDENTITY_INSERT dbo.Kullanicilar ON;
        INSERT dbo.Kullanicilar (KullaniciID, KullaniciAdi, Sifre, Rol, Eposta, Telefon) VALUES
            (3, N'yonetici', N'admin1', N'Admin', N'yonetici@example.com', '5550000101'),
            (5, N'uye', N'1234', N'Müşteri', N'uye@example.com', '555 000 0102'),
            (6, N'eski', N'abcd', N'Müsteri', NULL, NULL),
            (8, N'rolsuz', N'wxyz', NULL, N'', '');
        SET IDENTITY_INSERT dbo.Kullanicilar OFF;
        SET IDENTITY_INSERT dbo.Araclar ON;
        INSERT dbo.Araclar (AracID, Marka, Model, Yil, VitesTipi, YakitTipi, Km, Renk, Plaka, GunlukUcret, Durum, Image) VALUES
            (2, N'Anka', N'Kent', 2020, N'Otomatik', N'Benzin', 45000, N'Beyaz', N'00 ESK 02', 850.00, N'Müsait', 'https://example.com/a.png'),
            (4, N'Kartal', N'Kasa', 2022, N'Manuel', N'Dizel', 20000, N'Siyah', N'00 ESK 04', 1300.00, N'Kirada', NULL),
            (7, N'Poyraz', N'Mini', 2019, N'Manuel', N'Benzin', 60000, N'Gri', N'00 ESK 07', 700.00, N'Kirada', NULL),
            (9, N'Toros', N'Volt', 2021, N'Otomatik', N'Elektrik', 30000, N'Mavi', N'00 ESK 09', 950.00, N'Uygun', NULL),
            (11, N'Toros', N'Yol', 2018, N'Manuel', N'Dizel', 75000, N'Kırmızı', N'00 ESK 11', 680.00, NULL, NULL);
        SET IDENTITY_INSERT dbo.Araclar OFF;
        SET IDENTITY_INSERT dbo.Kiralama ON;
        INSERT dbo.Kiralama (KiralamaID, AracID, AlisTarihi, IadeTarihi, Ucret, KullaniciID) VALUES
            (3, 4, '2026-03-01', '2026-03-06', 6500.00, 5);
        SET IDENTITY_INSERT dbo.Kiralama OFF;
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Initialize_LegacyDatabase_KeepsTheRowsAndTurnsThemIntoTheNewModel()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync(Ct);
        await database.ExecuteAsync(LegacySchema, Ct);
        await database.ExecuteAsync(LegacyData, Ct);

        await using var provider = database.BuildServices();
        var initializer = provider.GetRequiredService<IDatabaseInitializer>();
        await initializer.InitializeAsync(Ct);
        await initializer.InitializeAsync(Ct); // A second start must be a no-op.

        // Rows and identities are kept; no demonstration data was added; the empty Musteriler is gone.
        Assert.Equal(4, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciID IN (3, 5, 6, 8)", Ct));
        Assert.Equal(5, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Araclar WHERE AracID IN (2, 4, 7, 9, 11)", Ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kiralama", Ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = N'Musteriler'", Ct));

        // Roles: exactly "Admin" stays an admin, both customer spellings and no role become users.
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciID = 3 AND Rol COLLATE Latin1_General_BIN2 = N'Admin'", Ct));
        Assert.Equal(3, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciID IN (5, 6, 8) AND Rol COLLATE Latin1_General_BIN2 = N'Kullanici'", Ct));
        Assert.Equal(4, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE SifreBicimi = 1 AND BasarisizGiris = 0 AND KilitBitis IS NULL", Ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciID = 5 AND Telefon = '5550000102'", Ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciID = 8 AND Eposta IS NULL AND Telefon IS NULL", Ct));

        // "Müsait", "Uygun" and a rented "Kirada" car stay listed; a "Kirada" car without a rental and
        // a car without a status were hidden and stay unlisted.
        Assert.Equal(3, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Araclar WHERE AracID IN (2, 4, 9) AND KiralamayaAcik = 1", Ct));
        Assert.Equal(2, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Araclar WHERE AracID IN (7, 11) AND KiralamayaAcik = 0", Ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Araclar') AND name = N'Durum'", Ct));

        // The rental is active and keeps its days.
        Assert.Equal(1, await database.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.Kiralama WHERE KiralamaID = 3 AND Durum = 0 AND AlisTarihi = '2026-03-01' AND IadeTarihi = '2026-03-06' AND GercekIadeTarihi IS NULL",
            Ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%_InitialCreate'", Ct));

        await AssertSameSchemaAsFreshDatabaseAsync(database);

        // Plain text passwords still work once and are replaced with hashes; roles are kept.
        var auth = provider.GetRequiredService<IAuthService>();
        var admin = await auth.SignInAsync("yonetici", "admin1", Ct);
        var user = await auth.SignInAsync("uye", "1234", Ct);
        Assert.True(admin.IsAdmin);
        Assert.False(user.IsAdmin);
        Assert.False((await auth.SignInAsync("rolsuz", "wxyz", Ct)).IsAdmin);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciID = 3 AND Sifre LIKE N'PBKDF2-SHA256$%' AND SifreBicimi = 0", Ct));

        // The old rental still holds its days: an overlapping booking is refused, an adjacent one allowed.
        var rentals = provider.GetRequiredService<IRentalService>();
        var other = await auth.SignInAsync("eski", "abcd", Ct);
        Assert.Equal(RentalService.Overlaps, (await Assert.ThrowsAsync<BusinessRuleException>(() => RentAsync(provider, other, 4, startOffset: 3, days: 2))).Message);
        var receipt = await RentAsync(provider, other, 4, startOffset: 4, days: 2);
        Assert.True(receipt.RentalId > 3);

        // The customer sees the old rental and returns it; the admin can reset a legacy account's password.
        var mine = Assert.Single(await rentals.GetMyActiveRentalsAsync(user, Ct));
        Assert.Equal(RentalEnding.Returned, await rentals.EndAsync(user, mine.Id, Ct));
        await provider.GetRequiredService<IAccountService>().UpdateAsync(admin, 6, new AccountInput("eski", "Yeni.Sifre12", null, null, UserRole.User), Ct);
        await auth.SignInAsync("eski", "Yeni.Sifre12", Ct);

        // Listed cars only; new rows continue after the existing identities.
        var catalog = await provider.GetRequiredService<ICarService>().GetCatalogAsync(user, CarFilter.None, Ct);
        Assert.Equal([2, 4, 9], catalog.Select(c => c.Id));
        Assert.True(await AddCarAsync(provider, admin, "00 YNI 01") > 11);
    }

    [Fact]
    public async Task Initialize_RowsOfUncertainMeaning_RefuseTheUpgradeWithCountsAndChangeNothing()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync(Ct);
        await database.ExecuteAsync(LegacySchema, Ct);
        await database.ExecuteAsync(LegacyData, Ct);
        await database.ExecuteAsync(
            """
            INSERT dbo.Kullanicilar (KullaniciAdi, Sifre, Rol) VALUES (N'kucuk', N'x', N'admin'), (N'bosluklu', N'y', N'Admin ');
            INSERT dbo.Araclar (Marka, Model, Yil, VitesTipi, YakitTipi, Km, Renk, Plaka, GunlukUcret, Durum) VALUES
                (N'Anka', N'Bakim', 2000, N'Manuel', N'Benzin', 1, N'Gri', N'00 BKM 01', 5, N'Bakımda'),
                (N'Anka', NULL, 2000, N'Manuel', N'Benzin', 1, N'Gri', N'00 BKM 02', 5, N'Müsait');
            INSERT dbo.Kiralama (AracID, AlisTarihi, IadeTarihi, Ucret, KullaniciID) VALUES
                (4, '2026-03-05', '2026-03-08', 40, 6),
                (2, '2026-02-01', NULL, 10, 6),
                (77, '2026-02-01', '2026-02-03', 10, 6);
            INSERT dbo.Musteriler (AdSoyad, TCNo) VALUES (N'Örnek Kişi', N'00000000000');
            """,
            Ct);
        var usersBefore = await database.ScalarAsync("SELECT CHECKSUM_AGG(CHECKSUM(*)) FROM dbo.Kullanicilar", Ct);
        var carsBefore = await database.ScalarAsync("SELECT CHECKSUM_AGG(CHECKSUM(AracID, Marka, Durum)) FROM dbo.Araclar", Ct);

        await using var provider = database.BuildServices();
        var error = await Assert.ThrowsAsync<DatabaseInitializationException>(() => provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct));

        var message = error.InnerException!.Message;
        Assert.Contains("2 users with a missing or too long name, password, e-mail or phone, or an unknown role", message, StringComparison.Ordinal);
        Assert.Contains("1 cars with a missing or too long brand", message, StringComparison.Ordinal);
        Assert.Contains("1 cars whose status is none of", message, StringComparison.Ordinal);
        Assert.Contains("1 rentals without an existing car or account", message, StringComparison.Ordinal);
        Assert.Contains("1 rentals with a missing or negative price, no return day", message, StringComparison.Ordinal);
        Assert.Contains("2 rentals that share days with another rental of the same car", message, StringComparison.Ordinal);
        Assert.Contains("1 customer records in the unused Musteriler table", message, StringComparison.Ordinal);
        Assert.Contains("No changes were made", message, StringComparison.Ordinal);
        Assert.DoesNotContain("kucuk", message, StringComparison.Ordinal);
        Assert.DoesNotContain("00000000000", message, StringComparison.Ordinal);

        Assert.Equal(usersBefore, await database.ScalarAsync("SELECT CHECKSUM_AGG(CHECKSUM(*)) FROM dbo.Kullanicilar", Ct));
        Assert.Equal(carsBefore, await database.ScalarAsync("SELECT CHECKSUM_AGG(CHECKSUM(AracID, Marka, Durum)) FROM dbo.Araclar", Ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM sys.tables WHERE name IN (N'__EFMigrationsHistory', N'KurulumAnahtarlari')", Ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Araclar') AND name = N'KiralamayaAcik'", Ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Musteriler", Ct));
    }

    [Fact]
    public async Task Initialize_TwoInstancesAtOnce_UpgradeOnce()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync(Ct);
        await database.ExecuteAsync(LegacySchema, Ct);
        await database.ExecuteAsync(LegacyData, Ct);
        await using var a = database.BuildServices();
        await using var b = database.BuildServices();

        await Task.WhenAll(
            Task.Run(() => a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct), Ct),
            Task.Run(() => b.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct), Ct));

        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.__EFMigrationsHistory", Ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kiralama", Ct));
        Assert.Equal(2, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Araclar WHERE KiralamayaAcik = 0", Ct));
    }

    [Fact]
    public async Task Initialize_NewDatabase_HasNoAccountAndNoCar()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var provider = database.BuildServices();

        Assert.Equal(DemoSeedOutcome.NotRequested, await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct));

        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar", Ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Araclar", Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => provider.GetRequiredService<IAuthService>().SignInAsync("admin", "admin", Ct));
    }

    [Fact]
    public async Task Initialize_DemoMode_AddsCarsAtomicallyButNoAccount()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices(demo: true);
        await using var b = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);

        var outcomes = await Task.WhenAll(
            Task.Run(() => a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct), Ct),
            Task.Run(() => b.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct), Ct));

        Assert.All(outcomes, o => Assert.Equal(DemoSeedOutcome.SkippedExistingData, o));
        Assert.Equal(5, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Araclar", Ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar", Ct));
    }

    /// <summary>
    /// Restores a backup of the original application's database (path in ARAC_LEGACY_BACKUP, readable by
    /// the SQL Server service) and upgrades it. Only counts, identities and checksums are compared, so no
    /// personal data from the backup ends up in test output.
    /// </summary>
    [Fact]
    public async Task Initialize_RestoredLegacyBackup_KeepsAllRows()
    {
        var backup = Environment.GetEnvironmentVariable(BackupEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(backup))
        {
            Assert.Skip($"{BackupEnvironmentVariable} is not set.");
        }

        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await RestoreAsync(database, backup, Ct);

        const string Users = "SELECT CHECKSUM_AGG(CHECKSUM(KullaniciID, KullaniciAdi, Sifre)) FROM dbo.Kullanicilar";
        const string Cars = "SELECT CHECKSUM_AGG(CHECKSUM(AracID, Marka, Model, Yil, VitesTipi, YakitTipi, Km, Renk, Plaka, GunlukUcret, Image)) FROM dbo.Araclar";
        const string Rentals = "SELECT CHECKSUM_AGG(CHECKSUM(KiralamaID, AracID, KullaniciID, AlisTarihi, IadeTarihi, Ucret)) FROM dbo.Kiralama";
        const string Admins = "SELECT COUNT(*) FROM dbo.Kullanicilar WHERE Rol COLLATE Latin1_General_BIN2 = N'Admin'";
        var usersBefore = await database.ScalarAsync(Users, Ct);
        var carsBefore = await database.ScalarAsync(Cars, Ct);
        var rentalsBefore = await database.ScalarAsync(Rentals, Ct);
        var adminsBefore = await database.ScalarAsync(Admins, Ct);
        var userCount = await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar", Ct);
        var carCount = await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Araclar", Ct);
        var rentalCount = await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kiralama", Ct);

        await using var provider = database.BuildServices();
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);

        Assert.True(rentalCount > 0, "The backup has no rentals.");
        Assert.Equal(userCount, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar", Ct));
        Assert.Equal(carCount, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Araclar", Ct));
        Assert.Equal(usersBefore, await database.ScalarAsync(Users, Ct));
        Assert.Equal(carsBefore, await database.ScalarAsync(Cars, Ct));
        Assert.Equal(rentalsBefore, await database.ScalarAsync(Rentals, Ct));
        Assert.Equal(adminsBefore, await database.ScalarAsync(Admins, Ct));
        Assert.Equal(rentalCount, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kiralama WHERE Durum = 0", Ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE SifreBicimi <> 1", Ct));
        await AssertSameSchemaAsFreshDatabaseAsync(database);
    }

    /// <summary>
    /// Compares every table, column, key, check, default and index of <paramref name="upgraded"/> with
    /// a database created by the migrations, so the upgrade leaves no difference behind.
    /// </summary>
    private static async Task AssertSameSchemaAsFreshDatabaseAsync(SqlServerTestDatabase upgraded)
    {
        await using var fresh = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using (var provider = fresh.BuildServices())
        {
            await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        }

        foreach (var query in SchemaQueries)
        {
            var expected = await fresh.TextAsync(query, Ct);
            Assert.False(string.IsNullOrEmpty(expected));
            Assert.Equal(expected, await upgraded.TextAsync(query, Ct));
        }
    }

    private static readonly string[] SchemaQueries =
    [
        """
        SELECT STRING_AGG(CONVERT(nvarchar(max), CONCAT(t.name, '.', c.name, ' ', ty.name, ' ', c.max_length, ' ', c.precision, ' ', c.scale, ' ', c.is_nullable, ' ', c.is_identity)) COLLATE Latin1_General_BIN2, N'|')
            WITHIN GROUP (ORDER BY t.name, c.name)
        FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE t.name <> N'__EFMigrationsHistory'
        """,
        """
        SELECT STRING_AGG(CONVERT(nvarchar(max), CONCAT(o.name, ' ', o.type, ' ', OBJECT_NAME(o.parent_object_id), ' ', cc.definition, dc.definition)) COLLATE Latin1_General_BIN2, N'|')
            WITHIN GROUP (ORDER BY o.name)
        FROM sys.objects o
        LEFT JOIN sys.check_constraints cc ON cc.object_id = o.object_id
        LEFT JOIN sys.default_constraints dc ON dc.object_id = o.object_id
        WHERE o.type IN ('PK', 'F', 'C', 'D', 'UQ') AND OBJECT_NAME(o.parent_object_id) <> N'__EFMigrationsHistory'
        """,
        """
        SELECT STRING_AGG(CONVERT(nvarchar(max), CONCAT(f.name, ' ', OBJECT_NAME(f.parent_object_id), '.', COL_NAME(fc.parent_object_id, fc.parent_column_id), ' > ', OBJECT_NAME(f.referenced_object_id), '.', COL_NAME(fc.referenced_object_id, fc.referenced_column_id), ' ', f.delete_referential_action)) COLLATE Latin1_General_BIN2, N'|')
            WITHIN GROUP (ORDER BY f.name)
        FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id = f.object_id
        """,
        """
        SELECT STRING_AGG(CONVERT(nvarchar(max), CONCAT(x.TableName, '.', x.IndexName, ' ', x.is_unique, ' ', x.filter_definition, ' ', x.Columns)) COLLATE Latin1_General_BIN2, N'|')
            WITHIN GROUP (ORDER BY x.TableName, x.IndexName)
        FROM (
            SELECT t.name AS TableName, i.name AS IndexName, i.is_unique, i.filter_definition,
                (SELECT STRING_AGG(COL_NAME(ic.object_id, ic.column_id) COLLATE Latin1_General_BIN2, ',') WITHIN GROUP (ORDER BY ic.key_ordinal)
                 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id) AS Columns
            FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id
            WHERE i.name IS NOT NULL AND t.name <> N'__EFMigrationsHistory') x
        """,
    ];

    private static async Task RestoreAsync(SqlServerTestDatabase database, string backup, CancellationToken ct)
    {
        await SqlServerTestDatabase.ExecuteAsync(
            database.Server,
            """
            DECLARE @files TABLE (
                LogicalName nvarchar(128), PhysicalName nvarchar(260), Type char(1), FileGroupName nvarchar(128), Size numeric(20,0),
                MaxSize numeric(20,0), FileId bigint, CreateLSN numeric(25,0), DropLSN numeric(25,0), UniqueId uniqueidentifier,
                ReadOnlyLSN numeric(25,0), ReadWriteLSN numeric(25,0), BackupSizeInBytes bigint, SourceBlockSize int, FileGroupId int,
                LogGroupGUID uniqueidentifier, DifferentialBaseLSN numeric(25,0), DifferentialBaseGUID uniqueidentifier, IsReadOnly bit,
                IsPresent bit, TDEThumbprint varbinary(32), SnapshotUrl nvarchar(360));
            INSERT @files EXEC sp_executesql N'RESTORE FILELISTONLY FROM DISK = @p', N'@p nvarchar(4000)', @p = @backup;
            DECLARE @data nvarchar(128) = (SELECT LogicalName FROM @files WHERE Type = 'D');
            DECLARE @log nvarchar(128) = (SELECT LogicalName FROM @files WHERE Type = 'L');
            DECLARE @path nvarchar(400) = CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(400));
            DECLARE @mdf nvarchar(500) = CONCAT(@path, @name, N'.mdf');
            DECLARE @ldf nvarchar(500) = CONCAT(@path, @name, N'_log.ldf');
            RESTORE DATABASE @name FROM DISK = @backup WITH MOVE @data TO @mdf, MOVE @log TO @ldf, RECOVERY;
            """,
            ct,
            ("@name", database.Name),
            ("@backup", backup));
    }
}
