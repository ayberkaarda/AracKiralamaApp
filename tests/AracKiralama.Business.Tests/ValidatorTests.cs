using System.Globalization;
using AracKiralama.Business.Security;
using AracKiralama.Business.Services;
using AracKiralama.Business.Validation;
using AracKiralama.Domain.Entities;
using FluentValidation;

namespace AracKiralama.Business.Tests;

/// <summary>
/// The validators on their own: boundaries, the order of the messages and the Turkish texts the
/// screens showed before the rules moved into the validators.
/// </summary>
public sealed class ValidatorTests
{
    private const string Password = "Gizli.Sifre1";
    private const int MaxYear = 2027;
    private static readonly DateOnly Today = new(2026, 3, 2);
    private static readonly RentalPolicy Policy = new() { MinRentalDays = 2, MaxRentalDays = 30, MaxAdvanceDays = 60 };

    [Fact]
    public void Messages_AreTheTextsTheScreensShowed()
    {
        Assert.Equal("Lütfen kullanıcı adı ve şifrenizi girin.", AuthService.InputRequired);
        Assert.Equal("Kullanıcı adı veya şifre hatalı. 5 hatalı denemeden sonra giriş 15 dakika engellenir.", AuthService.CredentialsInvalid);
        Assert.Equal("Lütfen tüm alanları doldurun.", AccountRules.AllFieldsRequired);
        Assert.Equal("Kullanıcı adı 3-32 karakter olmalı; yalnızca harf, rakam ve . _ - içerebilir.", AccountRules.UserNameInvalid);
        Assert.Equal("Bu kullanıcı adı rezerve edilmiştir. Lütfen başka bir kullanıcı adı seçin.", AccountRules.UserNameReserved);
        Assert.Equal("Şifre en az 8, en fazla 64 karakter olmalıdır.", AccountRules.PasswordInvalid);
        Assert.Equal("Lütfen geçerli bir e-posta adresi girin.", AccountRules.EmailInvalid);
        Assert.Equal("Telefon numarası 10 haneden oluşmalıdır.", AccountRules.PhoneInvalid);
        Assert.Equal("Şifreler uyuşmuyor.", AccountRules.PasswordsDoNotMatch);
        Assert.Equal("Yeni şifreler birbirini tutmuyor. Lütfen aynı şifreyi girin.", AuthService.NewPasswordsDoNotMatch);
        Assert.Equal("Yıl 1950 ile 2027 arasında olmalıdır.", CarFieldsValidator.RangeInvalid("Yıl", InputRules.MinYear, MaxYear));
        Assert.Equal("Plaka 2-12 karakter olmalı; yalnızca harf (A-Z), rakam, boşluk ve - içerebilir.", CarFieldsValidator.PlateInvalid);
        Assert.Equal("Gün sayısı 2 ile 30 arasında olmalıdır.", RentalQuoteInputValidator.DaysInvalid(Policy));
        Assert.Equal("Kiralama en fazla 60 gün sonrası için yapılabilir.", RentalQuoteInputValidator.StartTooFar(Policy));
        Assert.Equal("Kiralama geçmiş bir tarihte başlayamaz.", RentalService.StartInPast);
    }

    [Theory]
    [InlineData("", Password)]
    [InlineData("   ", Password)]
    [InlineData("ornek", "")]
    [InlineData("ornek", null)]
    public void SignIn_MissingValue_AsksForBoth(string userName, string? password)
    {
        var error = Assert.Single(TestValidators.SignIn.Validate(SignInInput.Create(userName, password)).Errors);

        Assert.Equal(AuthService.InputRequired, error.ErrorMessage);
        Assert.Equal(SignInInputValidator.Required, error.ErrorCode);
    }

