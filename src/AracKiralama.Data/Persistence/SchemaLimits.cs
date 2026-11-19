namespace AracKiralama.Data.Persistence;

/// <summary>
/// Column sizes and value rules enforced by the database. The business layer applies the same or
/// stricter rules; the legacy upgrade checks existing rows against these values.
/// </summary>
internal static class SchemaLimits
{
    /// <summary>Sizes of the original account columns (NVARCHAR(50), NVARCHAR(100), VARCHAR(15)).</summary>
    public const int UserNameLength = 50;
    public const int EmailLength = 100;
    public const int PhoneLength = 15;

    /// <summary>Sizes of the original car columns.</summary>
    public const int BrandLength = 50;
    public const int ModelLength = 50;
    public const int TransmissionLength = 20;
    public const int FuelTypeLength = 20;
    public const int ColorLength = 30;
    public const int PlateLength = 20;
    public const int ImageUrlLength = 255;

    /// <summary>Room for a PBKDF2 hash; the original column held plain text in 50 characters.</summary>
    public const int PasswordLength = 256;

    public const int RoleLength = 20;

    /// <summary>Money columns: DECIMAL(10, 2), as in the original schema.</summary>
    public const int MoneyPrecision = 10;
    public const int MoneyScale = 2;

    /// <summary>Length of a SHA-256 hash of a setup key.</summary>
    public const int SetupKeyHashLength = 32;

    /// <summary>Stored values of <see cref="Domain.Entities.UserRole"/>.</summary>
    public const string UserRoleValue = "Kullanici";
    public const string AdminRoleValue = "Admin";

    // Plain literals: the same text has to work on SQLite, which has no N'' prefix.
    public const string RoleCheck = "[Rol] IN ('Kullanici', 'Admin')";
    public const string FailedLoginCheck = "[BasarisizGiris] >= 0";

    /// <summary>Values of <see cref="Domain.Entities.PasswordFormat"/>: 0 PBKDF2 hash, 1 legacy plain text.</summary>
    public const string PasswordFormatCheck = "[SifreBicimi] IN (0, 1)";

    public const string DailyPriceCheck = "[GunlukUcret] > 0";

    public const string MileageCheck = "[Km] >= 0";

    /// <summary>Values of <see cref="Domain.Entities.RentalStatus"/>: 0 active, 1 returned, 2 cancelled.</summary>
    public const string RentalStatusCheck = "[Durum] IN (0, 1, 2)";

    /// <summary>A rental has a return day exactly when it is returned.</summary>
    public const string RentalReturnCheck =
        "([Durum] = 1 AND [GercekIadeTarihi] IS NOT NULL) OR ([Durum] <> 1 AND [GercekIadeTarihi] IS NULL)";

    /// <summary>A rental covers at least one day: it ends after it starts.</summary>
    public const string RentalDatesCheck = "[IadeTarihi] > [AlisTarihi]";

    public const string RentalPriceCheck = "[Ucret] >= 0";

    /// <summary>
    /// Only active rentals hold days. The unique index with this filter keeps two active rentals of one
    /// car from starting on the same day even for a writer that skips the car's lock; the full overlap
    /// rule is checked under that lock.
    /// </summary>
    public const string ActiveRentalFilter = "[Durum] = 0";

    /// <summary>Size of a maintenance or damage description.</summary>
    public const int MaintenanceDescriptionLength = 500;

    /// <summary>Values of <see cref="Domain.Entities.MaintenanceKind"/>: 0 maintenance, 1 damage.</summary>
    public const string MaintenanceKindCheck = "[Tur] IN (0, 1)";

    /// <summary>Values of <see cref="Domain.Entities.MaintenanceStatus"/>: 0 active, 1 cancelled.</summary>
    public const string MaintenanceStatusCheck = "[Durum] IN (0, 1)";

    /// <summary>A maintenance record covers at least one day.</summary>
    public const string MaintenanceDatesCheck = "[BitisTarihi] > [BaslangicTarihi]";

    public const string MaintenanceCostCheck = "[Maliyet] >= 0";

    public const string MaintenanceVersionCheck = "[Surum] >= 1";

    public const string CarVersionCheck = "[Surum] >= 1";

    public const string EmailFilter = "[Eposta] IS NOT NULL";
    public const string PhoneFilter = "[Telefon] IS NOT NULL";
}
