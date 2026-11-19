using System.Globalization;
using AracKiralama.Business.Security;
using AracKiralama.Business.Services;
using FluentValidation;

namespace AracKiralama.Business.Validation;

// The user facing (Turkish) messages are the ones the services showed before; they name fields and
// limits only, never the entered values. The account and rental validators stop at their first failure,
// so the user sees one clear message at a time, in the order the services checked the rules. The car
// validator checks every field and reports one message per invalid field, as the original form marked
// each one.

internal static class ValidationRuleExtensions
{
    /// <summary>Validates <paramref name="input"/> and throws a <see cref="BusinessRuleException"/> carrying the first failure message.</summary>
    public static void ValidateOrThrow<T>(this IValidator<T> validator, T input)
    {
        ArgumentNullException.ThrowIfNull(validator);

        var result = validator.Validate(input);
        if (!result.IsValid)
        {
            throw new BusinessRuleException(result.Errors[0].ErrorMessage);
        }
    }

    /// <summary>A new password of <see cref="PasswordPolicy.MinLength"/> to <see cref="PasswordPolicy.MaxLength"/> characters.</summary>
    public static bool IsAcceptableNewPassword(string? password) =>
        password is { Length: >= PasswordPolicy.MinLength and <= PasswordPolicy.MaxLength };

    public static bool IsValidPhone(string? phone) => InputRules.NormalizePhone(phone) is not null;
}

/// <summary>
/// Sign-in: a name and a password are given (<see cref="Required"/>), and both fit what an existing
/// account can hold (<see cref="Unacceptable"/>). The length rules of <see cref="PasswordPolicy"/> apply
/// to new passwords only; here they would lock out accounts of the original application.
/// </summary>
internal sealed class SignInInputValidator : AbstractValidator<SignInInput>
{
    /// <summary>Error code of a missing name or password.</summary>
    public const string Required = "required";

    /// <summary>Error code of a name or password no account can have; refused like a wrong password.</summary>
    public const string Unacceptable = "unacceptable";

    public SignInInputValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(s => s)
            .Must(s => s.UserName.Length > 0 && !string.IsNullOrEmpty(s.Password))
            .WithName(nameof(SignInInput))
            .WithErrorCode(Required)
            .WithMessage(AuthService.InputRequired);
        RuleFor(s => s.UserName)
            .MaximumLength(InputRules.StoredUserNameMaxLength)
            .WithErrorCode(Unacceptable)
            .WithMessage(_ => AuthService.CredentialsInvalid);
        RuleFor(s => s.Password)
            .Must(p => p!.Length <= PasswordPolicy.MaxLength)
            .WithErrorCode(Unacceptable)
            .WithMessage(_ => AuthService.CredentialsInvalid);
    }
}

/// <summary>
/// The sign-up form: every field given, a user name of the allowed shape that is not the reserved one, a
/// password of the policy's length, an e-mail address and a ten digit phone number.
/// </summary>
internal sealed class SignUpInputValidator : AbstractValidator<SignUpInput>
{
    public SignUpInputValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(s => s)
            .Must(s => s.UserName.Length > 0 && !string.IsNullOrWhiteSpace(s.Password) && s.Email.Length > 0 && !string.IsNullOrWhiteSpace(s.Phone))
            .WithName(nameof(SignUpInput))
            .WithMessage(AccountRules.AllFieldsRequired);
        RuleFor(s => s.UserName)
            .Must(InputRules.IsValidUserName).WithMessage(_ => AccountRules.UserNameInvalid)
            .Must(name => !AccountRules.IsReserved(name)).WithMessage(AccountRules.UserNameReserved);
        RuleFor(s => s.Password)
            .Must(ValidationRuleExtensions.IsAcceptableNewPassword).WithMessage(_ => AccountRules.PasswordInvalid);
        RuleFor(s => s.Email)
            .Must(InputRules.IsValidEmail).WithMessage(AccountRules.EmailInvalid);
        RuleFor(s => s.Phone)
            .Must(ValidationRuleExtensions.IsValidPhone).WithMessage(_ => AccountRules.PhoneInvalid);
    }
}

/// <summary>A password change: every field given, the new password twice the same and of the policy's length.</summary>
internal sealed class PasswordChangeInputValidator : AbstractValidator<PasswordChangeInput>
{
    public PasswordChangeInputValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(p => p)
            .Must(p => !string.IsNullOrWhiteSpace(p.CurrentPassword) && !string.IsNullOrWhiteSpace(p.NewPassword) && !string.IsNullOrWhiteSpace(p.NewPasswordRepeat))
            .WithName(nameof(PasswordChangeInput))
            .WithMessage(AccountRules.AllFieldsRequired);
        RuleFor(p => p.NewPasswordRepeat)
            .Must((p, repeat) => string.Equals(p.NewPassword, repeat, StringComparison.Ordinal)).WithMessage(AuthService.NewPasswordsDoNotMatch);
        RuleFor(p => p.NewPassword)
            .Must(ValidationRuleExtensions.IsAcceptableNewPassword).WithMessage(_ => AccountRules.PasswordInvalid);
    }
}