    /// <summary>Sign-in accepts what an existing account can hold, also passwords shorter than today's minimum.</summary>
    [Fact]
    public void SignIn_Bounds_AreThoseOfStoredAccounts()
    {
        var longestName = new string('a', InputRules.StoredUserNameMaxLength);
        var longestPassword = new string('x', PasswordPolicy.MaxLength);

        Assert.True(TestValidators.SignIn.Validate(SignInInput.Create(longestName, "1")).IsValid);
        Assert.True(TestValidators.SignIn.Validate(SignInInput.Create("ornek", longestPassword)).IsValid);
        Assert.True(TestValidators.SignIn.Validate(SignInInput.Create("ornek", "   ")).IsValid);

        var name = Assert.Single(TestValidators.SignIn.Validate(SignInInput.Create(longestName + "a", Password)).Errors);
        var password = Assert.Single(TestValidators.SignIn.Validate(SignInInput.Create("ornek", longestPassword + "x")).Errors);
        Assert.All(new[] { name, password }, e =>
        {
            Assert.Equal(AuthService.CredentialsInvalid, e.ErrorMessage);
            Assert.Equal(SignInInputValidator.Unacceptable, e.ErrorCode);
        });
    }

    [Fact]
    public void SignIn_TrimsTheNameOnly()
    {
        var input = SignInInput.Create("  ornek ", " sifre ");

        Assert.Equal("ornek", input.UserName);
        Assert.Equal(" sifre ", input.Password);
    }

    [Theory]
    [InlineData(null, Password, "a@example.com", "5551234567", "required")]
    [InlineData("yeni", "   ", "a@example.com", "5551234567", "required")]
    [InlineData("yeni", Password, " ", "5551234567", "required")]
    [InlineData("yeni", Password, "a@example.com", "", "required")]
    [InlineData("ab", "kisa", "eposta", "1", "name")]
    [InlineData("Admin", "kisa", "eposta", "1", "reserved")]
    [InlineData("yeni", "kisa", "eposta", "1", "password")]
    [InlineData("yeni", Password, "eposta", "1", "email")]
    [InlineData("yeni", Password, "a@example.com", "555 123 456", "phone")]
    public void SignUp_ReportsTheFirstBrokenRuleInTheOriginalOrder(string? name, string? password, string? email, string? phone, string rule)
    {
        var error = Assert.Single(TestValidators.SignUp.Validate(SignUpInput.Create(name, password, email, phone)).Errors);

        Assert.Equal(AccountMessage(rule), error.ErrorMessage);
    }

    [Theory]
    [InlineData("abc", true)]
    [InlineData("ab", false)]
    [InlineData("a234567890123456789012345678901b", true)]
    [InlineData("a234567890123456789012345678901bc", false)]
    [InlineData("ad.soyad_1-2", true)]
    [InlineData("ad soyad", false)]
    [InlineData("şoför", true)]
    public void SignUp_UserNameLengthAndShape(string name, bool valid)
    {
        Assert.Equal(valid, TestValidators.SignUp.Validate(SignUpInput.Create(name, Password, "a@example.com", "5551234567")).IsValid);
    }

    [Theory]
    [InlineData("555 123 4567")]
    [InlineData("5551234567")]
    public void SignUp_PhoneWithOrWithoutTheFormsSpaces_IsAccepted(string phone)
    {
        Assert.True(TestValidators.SignUp.Validate(SignUpInput.Create("yeni", Password, "a@example.com", phone)).IsValid);
    }

    [Theory]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void PasswordPolicy_EightToSixtyFourCharacters(int length, bool valid)
    {
        var password = new string('s', length);

        Assert.Equal(valid, TestValidators.SignUp.Validate(SignUpInput.Create("yeni", password, "a@example.com", "5551234567")).IsValid);
        Assert.Equal(valid, TestValidators.FirstAdmin.Validate(FirstAdminInput.Create("kurucu", password, password)).IsValid);
        Assert.Equal(valid, TestValidators.PasswordChange.Validate(new PasswordChangeInput(Password, password, password)).IsValid);
        Assert.Equal(valid, TestValidators.NewAccount.Validate(NewAccountInput.Create(new AccountInput("yeni", password, "a@example.com", "5551234567", UserRole.User))).IsValid);
        Assert.Equal(valid, TestValidators.AccountChange.Validate(AccountChangeInput.Create(new AccountInput("yeni", password, null, null, UserRole.User), Stored())).IsValid);
    }

    [Theory]
    [InlineData("a@b.c", true)]
    [InlineData("ad.soyad@ornek.com.tr", true)]
    [InlineData("eposta", false)]
    [InlineData("a@b", false)]
    [InlineData("a b@c.d", false)]
    [InlineData("a@@b.c", false)]
    public void SignUp_EmailFormat(string email, bool valid)
    {
        Assert.Equal(valid, TestValidators.SignUp.Validate(SignUpInput.Create("yeni", Password, email, "5551234567")).IsValid);
    }

