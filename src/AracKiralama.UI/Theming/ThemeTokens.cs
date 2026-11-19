namespace AracKiralama.UI.Theming;

/// <summary>Spacing scale in logical pixels (96 DPI); layouts are scaled by Windows Forms for the monitor.</summary>
internal static class Spacing
{
    public const int XS = 4;
    public const int S = 8;
    public const int M = 12;
    public const int L = 16;
    public const int XL = 24;
    public const int XXL = 32;

    /// <summary>All steps in ascending order.</summary>
    public static IReadOnlyList<int> Scale { get; } = [XS, S, M, L, XL, XXL];
}

/// <summary>Corner radii in logical pixels.</summary>
internal static class Radius
{
    /// <summary>Buttons, inputs, plates and status pills.</summary>
    public const int Control = 6;

    /// <summary>Cards, banners and badges.</summary>
    public const int Card = 10;
}

/// <summary>Fixed control sizes in logical pixels.</summary>
internal static class Sizes
{
    /// <summary>Height of buttons, text fields and drop-down lists, so a row of them lines up.</summary>
    public const int ControlHeight = 34;

    public const int ButtonMinWidth = 96;

    /// <summary>Glyph badge in screen headers and message windows.</summary>
    public const int HeaderBadge = 40;

    /// <summary>Logo badge of the brand panel.</summary>
    public const int LogoBadge = 56;

    /// <summary>Width of the brand panel on the sign-in screen.</summary>
    public const int BrandPanelWidth = 300;

    /// <summary>Height of a multi-line note field.</summary>
    public const int NoteHeight = 72;

    /// <summary>Narrowest car or rental card; a wider list shows more cards side by side.</summary>
    public const int CardMinWidth = 300;

    public const int FocusRing = 2;
}

/// <summary>One step of the type ramp; sizes are in points.</summary>
internal readonly record struct FontToken(string Family, float Size, FontStyle Style = FontStyle.Regular);

/// <summary>
/// Type ramp. Headings, prices, plates and calendar days use Bahnschrift, the DIN-style face that ships with
/// Windows: the letterforms of road signs and number plates, which gives the screens their road character.
/// Running text stays Segoe UI, the Windows UI face, which reads best in long Turkish sentences. The hierarchy
/// comes from size and weight, never from color alone. Where Bahnschrift is missing the headings fall back
/// to Segoe UI Semibold. Display &gt; Title &gt; Subtitle &gt; Body &gt; Caption.
/// </summary>
internal static class Typography
{
    public const string Family = "Segoe UI";
    public const string SemiboldFamily = "Segoe UI Semibold";

    /// <summary>The road-sign face of headings and figures.</summary>
    public const string SignFamily = "Bahnschrift SemiBold";

    /// <summary>The regular weight of the road-sign face, for number plates.</summary>
    public const string SignRegularFamily = "Bahnschrift";

    public static readonly FontToken Display = new(SignFamily, 20F);
    public static readonly FontToken Title = new(SignFamily, 15F);
    public static readonly FontToken Subtitle = new(SignFamily, 12F);
    public static readonly FontToken Body = new(Family, 9.75F);
    public static readonly FontToken BodyStrong = new(SemiboldFamily, 9.75F);
    public static readonly FontToken Caption = new(Family, 9F);

    /// <summary>Prices and totals: read at a glance, so they are larger than body text.</summary>
    public static readonly FontToken Figure = new(SignFamily, 13.5F);

    /// <summary>A number plate written on a plate chip.</summary>
    public static readonly FontToken Plate = new(SignRegularFamily, 9.75F);

    /// <summary>Day numbers of the availability calendar.</summary>
    public static readonly FontToken DayNumber = new(SignFamily, 11F);

    /// <summary>Text sizes from the smallest to the largest; used to check the ramp is ordered.</summary>
    public static IReadOnlyList<FontToken> Ramp { get; } = [Caption, Body, Subtitle, Title, Display];
}

/// <summary>Code points of Segoe Fluent Icons / Segoe MDL2 Assets used by the application.</summary>
internal static class Glyphs
{
    public const string Car = "";
    public const string Calendar = "";
    public const string Repair = "";
    public const string Chart = "";
    public const string Document = "";
    public const string Filter = "";
    public const string Refresh = "";
    public const string Person = "";
    public const string People = "";
    public const string AddPerson = "";
    public const string Lock = "";
    public const string Key = "";
    public const string Admin = "";
    public const string Add = "";
    public const string Save = "";
    public const string Delete = "";
    public const string Cancel = "";
    public const string Return = "";
    public const string Download = "";
    public const string Pin = "";
    public const string List = "";
    public const string SignOut = "";
    public const string Info = "";
    public const string Success = "";
    public const string Warning = "";
    public const string Error = "";
    public const string Question = "";
    public const string CheckMark = "";
    public const string ChevronDown = "";
    public const string ChevronLeft = "";
    public const string ChevronRight = "";
}
