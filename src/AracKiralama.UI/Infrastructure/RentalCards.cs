using System.Globalization;
using AracKiralama.Domain.Entities;
using AracKiralama.UI.Theming;

namespace AracKiralama.UI.Infrastructure;

/// <summary>The rental cards of the customer's rentals and of the admin's rental list.</summary>
internal static class RentalCards
{
    /// <summary>Narrowest rental card in logical pixels.</summary>
    public const int MinWidth = 400;

    public const string CancelText = "Kiralamayı İptal Et";

    public const string ReturnText = "İade Al";

    /// <summary>
    /// A card with the drawn car, its name and plate, the rental's stage in words, its days, its price and the
    /// buttons: "Sözleşme" saves the printable contract; "Kiralamayı İptal Et" before the pick-up day, "İade Al"
    /// once the rental is under way. With <paramref name="showRenter"/> the account's name and number are shown
    /// (admin only).
    /// </summary>
    public static (CardPanel Card, ThemedButton EndButton, ThemedButton ContractButton) Build(RentalDetails rental, bool underway, bool showRenter, Func<Task> end, Func<Task> contract)
    {
        ArgumentNullException.ThrowIfNull(rental);
        ArgumentNullException.ThrowIfNull(end);
        ArgumentNullException.ThrowIfNull(contract);

        var id = rental.Id.ToString(CultureInfo.InvariantCulture);
        var p = Theme.Current;
        var picture = new VehiclePicture { Name = "picRental" + id, Size = new Size(124, 76), Margin = new Padding(0, 0, Spacing.M, 0), Anchor = AnchorStyles.Left | AnchorStyles.Top };
        picture.ShowCar(rental.Model, null, rental.ImageUrl, rental.Brand + " " + rental.Model);

        var title = new HeadingLabel
        {
            Name = "lblRentalCar" + id,
            Text = rental.Brand + " " + rental.Model,
            Font = Theme.Fonts.Subtitle,
            ForeColor = p.Text,
            Margin = Padding.Empty,
        };
        var stage = new StatusPill
        {
            Name = "lblStage" + id,
            Text = underway ? "Devam ediyor" : "Başlamadı",
            Tone = underway ? RowTone.Accent : RowTone.Warm,
            Margin = new Padding(Spacing.S, 2, 0, 0),
        };
        var titleRow = FormLayout.Bar("rentalTitleRow" + id, (title, true), (stage, false));
        title.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        stage.Anchor = AnchorStyles.Right | AnchorStyles.Top;

        var plate = new PlateChip { Name = "lblRentalPlate" + id, Text = rental.Plate, Margin = new Padding(0, Spacing.XS, 0, Spacing.XS), Anchor = AnchorStyles.Left | AnchorStyles.Top };
        var days = FormLayout.Text("lblRentalDays" + id, Days(rental));
        days.ForeColor = p.TextSecondary;
        days.Margin = new Padding(0, Spacing.XS, 0, 0);
        var info = new List<Control> { titleRow, plate, days };
        if (showRenter)
        {
            var renter = new LineLabel
            {
                Name = "lblRenter" + id,
                Text = string.Create(CultureInfo.CurrentCulture, $"Kiracı: {rental.UserName} (No {rental.UserId})"),
                Font = Theme.Fonts.Caption,
                ForeColor = p.TextMuted,
                Margin = new Padding(0, Spacing.XS, 0, 0),
            };
            info.Add(renter);
        }

        var price = new Label
        {
            Name = "lblRentalPrice" + id,
            Text = DisplayFormat.Money(rental.Price),
            AutoSize = true,
            Font = Theme.Fonts.Figure,
            ForeColor = p.Text,
            UseMnemonic = false,
            Margin = new Padding(0, Spacing.S, 0, 0),
        };
        info.Add(price);
        var infoStack = FormLayout.Stack("rentalInfo" + id, [.. info]);
        plate.Dock = DockStyle.None;
        var top = new TableLayoutPanel
        {
            Name = "rentalTop" + id,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        top.Controls.Add(picture, 0, 0);
        top.Controls.Add(infoStack, 1, 0);
        infoStack.Dock = DockStyle.Fill;

        var contractButton = FormLayout.Button("btnContract" + id, "Sözleşme", ButtonKind.Secondary, Glyphs.Document);
        contractButton.AccessibleName = rental.Brand + " " + rental.Model + " sözleşmesini kaydet";
        contractButton.Margin = new Padding(0, 0, Spacing.S, 0);
        contractButton.Click += async (_, _) => await contract();
        var endButton = underway
            ? FormLayout.Button("btnEnd" + id, ReturnText, ButtonKind.Secondary, Glyphs.Return)
            : FormLayout.Button("btnEnd" + id, CancelText, ButtonKind.Danger, Glyphs.Cancel);
        endButton.Margin = Padding.Empty;
        endButton.Click += async (_, _) => await end();
        var actions = FormLayout.Bar("rentalActions" + id, (FormLayout.Spacer("rentalSpacer" + id), true), (contractButton, false), (endButton, false));
        actions.Margin = new Padding(0, Spacing.M, 0, 0);
        actions.Dock = DockStyle.Bottom;

        var card = new CardPanel { Name = "rentalCard" + id, MeasureByContent = true, Margin = Padding.Empty };
        card.Controls.Add(top);
        card.Controls.Add(actions);
        return (card, endButton, contractButton);
    }

    /// <summary>"12.03.2026 → 16.03.2026 · 4 gün": pick-up day, return day and the number of days.</summary>
    public static string Days(RentalDetails rental)
    {
        ArgumentNullException.ThrowIfNull(rental);

        var days = rental.EndDate.DayNumber - rental.StartDate.DayNumber;
        return string.Create(CultureInfo.CurrentCulture, $"{DisplayFormat.Date(rental.StartDate)} → {DisplayFormat.Date(rental.EndDate)} · {days} gün");
    }

    /// <summary>The question asked before a rental is ended.</summary>
    public static string Question(RentalDetails rental, bool underway)
    {
        ArgumentNullException.ThrowIfNull(rental);

        return underway
            ? $"{rental.Brand} {rental.Model} bugün iade alınacak. Ücret değişmez.\n\nDevam etmek istiyor musunuz?"
            : $"{rental.Brand} {rental.Model} için {DisplayFormat.Date(rental.StartDate)} tarihli kiralama iptal edilecek.\n\nDevam etmek istiyor musunuz?";
    }
}
