using System.Globalization;

namespace AracKiralama.UI.Infrastructure;

/// <summary>Application wide texts.</summary>
internal static class AppText
{
    /// <summary>Neutral product name used in window titles and message boxes.</summary>
    public const string Title = "Araç Kiralama";

    /// <summary>The user name as the screens greet the account: first letter upper case, the rest lower case.</summary>
    public static string DisplayName(string userName)
    {
        ArgumentNullException.ThrowIfNull(userName);
        if (userName.Length == 0)
        {
            return userName;
        }

        var turkish = DisplayFormat.Turkish;
        return char.ToUpper(userName[0], turkish) + userName[1..].ToLower(turkish);
    }
}

/// <summary>Formats dates and amounts the way the screens show them.</summary>
internal static class DisplayFormat
{
    public static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string Date(DateOnly value) => value.ToString("dd.MM.yyyy", Turkish);

    public static string Date(DateOnly? value) => value is { } date ? Date(date) : "-";

    /// <summary>"1.250,00 TL": thousands grouped, so prices and totals read at a glance.</summary>
    public static string Money(decimal value) => value.ToString("#,0.00", Turkish) + " TL";

    /// <summary>A price as the car form edits it (decimal comma, no currency).</summary>
    public static string MoneyInput(decimal value) => value.ToString("0.##", Turkish);
}

/// <summary>
/// The application's own artwork (assets/), embedded in the assembly. The images are shared by all forms
/// and live for the whole process, so forms must not dispose them.
/// </summary>
internal static class AppAssets
{
    private static readonly Lazy<Image> LazyLogo = new(() => LoadImage("logo-256.png"));
    private static readonly Lazy<Image> LazyBackground = new(() => LoadImage("background.png"));
    private static readonly Lazy<Image> LazyHero = new(() => LoadImage("hero.png"));
    private static readonly Lazy<Image> LazyCar = new(() => LoadImage("car.png"));
    private static readonly Lazy<Icon> LazyIcon = new(LoadIcon);

    /// <summary>The road and pin logo at 256 px (rasterized from assets/logo.svg), for painting it at any size.</summary>
    public static Image Logo => LazyLogo.Value;

    public static Image Background => LazyBackground.Value;

    /// <summary>The picture beside the sign-in fields.</summary>
    public static Image Hero => LazyHero.Value;

    /// <summary>Shown for a car without a picture or whose picture cannot be loaded.</summary>
    public static Image CarPlaceholder => LazyCar.Value;

    public static Icon Icon => LazyIcon.Value;

    private static Bitmap LoadImage(string name)
    {
        // Image.FromStream needs the stream for the lifetime of the image; a copy detaches it.
        using var stream = Open(name);
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    private static Icon LoadIcon()
    {
        using var stream = Open("app.ico");
        return new Icon(stream);
    }

    private static Stream Open(string name) =>
        typeof(AppAssets).Assembly.GetManifestResourceStream("AracKiralama.UI.Assets." + name)
            ?? throw new InvalidOperationException($"Embedded asset '{name}' is missing.");
}
