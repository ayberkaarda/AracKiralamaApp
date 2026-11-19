using System.ComponentModel;

namespace AracKiralama.UI.Theming;

/// <summary>
/// A container that scrolls its own content, so the window is sized for its visible part only: the frame's
/// fit-to-content step does not grow the window for the rows below it.
/// </summary>
internal interface ISelfScrolling;

/// <summary>
/// The car and rental cards of a list: as many columns as fit at <see cref="Sizes.CardMinWidth"/>, each card as
/// tall as its content at the column's width, rows that scroll inside the grid, and a message in place of the
/// cards while they load or when there are none. The cards are ordinary controls, so Tab, the buttons on them
/// and screen readers work as everywhere else.
/// </summary>
internal sealed class CardGrid : Panel, ISelfScrolling, IThemeAware
{
    private string _emptyText = string.Empty;
    private bool _loading;
    private bool _arranging;

    public CardGrid()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoScroll = true;
        Margin = Padding.Empty;
        BackColor = Theme.Current.Page;
        MinimumSize = new Size(0, 160);
    }

    /// <summary>Shown in the middle of the grid while it has no cards.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string EmptyText
    {
        get => _emptyText;
        set
        {
            _emptyText = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string LoadingText { get; set; } = "Yükleniyor…";

    /// <summary>A request for the cards is on its way; an empty grid says so instead of "nothing here".</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsLoading
    {
        get => _loading;
        set
        {
            if (_loading != value)
            {
                _loading = value;
                Invalidate();
            }
        }
    }

    /// <summary>Narrowest card in logical pixels; a wide grid shows more columns.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int MinCardWidth { get; set; } = Sizes.CardMinWidth;

    /// <summary>The message drawn in place of the cards, or nothing while there are cards.</summary>
    internal string? Message => Controls.Count > 0 ? null : _loading ? LoadingText : _emptyText;

    void IThemeAware.ApplyTheme(ThemeRemap remap) => Invalidate();

    /// <summary>Removes and disposes the cards, then adds <paramref name="cards"/> in one layout pass.</summary>
    public void Replace(IEnumerable<Control> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        SuspendLayout();
        try
        {
            foreach (var old in Controls.Cast<Control>().ToList())
            {
                old.Dispose();
            }

            AutoScrollPosition = Point.Empty;
            foreach (var card in cards)
            {
                Controls.Add(card);
            }
        }
        finally
        {
            ResumeLayout(true);
        }

        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        if (!_arranging)
        {
            _arranging = true;
            try
            {
                // The cards are placed for the width that is left once a vertical scroll bar is needed, so no
                // horizontal bar ever appears; the scroll range is set before the panel lays out its scroll bars.
                var full = Width - BorderWidth();
                var height = Arrange(full);
                if (height > Height - BorderWidth())
                {
                    height = Arrange(full - SystemInformation.VerticalScrollBarWidth);
                }

                var minimum = new Size(0, height);
                if (AutoScrollMinSize != minimum)
                {
                    AutoScrollMinSize = minimum;
                }
            }
            finally
            {
                _arranging = false;
            }
        }

        base.OnLayout(levent);
    }

    private int BorderWidth() => BorderStyle == BorderStyle.None ? 0 : 2 * SystemInformation.BorderSize.Width;

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPaint(e);
        PaintTo(e.Graphics);
    }

    /// <summary>
    /// Paints the empty or loading message while the grid has no cards. DrawToBitmap (used by the offscreen
    /// screenshot tool) prints the panel through this as well.
    /// </summary>
    internal void PaintTo(Graphics graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (Message is not { Length: > 0 } message)
        {
            return;
        }

        var pad = Theme.Px(this, Spacing.XL);
        var area = new Rectangle(pad, pad, Math.Max(0, ClientSize.Width - (2 * pad)), Math.Max(0, ClientSize.Height - (2 * pad)));
        TextRenderer.DrawText(
            graphics,
            message,
            Theme.Fonts.Body,
            area,
            Theme.Current.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }

    /// <summary>Places the cards row by row in <paramref name="available"/> pixels of width; returns the height they need.</summary>
    private int Arrange(int available)
    {
        var gap = Theme.Px(this, Spacing.L);
        var width = Math.Max(1, available);
        var columns = Math.Max(1, (width + gap) / (Theme.Px(this, MinCardWidth) + gap));
        var cards = Controls.Cast<Control>().ToList();
        columns = Math.Max(1, Math.Min(columns, Math.Max(1, cards.Count)));
        var cardWidth = Math.Max(1, (width - (gap * (columns - 1))) / columns);
        var offset = AutoScrollPosition;
        var top = 0;
        for (var start = 0; start < cards.Count; start += columns)
        {
            var row = cards.Skip(start).Take(columns).ToList();
            var height = row.Max(card => card.GetPreferredSize(new Size(cardWidth, 0)).Height);
            for (var i = 0; i < row.Count; i++)
            {
                row[i].SetBounds(offset.X + (i * (cardWidth + gap)), offset.Y + top, cardWidth, height);
            }

            top += height + gap;
        }

        return Math.Max(0, top - gap);
    }
}

/// <summary>
/// A number plate on a chip: the plate in the road-sign face on a white plate with a dark rim and the blue
/// country band of a Turkish plate's left edge, so it reads as a plate at a glance. Under high contrast it is
/// an outline in the window text color.
/// </summary>
internal sealed class PlateChip : Control
{
    private const TextFormatFlags Flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    public PlateChip()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Font = Theme.Fonts.Plate;
        Margin = Padding.Empty;
        AccessibleRole = AccessibleRole.StaticText;
        AutoSize = true;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty, Flags);
        return new Size(text.Width + Theme.Px(this, Band + (2 * Spacing.S)), text.Height + Theme.Px(this, 6));
    }

    /// <summary>Width of the country band in logical pixels.</summary>
    private const int Band = 7;

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        AccessibleName = "Plaka " + Text;
        if (AutoSize)
        {
            Size = GetPreferredSize(Size.Empty);
        }

        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (AutoSize)
        {
            Size = GetPreferredSize(Size.Empty);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var p = Theme.Current;
        var g = e.Graphics;
        g.Clear(Painting.ParentBackground(this));
        var bounds = new RectangleF(0, 0, Width, Height);
        var radius = Theme.PxF(this, 4);
        var band = Theme.Px(this, Band);
        Color plate, rim, ink;
        if (p.IsHighContrast)
        {
            (plate, rim, ink) = (p.Surface, p.Text, p.Text);
        }
        else
        {
            // A plate is white with black letters in either palette; only its rim follows the theme.
            (plate, rim, ink) = (Color.White, p.IsDark ? Color.FromArgb(0x8A, 0x93, 0x9E) : Color.FromArgb(0x2A, 0x2E, 0x33), Color.FromArgb(0x15, 0x18, 0x1C));
        }

        Painting.FillRounded(g, bounds, radius, plate, rim, Math.Max(1F, Theme.PxF(this, 1)));
        if (!p.IsHighContrast)
        {
            using var clip = Painting.RoundedRect(RectangleF.Inflate(bounds, -0.5F, -0.5F), radius);
            g.SetClip(clip);
            using var blue = new SolidBrush(Color.FromArgb(0x1F, 0x4E, 0x9A));
            g.FillRectangle(blue, 0, 0, band, Height);
            g.ResetClip();
        }

        var text = new Rectangle(band, 0, Width - band, Height);
        TextRenderer.DrawText(g, Text, Font, text, ink, Flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>
/// A small pill that states something in words (a rental's stage, a record's state), tinted by its tone so it
/// reads at a glance, but never by color alone. Under high contrast it is an outline in the window text color.
/// </summary>
internal sealed class StatusPill : Control
{
    private const TextFormatFlags Flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    private RowTone _tone;

    public StatusPill()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Font = Theme.Fonts.Caption;
        Margin = Padding.Empty;
        AccessibleRole = AccessibleRole.StaticText;
        AutoSize = true;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RowTone Tone
    {
        get => _tone;
        set
        {
            _tone = value;
            Invalidate();
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty, Flags);
        return new Size(text.Width + Theme.Px(this, 2 * Spacing.S), text.Height + Theme.Px(this, 4));
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        AccessibleName = Text;
        if (AutoSize)
        {
            Size = GetPreferredSize(Size.Empty);
        }

        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (AutoSize)
        {
            Size = GetPreferredSize(Size.Empty);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var g = e.Graphics;
        g.Clear(Painting.ParentBackground(this));
        var (fill, border, text) = ThemedListBox.PillColors(Theme.Current, selected: false, _tone);
        var bounds = new RectangleF(0, 0, Width, Height);
        Painting.FillRounded(g, bounds, Height / 2F, fill, border, Math.Max(1F, Theme.PxF(this, 1)));
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, text, Flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