    [Fact]
    public void SignUp_EmailOfTheColumnSize_IsTheLongestAccepted()
    {
        var local = new string('a', InputRules.EmailMaxLength - "@b.co".Length);

        Assert.True(TestValidators.SignUp.Validate(SignUpInput.Create("yeni", Password, local + "@b.co", "5551234567")).IsValid);
        Assert.False(TestValidators.SignUp.Validate(SignUpInput.Create("yeni", Password, local + "a@b.co", "5551234567")).IsValid);
    }

    [Theory]
    [InlineData("", Password, Password, "required")]
    [InlineData("kurucu", Password, " ", "required")]
    [InlineData("k", "kisa", "baska", "name")]
    [InlineData("kurucu", Password, "Baska.Sifre1", "match")]
    [InlineData("kurucu", "kisa", "kisa", "password")]
    public void FirstAdmin_ReportsTheFirstBrokenRule(string name, string password, string repeat, string rule)
    {
        var error = Assert.Single(TestValidators.FirstAdmin.Validate(FirstAdminInput.Create(name, password, repeat)).Errors);

        Assert.Equal(AccountMessage(rule), error.ErrorMessage);
    }

    [Fact]
    public void FirstAdmin_MayBeCalledAdmin() =>
        Assert.True(TestValidators.FirstAdmin.Validate(FirstAdminInput.Create("admin", Password, Password)).IsValid);

    [Theory]
    [InlineData(" ", "Yeni.Sifre12", "Yeni.Sifre12", "required")]
    [InlineData(Password, "Yeni.Sifre12", "Yeni.Sifre13", "new-match")]
    [InlineData(Password, "kisa", "kisa", "password")]
    public void PasswordChange_ReportsTheFirstBrokenRule(string current, string next, string repeat, string rule)
    {
        var error = Assert.Single(TestValidators.PasswordChange.Validate(new PasswordChangeInput(current, next, repeat)).Errors);

        Assert.Equal(AccountMessage(rule), error.ErrorMessage);
    }

    [Theory]
    [InlineData("yeni", null, "a@example.com", "5551234567", UserRole.User, "required")]
    [InlineData(" ", Password, "a@example.com", "5551234567", UserRole.User, "required")]
    [InlineData("yeni", Password, "a@example.com", "5551234567", (UserRole)9, "required")]
    [InlineData("ad soyad", Password, "eposta", "1", UserRole.User, "name")]
    [InlineData("yeni", Password, "eposta", "1", UserRole.User, "email")]
    [InlineData("yeni", Password, "a@example.com", "1", UserRole.User, "phone")]
    [InlineData("yeni", "kisa", "a@example.com", "5551234567", UserRole.User, "password")]
    public void NewAccount_ReportsTheFirstBrokenRule(string name, string? password, string email, string phone, UserRole role, string rule)
    {
        var input = NewAccountInput.Create(new AccountInput(name, password, email, phone, role));

        Assert.Equal(AccountMessage(rule), Assert.Single(TestValidators.NewAccount.Validate(input).Errors).ErrorMessage);
    }

    [Fact]
    public void NewAccount_ByAnAdmin_MayUseTheReservedNameAndLeaveContactEmpty()
    {
        var input = NewAccountInput.Create(new AccountInput(" admin ", Password, "  ", "", UserRole.Admin));

        Assert.Equal("admin", input.UserName);
        Assert.Null(input.Email);
        Assert.Null(input.Phone);
        Assert.True(TestValidators.NewAccount.Validate(input).IsValid);
    }

    [Fact]
    public void AccountChange_EmptyContactAndPassword_AreAccepted()
    {
        var input = AccountChangeInput.Create(new AccountInput("ornek", "", " ", "  ", UserRole.User), Stored());

        Assert.Null(input.Email);
        Assert.Null(input.Phone);
        Assert.True(TestValidators.AccountChange.Validate(input).IsValid);
    }

