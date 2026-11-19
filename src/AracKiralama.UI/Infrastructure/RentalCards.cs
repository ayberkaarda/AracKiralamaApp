using AracKiralama.Domain.Entities;

namespace AracKiralama.UI.Infrastructure;

/// <summary>The rental cards of the profile and of the admin's rental panel.</summary>
internal static class RentalCards
{
    public const int Width = 600;
    public const int Height = 180;

    /// <summary>
    /// A card with the car's picture, the rental's days and price and the button that ends it: "İptal
    /// Et" before the pick-up day, "İade Al" once the rental is under way. With
    /// <paramref name="showRenter"/> the account's name and id are shown (admin only). "Sözleşme" saves the
    /// rental's printable contract.
    /// </summary>
    public static (Panel Card, Button EndButton, Button ContractButton) Build(RentalDetails rental, bool underway, bool showRenter, Func<Task> end, Func<Task> contract)
    {
        ArgumentNullException.ThrowIfNull(rental);
        ArgumentNullException.ThrowIfNull(end);
        ArgumentNullException.ThrowIfNull(contract);

        var card = new Panel { Size = new Size(Width, Height), BackColor = Color.FromArgb(230, 230, 230) };

        var picture = new PictureBox { Size = new Size(150, 120), Location = new Point(10, 30), SizeMode = PictureBoxSizeMode.Zoom };
        picture.ShowCarPicture(rental.ImageUrl);
        card.Controls.Add(picture);

        var text = $"Araç: {rental.Brand} {rental.Model} ({rental.Plate})\n"
            + $"Alış Tarihi: {DisplayFormat.Date(rental.StartDate)}\n"
            + $"İade Tarihi: {DisplayFormat.Date(rental.EndDate)}\n"
            + $"Toplam Ücret: {DisplayFormat.Money(rental.Price)}";
        if (showRenter)
        {
            text += $"\nKiracı: {rental.UserName} (ID: {rental.UserId})";
        }

        card.Controls.Add(new Label
        {
            Location = new Point(180, 20),
            Size = new Size(400, 100),
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.Black,
            Text = text,
        });

        var button = new Button
        {
            Text = underway ? "İade Al" : "Kiralamayı İptal Et",
            Size = new Size(160, 40),
            Location = new Point(420, 125),
            BackColor = Color.IndianRed,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        button.Click += async (_, _) => await end();
        card.Controls.Add(button);

        // System colours: the button keeps the look the theme, also high contrast, gives it. It sizes itself
        // from its font (never smaller than the original 120 x 40) and keeps its right edge next to the end
        // button, so a large font or scale grows it to the left instead of clipping the text.
        var contractButton = new Button
        {
            Name = "btnContract",
            Text = "Sözleşme",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(120, 40),
            Padding = new Padding(4, 0, 4, 0),
            UseVisualStyleBackColor = true,
        };
        contractButton.Click += async (_, _) => await contract();
        card.Controls.Add(contractButton);
        void Place() => contractButton.Location = new Point(button.Left - 10 - contractButton.Width, button.Top);
        contractButton.SizeChanged += (_, _) => Place();
        Place();

        if (SystemInformation.HighContrast)
        {
            // The card's own grey would hide the theme's colours.
            card.BackColor = SystemColors.Control;
            foreach (var label in card.Controls.OfType<Label>())
            {
                label.ForeColor = SystemColors.ControlText;
            }
        }

        return (card, button, contractButton);
    }

    /// <summary>The question asked before a rental is ended.</summary>
    public static string Question(RentalDetails rental, bool underway) =>
        underway
            ? $"{rental.Brand} {rental.Model} bugün iade alınacak. Ücret değişmez.\n\nDevam etmek istiyor musunuz?"
            : $"{rental.Brand} {rental.Model} için {DisplayFormat.Date(rental.StartDate)} tarihli kiralama iptal edilecek.\n\nDevam etmek istiyor musunuz?";
}