/// <summary>
/// The first admin: every field given, a user name of the allowed shape (the company's own admin may be
/// called "admin"), the password twice the same and of the policy's length.
/// </summary>
internal sealed class FirstAdminInputValidator : AbstractValidator<FirstAdminInput>
{
    public FirstAdminInputValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(a => a)
            .Must(a => a.UserName.Length > 0 && !string.IsNullOrWhiteSpace(a.Password) && !string.IsNullOrWhiteSpace(a.PasswordRepeat))
            .WithName(nameof(FirstAdminInput))
            .WithMessage(AccountRules.AllFieldsRequired);
        RuleFor(a => a.UserName)
            .Must(InputRules.IsValidUserName).WithMessage(_ => AccountRules.UserNameInvalid);
        RuleFor(a => a.PasswordRepeat)
            .Must((a, repeat) => string.Equals(a.Password, repeat, StringComparison.Ordinal)).WithMessage(AccountRules.PasswordsDoNotMatch);
        RuleFor(a => a.Password)
            .Must(ValidationRuleExtensions.IsAcceptableNewPassword).WithMessage(_ => AccountRules.PasswordInvalid);
    }
}

/// <summary>
/// An account the admin adds: a password, a name and a known role, a user name of the allowed shape (the
/// reserved one included), an e-mail address and a ten digit phone number when given, and a password of
/// the policy's length.
/// </summary>
internal sealed class NewAccountInputValidator : AbstractValidator<NewAccountInput>
{
    public NewAccountInputValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(a => a)
            .Must(a => !string.IsNullOrWhiteSpace(a.Password) && a.UserName.Length > 0 && Enum.IsDefined(a.Role))
            .WithName(nameof(NewAccountInput))
            .WithMessage(AccountRules.AllFieldsRequired);
        RuleFor(a => a.UserName)
            .Must(InputRules.IsValidUserName).WithMessage(_ => AccountRules.UserNameInvalid);
        RuleFor(a => a.Email)
            .Must(email => email is null || InputRules.IsValidEmail(email)).WithMessage(AccountRules.EmailInvalid);
        RuleFor(a => a.Phone)
            .Must(phone => phone is null || ValidationRuleExtensions.IsValidPhone(phone)).WithMessage(_ => AccountRules.PhoneInvalid);
        RuleFor(a => a.Password)
            .Must(ValidationRuleExtensions.IsAcceptableNewPassword).WithMessage(_ => AccountRules.PasswordInvalid);
    }
}

/// <summary>
/// A change of an account: a name and a known role; a name, e-mail address and phone number of the
/// allowed shape unless the form sent the stored value unchanged; e-mail and phone checked only when
/// given; a new password only when given.
/// </summary>
internal sealed class AccountChangeInputValidator : AbstractValidator<AccountChangeInput>
{
    public AccountChangeInputValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(a => a)
            .Must(a => a.UserName.Length > 0 && Enum.IsDefined(a.Role))
            .WithName(nameof(AccountChangeInput))
            .WithMessage(AccountRules.AllFieldsRequired);
        RuleFor(a => a.UserName)
            .Must((a, name) => a.KeepsStoredName || InputRules.IsValidUserName(name)).WithMessage(_ => AccountRules.UserNameInvalid);
        RuleFor(a => a.Email)
            .Must((a, email) => email is null || a.KeepsStoredEmail || InputRules.IsValidEmail(email)).WithMessage(AccountRules.EmailInvalid);
        RuleFor(a => a.Phone)
            .Must((a, phone) => phone is null || a.KeepsStoredPhone || ValidationRuleExtensions.IsValidPhone(phone)).WithMessage(_ => AccountRules.PhoneInvalid);
        RuleFor(a => a.Password)
            .Must(password => string.IsNullOrEmpty(password) || ValidationRuleExtensions.IsAcceptableNewPassword(password))
            .WithMessage(_ => AccountRules.PasswordInvalid);
    }
}