    /// <summary>
    /// Values of the original data that today's rules refuse are kept when the form sends them unchanged
    /// (also with surrounding spaces), and then stored exactly as they were; changed values follow the rules.
    /// </summary>
    [Fact]
    public void AccountChange_StoredValuesOutsideTodaysRules_AreKeptButNotNewOnes()
    {
        var stored = new UserSummary(7, "Ali Veli", "eski adres", "0212 555 12", UserRole.User, 0);

        var unchanged = AccountChangeInput.Create(new AccountInput(" Ali Veli ", null, "eski adres ", " 0212 555 12", UserRole.User), stored);
        Assert.True(TestValidators.AccountChange.Validate(unchanged).IsValid);
        Assert.Equal(("Ali Veli", "eski adres", "0212 555 12"), (unchanged.UserName, unchanged.Email, unchanged.Phone));
        Assert.True(unchanged.KeepsStoredName && unchanged.KeepsStoredEmail && unchanged.KeepsStoredPhone);

        Assert.Equal(AccountRules.UserNameInvalid, Single(AccountChangeInput.Create(new AccountInput("Ali Veli2 ", null, null, null, UserRole.User), stored)));
        Assert.Equal(AccountRules.EmailInvalid, Single(AccountChangeInput.Create(new AccountInput("Ali Veli", null, "yeni adres", null, UserRole.User), stored)));
        Assert.Equal(AccountRules.PhoneInvalid, Single(AccountChangeInput.Create(new AccountInput("Ali Veli", null, null, "0212 555 13", UserRole.User), stored)));

        static string Single(AccountChangeInput input) => Assert.Single(TestValidators.AccountChange.Validate(input).Errors).ErrorMessage;
    }

    [Fact]
    public void AccountChange_NameAndRole_AreRequired()
    {
        Assert.Equal(AccountRules.AllFieldsRequired, Assert.Single(TestValidators.AccountChange.Validate(AccountChangeInput.Create(new AccountInput("  ", null, null, null, UserRole.User), Stored())).Errors).ErrorMessage);
        Assert.Equal(AccountRules.AllFieldsRequired, Assert.Single(TestValidators.AccountChange.Validate(AccountChangeInput.Create(new AccountInput("ornek", null, null, null, (UserRole)9), Stored())).Errors).ErrorMessage);
    }

    [Fact]
    public void Car_EmptyForm_ReportsEveryFieldInTheOrderOfTheForm()
    {
        var fields = CarFields.Create(new CarInput(" ", null, "", "", " ", "", "", "  ", "", null, true), MaxYear, stored: null);

        var errors = TestValidators.Car.Validate(fields).Errors;

        Assert.Equal(
            [
                (nameof(CarInput.Brand), "Marka boş bırakılamaz."),
                (nameof(CarInput.Model), "Model boş bırakılamaz."),
                (nameof(CarInput.Color), "Renk boş bırakılamaz."),
                (nameof(CarInput.Transmission), "Vites tipi boş bırakılamaz."),
                (nameof(CarInput.FuelType), "Yakıt tipi boş bırakılamaz."),
                (nameof(CarInput.Year), "Yıl boş bırakılamaz."),
                (nameof(CarInput.Mileage), "Kilometre boş bırakılamaz."),
                (nameof(CarInput.Plate), "Plaka boş bırakılamaz."),
                (nameof(CarInput.DailyPrice), "Ücret boş bırakılamaz."),
            ],
            errors.Select(e => (e.PropertyName, e.ErrorMessage)));
    }

