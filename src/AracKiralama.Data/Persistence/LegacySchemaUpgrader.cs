using System.Reflection;
using AracKiralama.Data.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace AracKiralama.Data.Persistence;

/// <summary>
/// Adopts a database created by the original application (Kullanicilar, Araclar and Kiralama without an
/// EF Core migrations history). The existing rows, identity values and plain text passwords are kept;
/// passwords are marked as legacy plain text and hashed on each account's next successful sign-in.
/// </summary>
/// <remarks>
/// <para>
/// The original application booked a car from today for a number of days, marked it "Kirada" and deleted
/// the rental row when the booking was cancelled, so every rental row it left is an active rental whose
/// AlisTarihi is the first and IadeTarihi the day after the last rented day: the half-open range the new
/// model uses. Rentals get their status (active) and the new return column. The car status column becomes
/// whether customers can see the car: "Müsait" and "Uygun" cars stay listed, a car marked "Kirada" stays
/// listed when it has a rental (its days are held by that rental) and is unlisted when it has none (it was
/// hidden by hand), and a car without a status, which the original car list never showed, stays unlisted.
/// The unused Musteriler table is dropped when it is empty.
/// </para>
/// <para>
/// Rows whose meaning is not certain (an unknown role or car status, missing car data, a rental without
/// its car, account or return day, rentals of one car that share days, customer records in Musteriler)
/// stop the upgrade before anything is changed; the problems are reported as counts only. Every step
/// checks the current schema first, and the whole upgrade runs in one transaction under an application
/// lock, so it is safe to run repeatedly and from concurrent processes.
/// </para>
/// </remarks>
internal static class LegacySchemaUpgrader
{
    private static readonly string[] LegacyTables = ["Kullanicilar", "Araclar", "Kiralama"];

    /// <summary>Columns that only the original schema has, plus the ones the upgrade reads.</summary>
    private static readonly (string Table, string Column)[] LegacyColumns =
    [
        ("Kullanicilar", "KullaniciID"), ("Kullanicilar", "KullaniciAdi"), ("Kullanicilar", "Sifre"), ("Kullanicilar", "Rol"),
        ("Kullanicilar", "Eposta"), ("Kullanicilar", "Telefon"),
        ("Araclar", "AracID"), ("Araclar", "Marka"), ("Araclar", "Model"), ("Araclar", "Yil"), ("Araclar", "VitesTipi"),
        ("Araclar", "YakitTipi"), ("Araclar", "Km"), ("Araclar", "Renk"), ("Araclar", "Plaka"), ("Araclar", "GunlukUcret"),
        ("Araclar", "Durum"), ("Araclar", "Image"),
        ("Kiralama", "KiralamaID"), ("Kiralama", "AracID"), ("Kiralama", "KullaniciID"), ("Kiralama", "AlisTarihi"),
        ("Kiralama", "IadeTarihi"), ("Kiralama", "Ucret"),
    ];

    private const string SqlPlus = " + ";

    private const string SqlOr = " OR ";

    /// <summary>Counts which of <see cref="LegacyColumns"/> exist; built once from the constant list above.</summary>
    private static readonly string CountLegacyColumns =
        $"SELECT {string.Join(SqlPlus, LegacyColumns.Select(c => $"CASE WHEN COL_LENGTH(N'dbo.{c.Table}', N'{c.Column}') IS NULL THEN 0 ELSE 1 END"))} AS [Value]";

    /// <summary>Identifier of the first migration; a database without it in its history may be legacy.</summary>
    internal static string BaselineMigrationId { get; } = MigrationId<InitialCreate>();

    /// <summary>The migrations whose schema the upgrade creates itself and records as applied.</summary>
    internal static IReadOnlyList<string> AdoptedMigrationIds { get; } = [BaselineMigrationId];

    private static string MigrationId<TMigration>()
        where TMigration : Migration =>
        typeof(TMigration).GetCustomAttribute<MigrationAttribute>()!.Id;

