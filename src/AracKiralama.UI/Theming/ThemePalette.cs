namespace AracKiralama.UI.Theming;

/// <summary>
/// Named colors of one theme. Controls never use literal colors; they read a role from the current
/// palette, so the light, dark and high contrast themes stay consistent.
/// </summary>
internal sealed record ThemePalette
{
    public required string Name { get; init; }

    public bool IsDark { get; init; }

    /// <summary>Built from the Windows high contrast colors; custom painting steps back for native drawing.</summary>
    public bool IsHighContrast { get; init; }

    /// <summary>Window background behind cards: a cool, slightly blue grey, like dry asphalt in daylight.</summary>
    public required Color Page { get; init; }

    /// <summary>Cards, inputs, header and footer.</summary>
    public required Color Surface { get; init; }

    /// <summary>Quiet areas: disabled inputs and the footer of the message dialog.</summary>
    public required Color SurfaceAlt { get; init; }

    public required Color SurfaceHover { get; init; }

    /// <summary>Decorative hairlines between areas.</summary>
    public required Color Border { get; init; }

    /// <summary>Borders of inputs; at least 3:1 against the surface.</summary>
    public required Color BorderStrong { get; init; }

    public required Color Text { get; init; }

    public required Color TextSecondary { get; init; }

    public required Color TextMuted { get; init; }

    public required Color TextDisabled { get; init; }

    /// <summary>The asphalt graphite of the logo: primary actions, selection and focus.</summary>
    public required Color Accent { get; init; }

    public required Color AccentHover { get; init; }

    public required Color AccentPressed { get; init; }

    /// <summary>Text and glyphs on <see cref="Accent"/>.</summary>
    public required Color OnAccent { get; init; }

    /// <summary>Secondary text on <see cref="Accent"/>.</summary>
    public required Color OnAccentMuted { get; init; }

    /// <summary>Tinted background for selected rows and quiet badges.</summary>
    public required Color AccentSubtle { get; init; }

    /// <summary>Accent used as text or a glyph on <see cref="Surface"/>.</summary>
    public required Color AccentText { get; init; }

    /// <summary>
    /// The signal orange of the logo's pin and of road markings: a small mark for the identity (the brand rule,
    /// header badges, the edge of the selected row, the price) and for what is the user's own. Decorative only;
    /// text in this family uses <see cref="WarmText"/>.
    /// </summary>
    public required Color Warm { get; init; }

    public required Color WarmSubtle { get; init; }

    public required Color WarmText { get; init; }

    public required Color Success { get; init; }

    public required Color SuccessSubtle { get; init; }

    public required Color Warning { get; init; }

    public required Color WarningSubtle { get; init; }

    public required Color Danger { get; init; }

    public required Color DangerSubtle { get; init; }

    public required Color Info { get; init; }

    public required Color InfoSubtle { get; init; }

    /// <summary>
    /// The asphalt panel of the sign-in screen and the tile of the header badges. It stays a deep ink in the
    /// dark palette too, where <see cref="Accent"/> turns light.
    /// </summary>
    public required Color Brand { get; init; }

    /// <summary>Text on <see cref="Brand"/>.</summary>
    public required Color OnBrand { get; init; }

    /// <summary>Secondary text on <see cref="Brand"/>.</summary>
    public required Color OnBrandMuted { get; init; }

    /// <summary>Raised tiles on <see cref="Brand"/>, such as the badges of the brand panel's points.</summary>
    public required Color BrandRaised { get; init; }

    /// <summary>
    /// Light theme: cool asphalt greys with a deep graphite accent and a signal orange mark. Primary buttons are
    /// graphite, so the one orange on a screen is the brand and never a second call to action; the accent's
    /// blue-grey hue is far from the red danger role, so deleting a car never looks like the primary action.
    /// </summary>
    public static ThemePalette Light { get; } = new()
    {
        Name = "Light",
        Page = Hex(0xF1F3F5),
        Surface = Hex(0xFFFFFF),
        SurfaceAlt = Hex(0xF6F7F9),
        SurfaceHover = Hex(0xECEFF2),
        Border = Hex(0xDCE0E5),
        BorderStrong = Hex(0x7A8390),
        Text = Hex(0x15181C),
        TextSecondary = Hex(0x3D4550),
        TextMuted = Hex(0x555E69),
        TextDisabled = Hex(0xA2A8B0),
        Accent = Hex(0x253141),
        AccentHover = Hex(0x323F51),
        AccentPressed = Hex(0x1A232E),
        OnAccent = Hex(0xFFFFFF),
        OnAccentMuted = Hex(0xC3CBD5),
        AccentSubtle = Hex(0xE6EAEF),
        AccentText = Hex(0x2A3D56),
        Warm = Hex(0xF0661A),
        WarmSubtle = Hex(0xFDEADC),
        WarmText = Hex(0x9C3B00),
        Success = Hex(0x1C6A42),
        SuccessSubtle = Hex(0xE4F2E9),
        Warning = Hex(0x7A4A00),
        WarningSubtle = Hex(0xFAEED5),
        Danger = Hex(0xB3261E),
        DangerSubtle = Hex(0xFBE8E6),
        Info = Hex(0x2A4F86),
        InfoSubtle = Hex(0xE7EDF6),
        Brand = Hex(0x212933),
        OnBrand = Hex(0xFFFFFF),
        OnBrandMuted = Hex(0xBFC7D0),
        BrandRaised = Hex(0x2E3845),
    };