    [Theory]
    [InlineData(nameof(CarInput.Year), "1949", false)]
    [InlineData(nameof(CarInput.Year), "1950", true)]
    [InlineData(nameof(CarInput.Year), "2027", true)]
    [InlineData(nameof(CarInput.Year), "2028", false)]
    [InlineData(nameof(CarInput.Year), "-2000", false)]
    [InlineData(nameof(CarInput.Year), "20 20", false)]
    [InlineData(nameof(CarInput.Mileage), "0", true)]
    [InlineData(nameof(CarInput.Mileage), "2000000", true)]
    [InlineData(nameof(CarInput.Mileage), "2000001", false)]
    [InlineData(nameof(CarInput.Mileage), "-1", false)]
    [InlineData(nameof(CarInput.Mileage), "1.000", false)]
    [InlineData(nameof(CarInput.Plate), "34 ABC 123", true)]
    [InlineData(nameof(CarInput.Plate), "34  abc   123", true)]
    [InlineData(nameof(CarInput.Plate), "AB", true)]
    [InlineData(nameof(CarInput.Plate), "A", false)]
    [InlineData(nameof(CarInput.Plate), "34-ABC-12345", true)]
    [InlineData(nameof(CarInput.Plate), "34-ABC-123456", false)]
    [InlineData(nameof(CarInput.Plate), "34 ÇÖĞ 12", false)]
    [InlineData(nameof(CarInput.Plate), "34_ABC", false)]
    [InlineData(nameof(CarInput.DailyPrice), "0", false)]
    [InlineData(nameof(CarInput.DailyPrice), "0,01", true)]
    [InlineData(nameof(CarInput.DailyPrice), "850,505", false)]
    [InlineData(nameof(CarInput.DailyPrice), "99999999,99", true)]
    [InlineData(nameof(CarInput.DailyPrice), "100000000", false)]
    [InlineData(nameof(CarInput.DailyPrice), "1.250", false)]
    [InlineData(nameof(CarInput.ImageUrl), "https://ornek.test/arac.png", true)]
    [InlineData(nameof(CarInput.ImageUrl), "ftp://ornek.test/arac.png", false)]
    [InlineData(nameof(CarInput.ImageUrl), "C:\\arac.png", false)]
    [InlineData(nameof(CarInput.ImageUrl), "arac.png", false)]
    [InlineData(nameof(CarInput.Transmission), "Otomatik", true)]
    [InlineData(nameof(CarInput.Transmission), "otomatik", false)]
    [InlineData(nameof(CarInput.Transmission), "Yarı otomatik", false)]
    [InlineData(nameof(CarInput.FuelType), "LPG", true)]
    [InlineData(nameof(CarInput.FuelType), "Hidrojen", false)]
    public void Car_FieldBoundaries(string field, string value, bool valid)
    {
        var input = field switch
        {
            nameof(CarInput.Year) => ValidCar() with { Year = value },
            nameof(CarInput.Mileage) => ValidCar() with { Mileage = value },
            nameof(CarInput.Plate) => ValidCar() with { Plate = value },
            nameof(CarInput.DailyPrice) => ValidCar() with { DailyPrice = value },
            nameof(CarInput.ImageUrl) => ValidCar() with { ImageUrl = value },
            nameof(CarInput.Transmission) => ValidCar() with { Transmission = value },
            _ => ValidCar() with { FuelType = value },
        };

        var result = TestValidators.Car.Validate(CarFields.Create(input, MaxYear, stored: null));

        Assert.Equal(valid, result.IsValid);
        if (!valid)
        {
            Assert.Equal(field, Assert.Single(result.Errors).PropertyName);
        }
    }

    [Fact]
    public void Car_TextLengths_AreTheColumnSizes()
    {
        var atLimit = ValidCar() with
        {
            Brand = new string('a', InputRules.BrandMaxLength),
            Model = new string('b', InputRules.ModelMaxLength),
            Color = new string('c', InputRules.ColorMaxLength),
            ImageUrl = "https://ornek.test/" + new string('e', InputRules.ImageUrlMaxLength - "https://ornek.test/".Length),
        };
        var beyond = atLimit with
        {
            Brand = atLimit.Brand + "a",
            Model = atLimit.Model + "b",
            Color = atLimit.Color + "c",
            ImageUrl = atLimit.ImageUrl + "e",
        };

        Assert.True(TestValidators.Car.Validate(CarFields.Create(atLimit, MaxYear, stored: null)).IsValid);
        var errors = TestValidators.Car.Validate(CarFields.Create(beyond, MaxYear, stored: null)).Errors;
        Assert.Equal(
            ["Marka en fazla 50 karakter olabilir.", "Model en fazla 50 karakter olabilir.", "Renk en fazla 30 karakter olabilir.", CarFieldsValidator.ImageUrlInvalid],
            errors.Select(e => e.ErrorMessage));
    }

