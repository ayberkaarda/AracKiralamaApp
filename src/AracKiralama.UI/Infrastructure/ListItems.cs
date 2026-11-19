using AracKiralama.Domain.Entities;
using AracKiralama.UI.Theming;

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

/// <summary>
/// A car in the admin's list. The text (<see cref="ToString"/>) is what screen readers and copying use;
/// <see cref="Row"/> is how the themed list draws it: the name, the plate and details below it, and whether the
/// car is open for rental as a pill that a long name never hides.
/// </summary>
internal sealed record CarItem(Car Car)
{
    public override string ToString() => $"{Car.Brand} {Car.Model} ({Car.Plate}) - {Describe(Car)}";

    public ListRow Row() => new(
        Car.Brand + " " + Car.Model,
        string.Join(" · ", new[] { Car.Plate, CarCards.Describe(Car), DisplayFormat.Money(Car.DailyPrice) + " / gün" }.Where(p => !string.IsNullOrWhiteSpace(p))),
        RowTone.Neutral,
        Describe(Car),
        Car.IsListed ? RowTone.Neutral : RowTone.Warning);

    /// <summary>Whether customers can book the car; which days are taken is shown by the rentals.</summary>
    public static string Describe(Car car)
    {
        ArgumentNullException.ThrowIfNull(car);
        return car.IsListed ? "Kiralamaya açık" : "Kiralamaya kapalı";
    }
}

/// <summary>
/// An account in the admin's list. The password is never shown, and the e-mail and phone are masked in the
/// list; the full values appear only in the edit fields of the account the admin picks.
/// </summary>
internal sealed record AccountItem(UserSummary User)
{
    public override string ToString() => $"{User.UserName} ({RoleChoice.Describe(User.Role)})";

    public ListRow Row()
    {
        var contact = string.Join(" · ", new[] { PersonalData.MaskEmail(User.Email), PersonalData.MaskPhone(User.Phone) }.Where(p => p.Length > 0));
        return new ListRow(
            User.UserName,
            contact.Length > 0 ? contact : "İletişim bilgisi yok",
            RowTone.Neutral,
            RoleChoice.Describe(User.Role),
            User.Role == UserRole.Admin ? RowTone.Accent : RowTone.Neutral);
    }
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
