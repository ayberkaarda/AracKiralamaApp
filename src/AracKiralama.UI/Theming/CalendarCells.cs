using System.ComponentModel;
using System.Drawing.Drawing2D;
using AracKiralama.Business.Services;

namespace AracKiralama.UI.Theming;

/// <summary>
/// How a day of the availability calendar is drawn. Every state has a pattern of its own besides its tint (plain
/// for free, diagonal lines for booked, cross-hatch for maintenance, a dotted ground for past days), and the cell
/// writes the state in words, so neither color nor pattern alone carries it. Under high contrast the patterns are
/// drawn in the window text color on the window color, and no other color is used.
/// </summary>
internal static class DayPainter
{
    public static void Paint(Graphics graphics, Control control, Rectangle bounds, DayState? state, bool today)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(control);

        var p = Theme.Current;
        var radius = Theme.PxF(control, Radius.Control);
        var box = RectangleF.Inflate(bounds, -0.5F, -0.5F);
        if (state is not { } day)
        {
            return;
        }

        var (fill, ink) = Colors(p, day);
        Painting.FillRounded(graphics, box, radius, fill, p.IsHighContrast ? p.Border : Mix(ink, fill), Math.Max(1F, Theme.PxF(control, 1)));
        if (Pattern(day) is { } style)
        {
            using var clip = Painting.RoundedRect(box, radius);
            var saved = graphics.Save();
            graphics.SetClip(clip);
            graphics.SmoothingMode = SmoothingMode.None;
            using var hatch = new HatchBrush(style, p.IsHighContrast ? p.Text : Color.FromArgb(day == DayState.Past ? 40 : 70, ink), Color.Transparent);
            graphics.FillRectangle(hatch, box);
            graphics.Restore(saved);
        }

        if (today)
        {
            var ring = Theme.PxF(control, p.IsHighContrast ? 3 : 2);
            Painting.FillRounded(graphics, RectangleF.Inflate(box, -ring / 2F, -ring / 2F), radius, Color.Transparent, p.IsHighContrast ? p.Text : p.Warm, ring);
        }
    }

    /// <summary>Fill of the cell and the color of its state word.</summary>
    public static (Color Fill, Color Ink) Colors(ThemePalette palette, DayState state)
    {
        ArgumentNullException.ThrowIfNull(palette);

        if (palette.IsHighContrast)
        {
            return (palette.Surface, palette.Text);
        }

        return state switch
        {
            DayState.Booked => (palette.DangerSubtle, palette.Danger),
            DayState.Maintenance => (palette.WarningSubtle, palette.Warning),
            DayState.Past => (palette.SurfaceAlt, palette.TextMuted),
            _ => (palette.SuccessSubtle, palette.Success),
        };
    }

    /// <summary>The pattern of a state; free days are plain.</summary>
    public static HatchStyle? Pattern(DayState state) => state switch
    {
        DayState.Booked => HatchStyle.WideUpwardDiagonal,
        DayState.Maintenance => HatchStyle.Cross,
        DayState.Past => HatchStyle.Percent10,
        _ => null,
    };

    private static Color Mix(Color color, Color background) => Theme.Flatten(Color.FromArgb(90, color), background);
}

/// <summary>
/// One day of the availability calendar: the day number in the road-sign face and the state in words below it,
/// on the state's tint and pattern, with a ring around today. The cell keeps no text color of its own
/// (<see cref="Control.ForeColor"/> is inherited), so high contrast keeps the system's.
/// </summary>
internal sealed class DayCell : Label
{
    private const TextFormatFlags Flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    /// <summary>The longest words a cell holds; the cell is sized for them at any font size.</summary>
    internal static readonly string[] Words = ["Geçmiş", "Bakım", "Dolu", "Boş", "30"];

    private DayState? _state;
    private bool _today;

    public DayCell()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        // Sized from the fonts, not from the text: a new month changes 42 texts without laying the grid out again.
        AutoSize = false;
        UseMnemonic = false;
        Margin = new Padding(2);
        TextAlign = ContentAlignment.MiddleCenter;
        Size = GetPreferredSize(Size.Empty);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Size = GetPreferredSize(Size.Empty);
    }

    /// <summary>The window's scaling does not stretch the cell; its size follows the fonts, which follow the monitor.</summary>
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        base.ScaleControl(factor, specified);
        Size = GetPreferredSize(Size.Empty);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DayState? State => _state;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsToday => _today;

    /// <summary>Shows a day (its number and state in <paramref name="text"/>), or an empty cell outside the month.</summary>
    public void Show(string text, DayState? state, bool today)
    {
        _state = state;
        _today = today;
        Text = text;
        AccessibleName = today ? text.Replace(Environment.NewLine, " ", StringComparison.Ordinal) + ", bugün" : text.Replace(Environment.NewLine, " ", StringComparison.Ordinal);
        Invalidate();
    }

    /// <summary>Two lines of text: the number in the day face and the widest state word, with room around them.</summary>
    public static Size SizeFor(Font numberFont, Font wordFont)
    {
        ArgumentNullException.ThrowIfNull(numberFont);
        ArgumentNullException.ThrowIfNull(wordFont);

        var widest = Words.Max(w => TextRenderer.MeasureText(w, wordFont, Size.Empty, Flags).Width);
        var pad = wordFont.Height / 2;
        return new Size(widest + (2 * pad), numberFont.Height + wordFont.Height + (2 * pad));
    }

    public override Size GetPreferredSize(Size proposedSize) => SizeFor(Theme.Fonts.DayNumber, Font);

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var p = Theme.Current;
        var g = e.Graphics;
        g.Clear(Painting.ParentBackground(this));
        if (_state is not { } state)
        {
            return;
        }

        DayPainter.Paint(g, this, ClientRectangle, state, _today);
        var (_, ink) = DayPainter.Colors(p, state);
        var lines = Text.Split(Environment.NewLine);
        var pad = Font.Height / 2;
        var numberFont = Theme.Fonts.DayNumber;
        var numberColor = p.IsHighContrast ? p.Text : state == DayState.Past ? p.TextMuted : p.Text;
        TextRenderer.DrawText(g, lines[0], numberFont, new Rectangle(pad, pad - 2, Width - (2 * pad), numberFont.Height), numberColor, Flags);
        if (lines.Length > 1)
        {
            TextRenderer.DrawText(g, lines[1], Font, new Rectangle(pad, Height - pad - Font.Height, Width - (2 * pad), Font.Height), ink, Flags);
        }
    }
}

/// <summary>A small sample of a state's tint and pattern for the calendar's legend.</summary>
internal sealed class DaySwatch : Control
{
    public DaySwatch(DayState state, bool today = false)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        State = state;
        Today = today;
        TabStop = false;
        Size = new Size(28, 20);
        Margin = new Padding(0, 0, Spacing.S, 0);
        AccessibleRole = AccessibleRole.Graphic;
    }

    public DayState State { get; }

    public bool Today { get; }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        e.Graphics.Clear(Painting.ParentBackground(this));
        DayPainter.Paint(e.Graphics, this, ClientRectangle, State, Today);
    }
}