    /// <summary>A gearbox or fuel type of the original data outside the lists is kept only when sent unchanged on a change.</summary>
    [Fact]
    public void Car_StoredChoiceOutsideTheLists_IsKeptOnlyUnchanged()
    {
        var stored = new Car { Transmission = "Yarı Otomatik", FuelType = "Benzin+LPG" };
        var unchanged = ValidCar() with { Transmission = " Yarı Otomatik ", FuelType = "Benzin+LPG" };

        Assert.True(TestValidators.Car.Validate(CarFields.Create(unchanged, MaxYear, stored)).IsValid);
        Assert.Equal("Yarı Otomatik", CarFieldsValidator.ChoiceValue(unchanged.Transmission, stored.Transmission));
        Assert.Equal("Manuel", CarFieldsValidator.ChoiceValue(" Manuel ", stored.Transmission));

        var errors = TestValidators.Car.Validate(CarFields.Create(unchanged, MaxYear, stored: null)).Errors;
        Assert.Equal(["Vites tipi listedeki seçeneklerden biri olmalıdır.", "Yakıt tipi listedeki seçeneklerden biri olmalıdır."], errors.Select(e => e.ErrorMessage));
    }

    [Theory]
    [InlineData(1, "100", 0, "days")]
    [InlineData(31, "100", 0, "days")]
    [InlineData(2, "100", -1, "past")]
    [InlineData(2, "100", 61, "far")]
    [InlineData(2, "0", 0, "price")]
    [InlineData(30, "-1", 60, "price")]
    [InlineData(30, "99999999.99", 0, "total")]
    public void Quote_ReportsDaysThenStartThenPriceThenTotal(int days, string price, int startOffset, string rule)
    {
        var input = new RentalQuoteInput(days, decimal.Parse(price, CultureInfo.InvariantCulture), Today.AddDays(startOffset), Today);

        var error = Assert.Single(TestValidators.Quote(Policy).Validate(input).Errors);

        var expected = rule switch
        {
            "days" => RentalQuoteInputValidator.DaysInvalid(Policy),
            "past" => RentalService.StartInPast,
            "far" => RentalQuoteInputValidator.StartTooFar(Policy),
            "price" => RentalService.CarNotFound,
            _ => RentalService.TotalTooHigh,
        };
        Assert.Equal(expected, error.ErrorMessage);
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(30, 0)]
    [InlineData(2, 60)]
    public void Quote_DaysAndStartWithinTheSettings_AreAccepted(int days, int startOffset) =>
        Assert.True(TestValidators.Quote(Policy).Validate(new RentalQuoteInput(days, 850.5m, Today.AddDays(startOffset), Today)).IsValid);

    /// <summary>The total is checked after the long rental discount, so a discount can bring a price within the limit.</summary>
    [Fact]
    public void Quote_TotalIsCheckedAfterTheDiscount()
    {
        var discounted = new RentalPolicy { MinRentalDays = 1, MaxRentalDays = 30, MaxAdvanceDays = 60, LongRentalDays = 10, LongRentalDiscountPercent = 50 };
        var input = new RentalQuoteInput(10, 15_000_000m, Today, Today);

        Assert.True(TestValidators.Quote(discounted).Validate(input).IsValid);
        Assert.False(TestValidators.Quote(Policy).Validate(input with { Days = 10 }).IsValid);
    }

    [Fact]
    public void ValidateOrThrow_CarriesTheFirstMessageOnly()
    {
        IValidator<SignUpInput> validator = TestValidators.SignUp;

        var error = Assert.Throws<BusinessRuleException>(() => validator.ValidateOrThrow(SignUpInput.Create("ab", "kisa", "eposta", "1")));

        Assert.Equal(AccountRules.UserNameInvalid, error.Message);
        Assert.Empty(error.FieldErrors);
    }

    private static UserSummary Stored() => new(7, "ornek", "ornek@example.com", "5551234567", UserRole.User, 0);

    private static CarInput ValidCar() =>
        new("Anka", "Deneme", "2022", "Manuel", "Benzin", "1000", "Gri", "34 ABC 123", "850,50", null, true);

    private static string AccountMessage(string rule) => rule switch
    {
        "required" => AccountRules.AllFieldsRequired,
        "name" => AccountRules.UserNameInvalid,
        "reserved" => AccountRules.UserNameReserved,
        "password" => AccountRules.PasswordInvalid,
        "email" => AccountRules.EmailInvalid,
        "phone" => AccountRules.PhoneInvalid,
        "match" => AccountRules.PasswordsDoNotMatch,
        "new-match" => AuthService.NewPasswordsDoNotMatch,
        _ => throw new ArgumentOutOfRangeException(nameof(rule)),
    };
}