/// <summary>
/// The car form: brand, model and colour given and within their column sizes, a gearbox and a fuel type
/// from the form's lists (or the stored value sent unchanged), a model year from
/// <see cref="InputRules.MinYear"/> to next year, a mileage from 0 to <see cref="InputRules.MaxMileage"/>,
/// a plate of the allowed shape, a positive daily price with at most two decimals and an optional http or
/// https picture address. Every field is checked; each invalid one gets one message under its
/// <see cref="CarInput"/> property name, in the order of the original form.
/// </summary>
internal sealed class CarFieldsValidator : AbstractValidator<CarFields>
{
    internal const string PlateRequired = "Plaka boş bırakılamaz.";
    internal const string PriceRequired = "Ücret boş bırakılamaz.";
    internal const string PriceInvalid = "Ücret sıfırdan büyük, en fazla iki ondalıklı bir tutar olmalıdır (örnek: 850,50).";
    internal const string ImageUrlInvalid = "Resim linki http:// veya https:// ile başlayan, en fazla 255 karakterlik bir adres olmalıdır.";

    internal static readonly string PlateInvalid = string.Create(
        CultureInfo.InvariantCulture,
        $"Plaka {InputRules.PlateMinLength}-{InputRules.PlateMaxLength} karakter olmalı; yalnızca harf (A-Z), rakam, boşluk ve - içerebilir.");

    public CarFieldsValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Continue;
        RuleLevelCascadeMode = CascadeMode.Stop;

        Text(RuleFor(c => c.Brand), "Marka", InputRules.BrandMaxLength);
        Text(RuleFor(c => c.Model), "Model", InputRules.ModelMaxLength);
        Text(RuleFor(c => c.Color), "Renk", InputRules.ColorMaxLength);
        Choice(RuleFor(c => c.Transmission), "Vites tipi", InputRules.Transmissions, c => c.StoredTransmission);
        Choice(RuleFor(c => c.FuelType), "Yakıt tipi", InputRules.FuelTypes, c => c.StoredFuelType);

        RuleFor(c => c.Year)
            .Must(year => !string.IsNullOrEmpty(year?.Trim())).WithMessage("Yıl boş bırakılamaz.")
            .Must((c, year) => ParseNumber(year) is { } value && value >= InputRules.MinYear && value <= c.MaxYear)
            .WithMessage(c => RangeInvalid("Yıl", InputRules.MinYear, c.MaxYear));

        RuleFor(c => c.Mileage)
            .Must(mileage => !string.IsNullOrEmpty(mileage?.Trim())).WithMessage("Kilometre boş bırakılamaz.")
            .Must(mileage => ParseNumber(mileage) is >= 0 and <= InputRules.MaxMileage)
            .WithMessage(RangeInvalid("Kilometre", 0, InputRules.MaxMileage));

        RuleFor(c => c.Plate)
            .Must(plate => !string.IsNullOrWhiteSpace(plate)).WithMessage(PlateRequired)
            .Must(plate => InputRules.NormalizePlate(plate) is not null).WithMessage(_ => PlateInvalid);

        RuleFor(c => c.DailyPrice)
            .Must(price => !string.IsNullOrWhiteSpace(price)).WithMessage(PriceRequired)
            .Must(price => InputRules.TryParseMoney(price, out var amount) && amount > 0m && amount <= InputRules.MaxDailyPrice)
            .WithMessage(PriceInvalid);

        RuleFor(c => c.ImageUrl)
            .Must(url => InputRules.IsValidImageUrl(url!.Trim()))
            .When(c => !string.IsNullOrWhiteSpace(c.ImageUrl))
            .WithMessage(ImageUrlInvalid);
    }

    internal static string RangeInvalid(string label, int min, int max) =>
        string.Create(CultureInfo.InvariantCulture, $"{label} {min} ile {max} arasında olmalıdır.");

    /// <summary>The number typed as plain digits, or <see langword="null"/>.</summary>
    internal static int? ParseNumber(string? text) =>
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;

    /// <summary>
    /// The value to store for a choice: the stored one when the form sent it unchanged (apart from
    /// surrounding spaces), otherwise the typed text, which the validator has matched with the list.
    /// </summary>
    internal static string ChoiceValue(string? typed, string? stored)
    {
        var text = typed?.Trim() ?? string.Empty;
        return stored is not null && string.Equals(text, stored.Trim(), StringComparison.Ordinal) ? stored : text;
    }

    private static void Text(IRuleBuilderInitial<CarFields, string?> rule, string label, int maxLength) =>
        rule
            .Must(value => !string.IsNullOrEmpty(value?.Trim())).WithMessage(label + " boş bırakılamaz.")
            .Must(value => value!.Trim().Length <= maxLength)
            .WithMessage(string.Create(CultureInfo.InvariantCulture, $"{label} en fazla {maxLength} karakter olabilir."));

    private static void Choice(IRuleBuilderInitial<CarFields, string?> rule, string label, IReadOnlyList<string> allowed, Func<CarFields, string?> stored) =>
        rule
            .Must(value => !string.IsNullOrEmpty(value?.Trim())).WithMessage(label + " boş bırakılamaz.")
            .Must((c, value) =>
            {
                var text = value!.Trim();
                var unchanged = stored(c) is { } kept && string.Equals(text, kept.Trim(), StringComparison.Ordinal);
                return unchanged || allowed.Contains(text, StringComparer.Ordinal);
            })
            .WithMessage(label + " listedeki seçeneklerden biri olmalıdır.");
}

