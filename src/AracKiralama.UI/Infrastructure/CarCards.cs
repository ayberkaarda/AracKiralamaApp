using System.Globalization;
using AracKiralama.Domain.Entities;
using AracKiralama.UI.Theming;

namespace AracKiralama.UI.Infrastructure;

/// <summary>The cards of the customer's car list.</summary>
internal static class CarCards
{
    /// <summary>Height of the drawn car on a card, in logical pixels.</summary>
    private const int PictureHeight = 128;

    /// <summary>
    /// A card with the car's drawing (or picture), its name with the plate, the year, gearbox, fuel and color,
    /// the daily price and a "Kirala" button. The name wraps instead of pushing the plate or the price away.
    /// </summary>
    public static (CardPanel Card, ThemedButton Rent) Build(Car car, Func<Task> rent)
    {
        ArgumentNullException.ThrowIfNull(car);
        ArgumentNullException.ThrowIfNull(rent);

        var id = car.Id.ToString(CultureInfo.InvariantCulture);
        var p = Theme.Current;
        var picture = new VehiclePicture { Name = "picCar" + id, Height = PictureHeight, Margin = new Padding(0, 0, 0, Spacing.M) };
        picture.ShowCar(car.Model, car.Color, car.ImageUrl, car.Brand + " " + car.Model);

        var title = new HeadingLabel
        {
            Name = "lblCarName" + id,
            Text = car.Brand + " " + car.Model,
            Font = Theme.Fonts.Subtitle,
            ForeColor = p.Text,
            Margin = Padding.Empty,
        };
        var plate = new PlateChip { Name = "lblPlate" + id, Text = car.Plate, Margin = new Padding(Spacing.S, 2, 0, 0) };
        var titleRow = FormLayout.Bar("titleRow" + id, (title, true), (plate, false));
        title.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;

        var details = FormLayout.Hint(Describe(car));
        details.Name = "lblDetails" + id;
        details.Margin = new Padding(0, Spacing.XS, 0, Spacing.M);

        var price = new Label
        {
            Name = "lblPrice" + id,
            Text = DisplayFormat.Money(car.DailyPrice),
            AutoSize = true,
            Font = Theme.Fonts.Figure,
            ForeColor = p.Text,
            UseMnemonic = false,
            Margin = Padding.Empty,
        };
        var perDay = FormLayout.Hint("/ gün");
        perDay.Margin = new Padding(Spacing.XS, 0, 0, 0);
        var button = FormLayout.Button("btnRent" + id, "Kirala", ButtonKind.Primary, Glyphs.Car);
        button.AccessibleName = car.Brand + " " + car.Model + " kirala";
        button.Margin = Padding.Empty;
        button.Click += async (_, _) => await rent();
        var priceRow = FormLayout.Bar("priceRow" + id, (price, false), (perDay, false), (FormLayout.Spacer("priceSpacer" + id), true), (button, false));
        price.Anchor = perDay.Anchor = AnchorStyles.Left;
        button.Anchor = AnchorStyles.Right;

        // The price row sits at the bottom, so the prices and buttons of a row of cards line up when one name wraps.
        var card = new CardPanel { Name = "carCard" + id, MeasureByContent = true, Margin = Padding.Empty };
        var stack = FormLayout.Stack("carStack" + id, picture, titleRow, details);
        priceRow.Dock = DockStyle.Bottom;
        card.Controls.Add(stack);
        card.Controls.Add(priceRow);
        return (card, button);
    }

    /// <summary>"2023 · Otomatik · Benzin · Beyaz"; a missing value is left out.</summary>
    public static string Describe(Car car)
    {
        ArgumentNullException.ThrowIfNull(car);

        return string.Join(
            " · ",
            new[] { car.Year.ToString(CultureInfo.InvariantCulture), car.Transmission, car.FuelType, car.Color }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}