    /// <summary>
    /// Upgrades a legacy database in place. Returns <see langword="false"/> when there is nothing to
    /// adopt (no database yet, an empty database, or the baseline migration is already applied).
    /// </summary>
    public static async Task<bool> UpgradeIfLegacyAsync(AracDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.GetService<IRelationalDatabaseCreator>().ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (!await IsLegacyAsync(db, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            db,
            static async (context, token) =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(token).ConfigureAwait(false);

                // The return code is checked: a refused lock stops the upgrade before any statement runs.
                await AppLock.AcquireAsync(context, "AracKiralama.LegacySchemaUpgrade", 60_000, token).ConfigureAwait(false);

                // Another process may have finished the upgrade while this one waited for the lock.
                if (!await IsLegacyAsync(context, token).ConfigureAwait(false))
                {
                    await transaction.RollbackAsync(token).ConfigureAwait(false);
                    return false;
                }

                await EnsureLegacyShapeAsync(context, token).ConfigureAwait(false);
                await ValidateDataAsync(context, token).ConfigureAwait(false);

                foreach (var statement in UpgradeStatements)
                {
                    await ExecuteAsync(context, statement, token).ConfigureAwait(false);
                }

                await MarkAdoptedMigrationsAppliedAsync(context, token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return true;
            },
            verifySucceeded: null,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> IsLegacyAsync(AracDbContext db, CancellationToken cancellationToken)
    {
        var applied = await db.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false);
        if (applied.Contains(BaselineMigrationId, StringComparer.Ordinal))
        {
            return false;
        }

        return await CountExistingLegacyTablesAsync(db, cancellationToken).ConfigureAwait(false) > 0;
    }

    private static Task<int> CountExistingLegacyTablesAsync(AracDbContext db, CancellationToken cancellationToken) =>
        ScalarAsync(
            db,
            "SELECT COUNT(*) AS [Value] FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo') AND name IN (N'Kullanicilar', N'Araclar', N'Kiralama')",
            cancellationToken);

    /// <summary>All three tables with the columns of the original application must be there.</summary>
    private static async Task EnsureLegacyShapeAsync(AracDbContext db, CancellationToken cancellationToken)
    {
        var tables = await CountExistingLegacyTablesAsync(db, cancellationToken).ConfigureAwait(false);
        var columns = await ScalarAsync(db, CountLegacyColumns, cancellationToken).ConfigureAwait(false);
        if (tables != LegacyTables.Length || columns != LegacyColumns.Length)
        {
            throw new InvalidOperationException(
                "The database contains only part of the car rental schema (Kullanicilar, Araclar and Kiralama with "
                + "the columns of the original application) and no migration history, so it cannot be upgraded automatically.");
        }
    }

    /// <summary>
    /// An exact text value of <paramref name="column"/>, compared by its bytes and length because SQL
    /// Server ignores trailing spaces and, under the original collation, case.
    /// </summary>
    private static string Exact(string column, string value) =>
        $"({column} COLLATE Latin1_General_BIN2 = N'{value}' AND DATALENGTH(CONVERT(nvarchar(4000), {column})) = {value.Length * 2})";

    /// <summary>
    /// Role values whose meaning in the original application is certain: its sign-in made an account an
    /// admin when the role was exactly "Admin" and a customer otherwise; new accounts got "Müşteri" from
    /// the forms or "Müsteri" from the column default. Only these exact values (and no role at all) are
    /// accepted. Anything else, such as "admin" or "Admin ", stops the upgrade so a person decides the role.
    /// </summary>
    private static readonly string LegacyAdminRole = Exact("Rol", "Admin");

    private static readonly string LegacyUserRole = string.Join(
        SqlOr,
        Exact("Rol", "Müşteri"),
        Exact("Rol", "Müsteri"),
        Exact("Rol", SchemaLimits.UserRoleValue));

    private static readonly string KnownRoleCondition = $"Rol IS NULL OR {LegacyAdminRole} OR {LegacyUserRole}";

    /// <summary>
    /// Car status values the original application wrote: "Müsait" (the column default and a cancelled
    /// rental), "Kirada" (a rental) and "Uygun" (typed by an admin; the car list showed it like "Müsait").
    /// </summary>
    private static readonly string ListedCarStatus = string.Join(SqlOr, Exact("Durum", "Müsait"), Exact("Durum", "Uygun"));

    private static readonly string RentedCarStatus = Exact("Durum", "Kirada");

    private static readonly string KnownCarStatusCondition = $"Durum IS NULL OR {ListedCarStatus} OR {RentedCarStatus}";

    /// <summary>
    /// Refuses the upgrade, before anything is changed, when existing rows cannot be carried over with a
    /// certain meaning. Fixing such rows needs a human decision, so no data is altered or deleted here.
    /// Only counts are reported; row contents (names, e-mails, phones, plates, passwords) never appear in
    /// the message.
    /// </summary>
    private static async Task ValidateDataAsync(AracDbContext db, CancellationToken cancellationToken)
    {
        (string Sql, string Problem)[] checks =
        [
            ($"""
             SELECT COUNT(*) AS [Value] FROM dbo.Kullanicilar
             WHERE KullaniciAdi IS NULL OR LEN(KullaniciAdi) = 0 OR LEN(KullaniciAdi) > {SchemaLimits.UserNameLength}
                OR Sifre IS NULL OR LEN(Sifre) = 0 OR LEN(Sifre) > {SchemaLimits.PasswordLength}
                OR LEN(Eposta) > {SchemaLimits.EmailLength} OR LEN(Telefon) > {SchemaLimits.PhoneLength}
                OR NOT ({KnownRoleCondition})
             """,
                "users with a missing or too long name, password, e-mail or phone, or an unknown role"),
            ("""
             SELECT COUNT(*) AS [Value] FROM (
                 SELECT KullaniciAdi FROM dbo.Kullanicilar GROUP BY KullaniciAdi HAVING COUNT(*) > 1) d
             """,
                "user names shared by more than one account"),
            ("""
             SELECT COUNT(*) AS [Value] FROM (
                 SELECT Eposta FROM dbo.Kullanicilar WHERE LTRIM(RTRIM(Eposta)) <> N''
                 GROUP BY Eposta HAVING COUNT(*) > 1) d
             """,
                "e-mail addresses shared by more than one account"),
            ("""
             SELECT COUNT(*) AS [Value] FROM (
                 SELECT REPLACE(Telefon, ' ', '') AS Phone FROM dbo.Kullanicilar WHERE REPLACE(Telefon, ' ', '') <> ''
                 GROUP BY REPLACE(Telefon, ' ', '') HAVING COUNT(*) > 1) d
             """,
                "phone numbers shared by more than one account"),
            ($"""
             SELECT COUNT(*) AS [Value] FROM dbo.Araclar
             WHERE Marka IS NULL OR LTRIM(RTRIM(Marka)) = N'' OR LEN(Marka) > {SchemaLimits.BrandLength}
                OR Model IS NULL OR LTRIM(RTRIM(Model)) = N'' OR LEN(Model) > {SchemaLimits.ModelLength}
                OR VitesTipi IS NULL OR LTRIM(RTRIM(VitesTipi)) = N'' OR LEN(VitesTipi) > {SchemaLimits.TransmissionLength}
                OR YakitTipi IS NULL OR LTRIM(RTRIM(YakitTipi)) = N'' OR LEN(YakitTipi) > {SchemaLimits.FuelTypeLength}
                OR Renk IS NULL OR LTRIM(RTRIM(Renk)) = N'' OR LEN(Renk) > {SchemaLimits.ColorLength}
                OR Plaka IS NULL OR LTRIM(RTRIM(Plaka)) = N'' OR LEN(Plaka) > {SchemaLimits.PlateLength}
                OR Yil IS NULL OR Km IS NULL OR Km < 0 OR GunlukUcret IS NULL OR GunlukUcret <= 0
                OR LEN(Image) > {SchemaLimits.ImageUrlLength}
             """,
                "cars with a missing or too long brand, model, gearbox, fuel type, colour or plate, no year, no mileage or no positive daily price"),
            ("""
             SELECT COUNT(*) AS [Value] FROM (
                 SELECT Plaka FROM dbo.Araclar GROUP BY Plaka HAVING COUNT(*) > 1) d
             """,
                "plates shared by more than one car"),
            ($"""
             SELECT COUNT(*) AS [Value] FROM dbo.Araclar
             WHERE NOT ({KnownCarStatusCondition})
             """,
                "cars whose status is none of 'Müsait', 'Uygun' and 'Kirada'"),
            ("""
             SELECT COUNT(*) AS [Value] FROM dbo.Kiralama k
             WHERE k.AracID IS NULL OR k.KullaniciID IS NULL
                OR NOT EXISTS (SELECT 1 FROM dbo.Araclar a WHERE a.AracID = k.AracID)
                OR NOT EXISTS (SELECT 1 FROM dbo.Kullanicilar u WHERE u.KullaniciID = k.KullaniciID)
             """,
                "rentals without an existing car or account"),
            ("""
             SELECT COUNT(*) AS [Value] FROM dbo.Kiralama
             WHERE Ucret IS NULL OR Ucret < 0 OR IadeTarihi IS NULL OR IadeTarihi <= AlisTarihi
             """,
                "rentals with a missing or negative price, no return day or a return day not after their start"),
            ("""
             SELECT COUNT(*) AS [Value] FROM dbo.Kiralama k
             WHERE EXISTS (
                 SELECT 1 FROM dbo.Kiralama o
                 WHERE o.AracID = k.AracID AND o.KiralamaID <> k.KiralamaID
                   AND o.AlisTarihi < k.IadeTarihi AND k.AlisTarihi < o.IadeTarihi)
             """,
                "rentals that share days with another rental of the same car"),
            ("""
             SELECT CAST(ISNULL((
                 SELECT SUM(p.rows) FROM sys.partitions p
                 WHERE p.object_id = OBJECT_ID(N'dbo.Musteriler', N'U') AND p.index_id IN (0, 1)), 0) AS int) AS [Value]
             """,
                "customer records in the unused Musteriler table, whose personal data a person has to move or delete"),
        ];

        var problems = new List<string>();
        foreach (var (sql, problem) in checks)
        {
            var count = await ScalarAsync(db, sql, cancellationToken).ConfigureAwait(false);
            if (count > 0)
            {
                problems.Add($"{count} {problem}");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "The existing car rental database cannot be upgraded until these rows are corrected: "
                + string.Join("; ", problems) + ". No changes were made.");
        }
    }

    /// <summary>
    /// Brings the legacy tables to the shape created by <see cref="InitialCreate"/>. Each statement is
    /// guarded so it only runs when the corresponding part of the schema is missing.
    /// </summary>
    private static readonly string[] UpgradeStatements =
    [
        // The defaults (role "Müsteri", status "Müsait"), the system-named unique constraint on the plate,
        // any other unnamed check or unique constraint, and the foreign key of the original schema, are
        // replaced by the model's named constraints and indexes.
        DropConstraints("Kullanicilar", "D"),
        DropConstraints("Araclar", "D"),
        DropConstraints("Kiralama", "D"),
        DropConstraints("Kullanicilar", "C"),
        DropConstraints("Araclar", "C"),
        DropConstraints("Kiralama", "C"),
        DropConstraints("Kullanicilar", "UQ"),
        DropConstraints("Araclar", "UQ"),
        DropConstraints("Kiralama", "UQ"),
        DropConstraints("Kiralama", "F"),

        // Each accepted spelling becomes the role the original application gave it; validation has
        // refused any other value.
        $"""
        UPDATE dbo.Kullanicilar SET Rol = CASE WHEN {LegacyAdminRole} THEN N'{SchemaLimits.AdminRoleValue}' ELSE N'{SchemaLimits.UserRoleValue}' END
        WHERE Rol IS NULL OR {LegacyAdminRole} OR {LegacyUserRole};
        """,

        // An empty e-mail or phone means none (the unique indexes ignore missing values only); a phone
        // keeps its digits, as the new forms store it.
        """
        UPDATE dbo.Kullanicilar SET Eposta = NULL WHERE Eposta IS NOT NULL AND LTRIM(RTRIM(Eposta)) = N'';
        UPDATE dbo.Kullanicilar SET Telefon = NULLIF(REPLACE(Telefon, ' ', ''), '') WHERE Telefon IS NOT NULL AND (CHARINDEX(' ', Telefon) > 0 OR Telefon = '');
        """,

        AlterColumn("Kullanicilar", "KullaniciAdi", "nvarchar", SchemaLimits.UserNameLength * 2, "NVARCHAR(50) NOT NULL"),
        AlterColumn("Kullanicilar", "Sifre", "nvarchar", SchemaLimits.PasswordLength * 2, "NVARCHAR(256) NOT NULL"),
        AlterColumn("Kullanicilar", "Rol", "nvarchar", SchemaLimits.RoleLength * 2, "NVARCHAR(20) NOT NULL"),
        AlterColumn("Kullanicilar", "Eposta", "nvarchar", SchemaLimits.EmailLength * 2, "NVARCHAR(100) NULL", nullable: true),
        AlterColumn("Kullanicilar", "Telefon", "varchar", SchemaLimits.PhoneLength, "VARCHAR(15) NULL", nullable: true),

        // Existing accounts get the values of a fresh account; every password of the original
        // application is plain text (format 1), whatever it looks like.
        AddColumnWithInitialValue("Kullanicilar", "SifreBicimi", "INT NOT NULL", "1"),
        AddColumnWithInitialValue("Kullanicilar", "BasarisizGiris", "INT NOT NULL", "0"),
        AddColumn("Kullanicilar", "KilitBitis", "DATETIME2 NULL"),

        AlterColumn("Araclar", "Marka", "nvarchar", SchemaLimits.BrandLength * 2, "NVARCHAR(50) NOT NULL"),
        AlterColumn("Araclar", "Model", "nvarchar", SchemaLimits.ModelLength * 2, "NVARCHAR(50) NOT NULL"),
        AlterColumn("Araclar", "Yil", "int", 4, "INT NOT NULL"),
        AlterColumn("Araclar", "VitesTipi", "nvarchar", SchemaLimits.TransmissionLength * 2, "NVARCHAR(20) NOT NULL"),
        AlterColumn("Araclar", "YakitTipi", "nvarchar", SchemaLimits.FuelTypeLength * 2, "NVARCHAR(20) NOT NULL"),
        AlterColumn("Araclar", "Km", "int", 4, "INT NOT NULL"),
        AlterColumn("Araclar", "Renk", "nvarchar", SchemaLimits.ColorLength * 2, "NVARCHAR(30) NOT NULL"),
        AlterColumn("Araclar", "Plaka", "nvarchar", SchemaLimits.PlateLength * 2, "NVARCHAR(20) NOT NULL"),
        AlterColumn("Araclar", "GunlukUcret", "decimal", 9, "DECIMAL(10, 2) NOT NULL"),
        AlterColumn("Araclar", "Image", "varchar", SchemaLimits.ImageUrlLength, "VARCHAR(255) NULL", nullable: true),
        AddColumnWithInitialValue("Araclar", "KiralamayaAcik", "BIT NOT NULL", "1"),

        // The status column only exists until this step has run, so the step cannot be repeated. A car
        // stays listed when it was shown as available or its days are held by a rental.
        $"""
        IF COL_LENGTH(N'dbo.Araclar', N'Durum') IS NOT NULL
        BEGIN
            EXEC(N'UPDATE a SET
                    KiralamayaAcik = CASE WHEN {Quoted(ListedCarStatus)}
                        OR ({Quoted(RentedCarStatus)} AND EXISTS (SELECT 1 FROM dbo.Kiralama k WHERE k.AracID = a.AracID)) THEN 1 ELSE 0 END
                FROM dbo.Araclar a');
            EXEC(N'ALTER TABLE dbo.Araclar DROP COLUMN Durum');
        END
        """,

        // Every rental row of the original application is an active rental.
        AlterColumn("Kiralama", "AracID", "int", 4, "INT NOT NULL"),
        AlterColumn("Kiralama", "KullaniciID", "int", 4, "INT NOT NULL"),
        AlterColumn("Kiralama", "AlisTarihi", "date", 3, "DATE NOT NULL"),
        AlterColumn("Kiralama", "IadeTarihi", "date", 3, "DATE NOT NULL"),
        AlterColumn("Kiralama", "Ucret", "decimal", 9, "DECIMAL(10, 2) NOT NULL"),
        AddColumn("Kiralama", "GercekIadeTarihi", "DATE NULL"),
        AddColumnWithInitialValue("Kiralama", "Durum", "INT NOT NULL", "0"),

        // The original application never read or wrote Musteriler; validation has refused a table with rows.
        """
        IF OBJECT_ID(N'dbo.Musteriler', N'U') IS NOT NULL
            EXEC(N'IF NOT EXISTS (SELECT 1 FROM dbo.Musteriler) DROP TABLE dbo.Musteriler');
        """,

        RenamePrimaryKey("Kullanicilar", "PK_Kullanicilar"),
        RenamePrimaryKey("Araclar", "PK_Araclar"),
        RenamePrimaryKey("Kiralama", "PK_Kiralama"),

        """
        IF OBJECT_ID(N'dbo.KurulumAnahtarlari', N'U') IS NULL
            EXEC(N'CREATE TABLE dbo.KurulumAnahtarlari (
                AnahtarID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_KurulumAnahtarlari PRIMARY KEY,
                AnahtarOzeti BINARY(32) NOT NULL,
                Olusturma DATETIME2 NOT NULL,
                SonKullanma DATETIME2 NOT NULL,
                Tuketildi BIT NOT NULL,
                TuketimTarihi DATETIME2 NULL)');
        """,

        AddForeignKey("Kiralama", "FK_Kiralama_Araclar", "AracID", "Araclar", "AracID"),
        AddForeignKey("Kiralama", "FK_Kiralama_Kullanicilar", "KullaniciID", "Kullanicilar", "KullaniciID"),

        AddCheck("Kullanicilar", "CK_Kullanicilar_Rol", SchemaLimits.RoleCheck),
        AddCheck("Kullanicilar", "CK_Kullanicilar_BasarisizGiris", SchemaLimits.FailedLoginCheck),
        AddCheck("Kullanicilar", "CK_Kullanicilar_SifreBicimi", SchemaLimits.PasswordFormatCheck),
        AddCheck("Araclar", "CK_Araclar_GunlukUcret", SchemaLimits.DailyPriceCheck),
        AddCheck("Araclar", "CK_Araclar_Km", SchemaLimits.MileageCheck),
        AddCheck("Kiralama", "CK_Kiralama_Durum", SchemaLimits.RentalStatusCheck),
        AddCheck("Kiralama", "CK_Kiralama_Iade", SchemaLimits.RentalReturnCheck),
        AddCheck("Kiralama", "CK_Kiralama_Tarih", SchemaLimits.RentalDatesCheck),
        AddCheck("Kiralama", "CK_Kiralama_Ucret", SchemaLimits.RentalPriceCheck),

        CreateIndex("Kullanicilar", "IX_Kullanicilar_KullaniciAdi", "CREATE UNIQUE INDEX IX_Kullanicilar_KullaniciAdi ON dbo.Kullanicilar (KullaniciAdi)"),
        CreateIndex("Kullanicilar", "IX_Kullanicilar_Eposta", $"CREATE UNIQUE INDEX IX_Kullanicilar_Eposta ON dbo.Kullanicilar (Eposta) WHERE {SchemaLimits.EmailFilter}"),
        CreateIndex("Kullanicilar", "IX_Kullanicilar_Telefon", $"CREATE UNIQUE INDEX IX_Kullanicilar_Telefon ON dbo.Kullanicilar (Telefon) WHERE {SchemaLimits.PhoneFilter}"),
        CreateIndex("Araclar", "IX_Araclar_Plaka", "CREATE UNIQUE INDEX IX_Araclar_Plaka ON dbo.Araclar (Plaka)"),
        CreateIndex("Araclar", "IX_Araclar_Marka", "CREATE INDEX IX_Araclar_Marka ON dbo.Araclar (Marka)"),
        CreateIndex("Kiralama", "IX_Kiralama_AracID", "CREATE INDEX IX_Kiralama_AracID ON dbo.Kiralama (AracID)"),
        CreateIndex("Kiralama", "IX_Kiralama_KullaniciID", "CREATE INDEX IX_Kiralama_KullaniciID ON dbo.Kiralama (KullaniciID)"),
        CreateIndex(
            "Kiralama",
            "IX_Kiralama_AracID_AlisTarihi",
            $"CREATE UNIQUE INDEX IX_Kiralama_AracID_AlisTarihi ON dbo.Kiralama (AracID, AlisTarihi) WHERE {SchemaLimits.ActiveRentalFilter}"),
    ];

    /// <summary>Doubles the quotes of a fixed condition so it can be embedded in an EXEC(N'...') string.</summary>
    private static string Quoted(string condition) => condition.Replace("'", "''", StringComparison.Ordinal);

    private static string AlterColumn(string table, string column, string typeName, int maxLength, string definition, bool nullable = false) =>
        $"""
        IF EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID(N'dbo.{table}') AND c.name = N'{column}'
              AND (c.is_nullable <> {(nullable ? 1 : 0)} OR t.name <> N'{typeName}' OR c.max_length <> {maxLength}))
            EXEC(N'ALTER TABLE dbo.{table} ALTER COLUMN {column} {definition}');
        """;

    private static string AddColumn(string table, string column, string definition) =>
        $"""
        IF COL_LENGTH(N'dbo.{table}', N'{column}') IS NULL
            EXEC(N'ALTER TABLE dbo.{table} ADD {column} {definition}');
        """;

    /// <summary>
    /// Adds a NOT NULL column filled with <paramref name="value"/> for the existing rows. The default
    /// constraint only exists for the duration of the statement, as the migration creates none.
    /// </summary>
    private static string AddColumnWithInitialValue(string table, string column, string definition, string value) =>
        $"""
        IF COL_LENGTH(N'dbo.{table}', N'{column}') IS NULL
        BEGIN
            EXEC(N'ALTER TABLE dbo.{table} ADD {column} {definition} CONSTRAINT DF_Upgrade_{table}_{column} DEFAULT {value} WITH VALUES');
            EXEC(N'ALTER TABLE dbo.{table} DROP CONSTRAINT DF_Upgrade_{table}_{column}');
        END
        """;

    private static string RenamePrimaryKey(string table, string name) =>
        $"""
        DECLARE @current sysname = (
            SELECT name FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.{table}') AND type = 'PK');
        IF @current IS NOT NULL AND @current <> N'{name}'
        BEGIN
            DECLARE @qualified nvarchar(300) = CONCAT(N'dbo.', QUOTENAME(@current));
            EXEC sp_rename @objname = @qualified, @newname = N'{name}', @objtype = N'OBJECT';
        END
        """;

    /// <summary>
    /// Drops the constraints of one kind (D default, C check, UQ unique, F foreign key) of
    /// <paramref name="table"/> that do not carry a name of the model (CK_ followed by the table name, or
    /// one of the model's foreign keys).
    /// </summary>
    private static string DropConstraints(string table, string type) =>
        $"""
        DECLARE @drop nvarchar(max) = N'';
        SELECT @drop = CONCAT(@drop, N'ALTER TABLE dbo.{table} DROP CONSTRAINT ', QUOTENAME(o.name), N';')
        FROM sys.objects o
        WHERE o.parent_object_id = OBJECT_ID(N'dbo.{table}') AND o.type = N'{type}'
          AND o.name NOT LIKE N'CK[_]{table}[_]%'
          AND o.name NOT IN (N'FK_Kiralama_Araclar', N'FK_Kiralama_Kullanicilar');
        IF @drop <> N'' EXEC(@drop);
        """;

    /// <summary>Adds the named, non-cascading foreign key when it is missing.</summary>
    private static string AddForeignKey(string table, string name, string column, string principal, string principalColumn) =>
        $"""
        IF OBJECT_ID(N'dbo.{name}', N'F') IS NULL
            EXEC(N'ALTER TABLE dbo.{table} WITH CHECK ADD CONSTRAINT {name} FOREIGN KEY ({column}) REFERENCES dbo.{principal} ({principalColumn})');
        """;

    private static string AddCheck(string table, string name, string condition) =>
        $"""
        IF OBJECT_ID(N'dbo.{name}', N'C') IS NULL
            EXEC(N'ALTER TABLE dbo.{table} WITH CHECK ADD CONSTRAINT {name} CHECK ({Quoted(condition)})');
        """;

    private static string CreateIndex(string table, string name, string createStatement) =>
        $"""
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.{table}') AND name = N'{name}')
            EXEC(N'{Quoted(createStatement)}');
        """;

    private static async Task MarkAdoptedMigrationsAppliedAsync(AracDbContext db, CancellationToken cancellationToken)
    {
        var history = db.GetService<IHistoryRepository>();
        await ExecuteAsync(db, history.GetCreateIfNotExistsScript(), cancellationToken).ConfigureAwait(false);
        foreach (var migrationId in AdoptedMigrationIds)
        {
            await ExecuteAsync(
                db,
                history.GetInsertScript(new HistoryRow(migrationId, ProductInfo.GetVersion())),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task<int> ScalarAsync(AracDbContext db, string sql, CancellationToken cancellationToken) =>
#pragma warning disable EF1002 // The statements are built from constants in this class, never from user input.
        db.Database.SqlQueryRaw<int>(sql).SingleAsync(cancellationToken);
#pragma warning restore EF1002

    private static Task<int> ExecuteAsync(AracDbContext db, string sql, CancellationToken cancellationToken) =>
#pragma warning disable EF1002 // The statements are built from constants in this class, never from user input.
        db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
#pragma warning restore EF1002
}