/// <summary>
/// A rental: <see cref="RentalPolicy.MinRentalDays"/> to <see cref="RentalPolicy.MaxRentalDays"/> days,
/// a start from today to <see cref="RentalPolicy.MaxAdvanceDays"/> days ahead, a positive daily price (a
/// car without one cannot be rented) and a total the money column can hold.
/// </summary>
internal sealed class RentalQuoteInputValidator : AbstractValidator<RentalQuoteInput>
{
    public RentalQuoteInputValidator(RentalPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        ClassLevelCascadeMode = CascadeMode.Stop;
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(q => q.Days)
            .InclusiveBetween(policy.MinRentalDays, policy.MaxRentalDays).WithMessage(DaysInvalid(policy));
        RuleFor(q => q.StartDate)
            .Must((q, start) => start >= q.Today).WithMessage(RentalService.StartInPast)
            .Must((q, start) => start.DayNumber - q.Today.DayNumber <= policy.MaxAdvanceDays).WithMessage(StartTooFar(policy));
        RuleFor(q => q.DailyPrice)
            .GreaterThan(0m).WithMessage(RentalService.CarNotFound);
        RuleFor(q => q)
            .Must(q => PriceCalculator.Total(q.Days, q.DailyPrice, policy) <= PriceCalculator.MaxAmount)
            .WithName(nameof(RentalQuoteInput))
            .WithMessage(RentalService.TotalTooHigh);
    }

    internal static string DaysInvalid(RentalPolicy policy) =>
        string.Create(CultureInfo.InvariantCulture, $"Gün sayısı {policy.MinRentalDays} ile {policy.MaxRentalDays} arasında olmalıdır.");

    internal static string StartTooFar(RentalPolicy policy) =>
        string.Create(CultureInfo.InvariantCulture, $"Kiralama en fazla {policy.MaxAdvanceDays} gün sonrası için yapılabilir.");
}

/// <summary>
/// A maintenance or damage record: a known kind, a plain text description of at most
/// <see cref="InputRules.MaintenanceDescriptionMaxLength"/> characters, an end after the start within
/// <see cref="RentalPolicy.MaxAllowedDays"/> days of today on either side, and an optional cost.
/// </summary>
internal sealed class MaintenanceFieldsValidator : AbstractValidator<MaintenanceFields>
{
    internal const string KindInvalid = "Lütfen kayıt türünü seçin.";
    internal const string DescriptionRequired = "Lütfen yapılan işi veya hasarı kısaca yazın.";
    internal const string DescriptionInvalid = "Açıklama en fazla 500 karakter olabilir ve kontrol karakteri içeremez.";
    internal const string DatesInvalid = "Bitiş tarihi başlangıç tarihinden sonra olmalıdır.";
    internal const string DatesOutOfRange = "Tarihler bugünden en fazla 10 yıl önce veya sonra olabilir.";
    internal const string CostInvalid = "Maliyet 0 veya daha büyük, en fazla iki ondalık basamaklı bir tutar olmalıdır (örnek: 1250,50).";

    public MaintenanceFieldsValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(f => f.Input.Kind)
            .IsInEnum().WithMessage(KindInvalid);
        RuleFor(f => f.Input.Description)
            .Must(d => !string.IsNullOrWhiteSpace(d)).WithMessage(DescriptionRequired)
            .Must(InputRules.IsValidMaintenanceDescription).WithMessage(DescriptionInvalid);
        RuleFor(f => f.Input)
            .Must(i => i.EndDate > i.StartDate).WithMessage(DatesInvalid)
            .Must((f, i) => i.StartDate.DayNumber >= f.Today.DayNumber - RentalPolicy.MaxAllowedDays
                && i.EndDate.DayNumber <= f.Today.DayNumber + RentalPolicy.MaxAllowedDays)
            .WithMessage(DatesOutOfRange);
        RuleFor(f => f.Input.Cost)
            .Must(c => string.IsNullOrWhiteSpace(c) || (InputRules.TryParseMoney(c, out var cost) && cost <= InputRules.MaxDailyPrice))
            .WithMessage(CostInvalid);
    }
}
