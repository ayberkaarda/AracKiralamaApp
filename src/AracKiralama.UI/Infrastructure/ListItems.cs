using System.ComponentModel;
using AracKiralama.Domain.Entities;

namespace AracKiralama.UI.Infrastructure;

/// <summary>A filter choice of a combo box; "Tümü" has no value.</summary>
internal sealed record FilterChoice(string Text, string? Value)
{
    public const string All = "Tümü";

    public static FilterChoice Any { get; } = new(All, null);

    public override string ToString() => Text;
}

/// <summary>
/// The brand, gearbox and fuel combo boxes of the car lists. Filling them is not a user choice: while the
/// options are replaced no change is reported, and the previous choice is kept when it still exists.
/// </summary>
internal sealed class CarFilterBoxes
{
    private readonly ComboBox _brand;
    private readonly ComboBox _transmission;
    private readonly ComboBox _fuelType;
    private bool _filling;

    public CarFilterBoxes(ComboBox brand, ComboBox transmission, ComboBox fuelType)
    {
        _brand = brand;
        _transmission = transmission;
        _fuelType = fuelType;
        foreach (var box in new[] { brand, transmission, fuelType })
        {
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.Items.Add(FilterChoice.Any);
            box.SelectedIndex = 0;
            box.SelectedIndexChanged += (_, _) =>
            {
                if (!_filling)
                {
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            };
        }
    }

    /// <summary>Raised when the user picks another value.</summary>
    public event EventHandler? Changed;

    public CarFilter Current => new(Selected(_brand), Selected(_transmission), Selected(_fuelType));

    public void Fill(CarFilterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _filling = true;
        try
        {
            Fill(_brand, options.Brands);
            Fill(_transmission, options.Transmissions);
            Fill(_fuelType, options.FuelTypes);
        }
        finally
        {
            _filling = false;
        }
    }

    private static void Fill(ComboBox box, IEnumerable<string> values)
    {
        var previous = Selected(box);
        box.ReplaceItems(new[] { FilterChoice.Any }.Concat(values.Select(v => new FilterChoice(v, v))));
        var index = 0;
        for (var i = 1; i < box.Items.Count; i++)
        {
            if (box.Items[i] is FilterChoice choice && string.Equals(choice.Value, previous, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        box.SelectedIndex = index;
    }

    private static string? Selected(ComboBox box) => (box.SelectedItem as FilterChoice)?.Value;
}

/// <summary>A car row of the admin's car grid; the column headers are the original column names.</summary>
internal sealed class CarRow(Car car)
{
    [Browsable(false)]
    public Car Car { get; } = car;

    [DisplayName("AracID")]
    public int Id => Car.Id;

    [DisplayName("Marka")]
    public string Brand => Car.Brand;

    [DisplayName("Model")]
    public string Model => Car.Model;

    [DisplayName("Yil")]
    public int Year => Car.Year;

    [DisplayName("VitesTipi")]
    public string Transmission => Car.Transmission;

    [DisplayName("YakitTipi")]
    public string FuelType => Car.FuelType;

    [DisplayName("Km")]
    public int Mileage => Car.Mileage;

    [DisplayName("Renk")]
    public string Color => Car.Color;

    [DisplayName("Plaka")]
    public string Plate => Car.Plate;

    [DisplayName("GunlukUcret")]
    public string DailyPrice => DisplayFormat.MoneyInput(Car.DailyPrice);

    [DisplayName("Durum")]
    public string Status => Describe(Car);

    [DisplayName("Image")]
    public string ImageUrl => Car.ImageUrl ?? string.Empty;

    /// <summary>Whether customers can book the car; which days are taken is shown by the rentals.</summary>
    public static string Describe(Car car)
    {
        ArgumentNullException.ThrowIfNull(car);
        return car.IsListed ? "Kiralamaya açık" : "Kiralamaya kapalı";
    }
}

/// <summary>
/// An account row of the admin's account grid. The password is never shown, and the e-mail and phone are
/// masked in the list; the full values appear only in the edit fields of the account the admin picks.
/// </summary>
internal sealed class UserRow(UserSummary user)
{
    [Browsable(false)]
    public UserSummary User { get; } = user;

    [DisplayName("KullaniciID")]
    public int Id => User.Id;

    [DisplayName("KullaniciAdi")]
    public string UserName => User.UserName;

    [DisplayName("Eposta")]
    public string Email => PersonalData.MaskEmail(User.Email);

    [DisplayName("Telefon")]
    public string Phone => PersonalData.MaskPhone(User.Phone);

    [DisplayName("Rol")]
    public string Role => RoleChoice.Describe(User.Role);
}

/// <summary>Masks contact data in lists, so a glance at the screen does not reveal it.</summary>
internal static class PersonalData
{
    /// <summary>"ornek@example.com" becomes "o***@example.com"; no address stays empty.</summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrEmpty(email))
        {
            return string.Empty;
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 ? "***" : email[0] + "***" + email[at..];
    }

    /// <summary>"5551234567" becomes "555 *** ** 67"; no number stays empty.</summary>
    public static string MaskPhone(string? phone)
    {
        if (string.IsNullOrEmpty(phone))
        {
            return string.Empty;
        }

        return phone.Length < 5 ? "***" : phone[..3] + " *** ** " + phone[^2..];
    }
}

/// <summary>A role of the account form, shown with the original names ("Admin", "Müşteri").</summary>
internal sealed record RoleChoice(UserRole Role)
{
    public static IReadOnlyList<RoleChoice> All { get; } = [new(UserRole.Admin), new(UserRole.User)];

    public static string Describe(UserRole role) => role == UserRole.Admin ? "Admin" : "Müşteri";

    public override string ToString() => Describe(Role);
}
