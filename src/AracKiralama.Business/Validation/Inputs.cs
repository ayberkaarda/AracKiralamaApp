using AracKiralama.Business.Services;
using AracKiralama.Domain.Entities;

namespace AracKiralama.Business.Validation;

/// <summary>A sign-in attempt. The user name is trimmed; the password is kept as typed.</summary>
internal sealed record SignInInput(string UserName, string? Password)
{
    public static SignInInput Create(string? userName, string? password) =>
        new(userName?.Trim() ?? string.Empty, password);
}

/// <summary>
/// A new account of the sign-up form. The user name and the e-mail address are trimmed; the password
/// and the phone number are kept as typed (the phone may carry the spaces the form inserts).
/// </summary>
internal sealed record SignUpInput(string UserName, string? Password, string Email, string? Phone)
{
    public static SignUpInput Create(string? userName, string? password, string? email, string? phone) =>
        new(userName?.Trim() ?? string.Empty, password, email?.Trim() ?? string.Empty, phone);
}

/// <summary>A change of the signed-in account's own password; every value is kept as typed.</summary>
internal sealed record PasswordChangeInput(string? CurrentPassword, string? NewPassword, string? NewPasswordRepeat);

/// <summary>The first admin of the first-run setup. The user name is trimmed; the passwords are kept as typed.</summary>
internal sealed record FirstAdminInput(string UserName, string? Password, string? PasswordRepeat)
{
    public static FirstAdminInput Create(string? userName, string? password, string? passwordRepeat) =>
        new(userName?.Trim() ?? string.Empty, password, passwordRepeat);
}

/// <summary>
/// An account the admin adds. The user name and the e-mail address are trimmed; e-mail and phone are
/// <see langword="null"/> when left empty (both are optional); the password and the phone number are
/// kept as typed.
/// </summary>
internal sealed record NewAccountInput(string UserName, string? Password, string? Email, string? Phone, UserRole Role)
{
    public static NewAccountInput Create(AccountInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new(
            input.UserName?.Trim() ?? string.Empty,
            input.Password,
            string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim(),
            string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone,
            input.Role);
    }
}

/// <summary>
/// A change the admin makes to a stored account. E-mail and phone are <see langword="null"/> when left
/// empty (accounts of the original application may have neither). A name, e-mail address or phone
/// number the form sends unchanged (apart from surrounding spaces) is the stored value, which may
/// predate today's rules and is then kept as it is (<c>Keeps…</c>); a changed value follows the rules.
/// </summary>
internal sealed record AccountChangeInput(
    string UserName,
    string? Password,
    string? Email,
    string? Phone,
    UserRole Role,
    bool KeepsStoredName,
    bool KeepsStoredEmail,
    bool KeepsStoredPhone)
{
    public static AccountChangeInput Create(AccountInput input, UserSummary stored)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(stored);

        var name = input.UserName?.Trim() ?? string.Empty;
        var storedName = name.Length > 0 ? Unchanged(name, stored.UserName) : null;
        var storedEmail = string.IsNullOrWhiteSpace(input.Email) ? null : Unchanged(input.Email, stored.Email);
        var storedPhone = string.IsNullOrWhiteSpace(input.Phone) ? null : Unchanged(input.Phone, stored.Phone);
        return new(
            storedName ?? name,
            input.Password,
            string.IsNullOrWhiteSpace(input.Email) ? null : storedEmail ?? input.Email.Trim(),
            string.IsNullOrWhiteSpace(input.Phone) ? null : storedPhone ?? input.Phone,
            input.Role,
            storedName is not null,
            storedEmail is not null,
            storedPhone is not null);
    }

    /// <summary>The stored value when <paramref name="value"/> is the same text (apart from surrounding spaces), otherwise <see langword="null"/>.</summary>
    private static string? Unchanged(string value, string? stored) =>
        stored is not null && string.Equals(value.Trim(), stored.Trim(), StringComparison.Ordinal) ? stored : null;
}

/// <summary>
/// The fields of the admin's car form as typed, checked against the latest model year allowed
/// (<paramref name="MaxYear"/>, next year). On a change, <paramref name="StoredTransmission"/> and
/// <paramref name="StoredFuelType"/> are the car's stored values: the original data may hold a gearbox
/// or fuel type that is in neither list, which is kept as long as the form sends it unchanged.
/// </summary>
internal sealed record CarFields(
    string? Brand,
    string? Model,
    string? Year,
    string? Transmission,
    string? FuelType,
    string? Mileage,
    string? Color,
    string? Plate,
    string? DailyPrice,
    string? ImageUrl,
    int MaxYear,
    string? StoredTransmission,
    string? StoredFuelType)
{
    public static CarFields Create(CarInput input, int maxYear, Car? stored)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new(
            input.Brand,
            input.Model,
            input.Year,
            input.Transmission,
            input.FuelType,
            input.Mileage,
            input.Color,
            input.Plate,
            input.DailyPrice,
            input.ImageUrl,
            maxYear,
            stored?.Transmission,
            stored?.FuelType);
    }
}

/// <summary>
/// A rental of <paramref name="Days"/> days from <paramref name="StartDate"/> at the daily price the
/// customer was shown, checked against <paramref name="Today"/>.
/// </summary>
internal sealed record RentalQuoteInput(int Days, decimal DailyPrice, DateOnly StartDate, DateOnly Today);

/// <summary>A maintenance record to check, with today by the database's clock.</summary>
internal sealed record MaintenanceFields(MaintenanceInput Input, DateOnly Today);