    /// <summary>
    /// Dark theme: the same roles stepped for a night road, not an inverted light theme. The accent turns a
    /// light steel so primary buttons stay the brightest thing on a card; the brand panel stays deep ink.
    /// </summary>
    public static ThemePalette Dark { get; } = new()
    {
        Name = "Dark",
        IsDark = true,
        Page = Hex(0x101215),
        Surface = Hex(0x181B1F),
        SurfaceAlt = Hex(0x1D2025),
        SurfaceHover = Hex(0x252930),
        Border = Hex(0x2F343B),
        BorderStrong = Hex(0x7B8490),
        Text = Hex(0xECEEF1),
        TextSecondary = Hex(0xC1C6CD),
        TextMuted = Hex(0x9FA6AF),
        TextDisabled = Hex(0x5B626B),
        Accent = Hex(0xB4C2D3),
        AccentHover = Hex(0xC5D0DD),
        AccentPressed = Hex(0xA2B1C4),
        OnAccent = Hex(0x11161C),
        OnAccentMuted = Hex(0x2C3744),
        AccentSubtle = Hex(0x243040),
        AccentText = Hex(0xBACADC),
        Warm = Hex(0xFF8A3D),
        WarmSubtle = Hex(0x3A2416),
        WarmText = Hex(0xFFB07A),
        Success = Hex(0x7FD3A0),
        SuccessSubtle = Hex(0x14302A),
        Warning = Hex(0xF0BF5C),
        WarningSubtle = Hex(0x33290F),
        Danger = Hex(0xFF9C8C),
        DangerSubtle = Hex(0x3C1B16),
        Info = Hex(0x9DB8F5),
        InfoSubtle = Hex(0x1A2440),
        Brand = Hex(0x1A2129),
        OnBrand = Hex(0xEEF1F4),
        OnBrandMuted = Hex(0xA9B3BE),
        BrandRaised = Hex(0x26303B),
    };

    /// <summary>
    /// High contrast theme built from the active Windows system colors, so the user's contrast theme is
    /// respected instead of being replaced by the application's colors. Selected and hovered content is
    /// written in HighlightText on Highlight.
    /// </summary>
    public static ThemePalette HighContrast() => new()
    {
        Name = "HighContrast",
        IsHighContrast = true,
        IsDark = SystemColors.Window.GetBrightness() < 0.5f,
        Page = SystemColors.Window,
        Surface = SystemColors.Window,
        SurfaceAlt = SystemColors.Window,
        SurfaceHover = SystemColors.Highlight,
        Border = SystemColors.WindowText,
        BorderStrong = SystemColors.WindowText,
        Text = SystemColors.WindowText,
        TextSecondary = SystemColors.WindowText,
        TextMuted = SystemColors.WindowText,
        TextDisabled = SystemColors.GrayText,
        Accent = SystemColors.Highlight,
        AccentHover = SystemColors.Highlight,
        AccentPressed = SystemColors.Highlight,
        OnAccent = SystemColors.HighlightText,
        OnAccentMuted = SystemColors.HighlightText,
        AccentSubtle = SystemColors.Window,
        AccentText = SystemColors.HotTrack,
        Warm = SystemColors.WindowText,
        WarmSubtle = SystemColors.Window,
        WarmText = SystemColors.WindowText,
        Success = SystemColors.WindowText,
        SuccessSubtle = SystemColors.Window,
        Warning = SystemColors.WindowText,
        WarningSubtle = SystemColors.Window,
        Danger = SystemColors.WindowText,
        DangerSubtle = SystemColors.Window,
        Info = SystemColors.WindowText,
        InfoSubtle = SystemColors.Window,
        Brand = SystemColors.Window,
        OnBrand = SystemColors.WindowText,
        OnBrandMuted = SystemColors.WindowText,
        BrandRaised = SystemColors.Window,
    };

    private static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
}
