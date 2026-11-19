using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace AracKiralama.UI.Theming;

/// <summary>Body style of the drawn vehicle.</summary>
internal enum VehicleShape
{
    Sedan,
    Hatchback,
    Suv,
    Van,
}

/// <summary>
/// The application's own vehicle illustration, drawn with GDI+ from a few shapes: a side view of a generic car
/// in the car's color, for every car without a bundled photograph or a picture of its own. The body style
/// follows the model's name (the same model always gets the same style), the paint follows the color field.
/// Under high contrast the car is an outline in the window text color.
/// </summary>
internal static class VehicleArt
{
    /// <summary>Width and height of the drawing's own coordinate system.</summary>
    private static readonly SizeF Canvas = new(200F, 84F);

    private static readonly (string Word, int Rgb)[] Paints =
    [
        ("beyaz", 0xF2F3F5), ("siyah", 0x2A2E33), ("gümüş", 0xB8BEC6), ("gri", 0x8A929C), ("füme", 0x5B636D),
        ("kırmızı", 0xC0392B), ("bordo", 0x7B1F2D), ("lacivert", 0x23365F), ("mavi", 0x2F63A6), ("turkuaz", 0x1F8A8A),
        ("yeşil", 0x2E7D4F), ("sarı", 0xE2B53A), ("turuncu", 0xE06A1B), ("kahverengi", 0x7A5236), ("bej", 0xD6C6A5),
        ("mor", 0x6A3E8E),
    ];

    /// <summary>A body style for <paramref name="model"/>, stable across runs (not the randomized string hash).</summary>
    public static VehicleShape ShapeFor(string? model)
    {
        var hash = 2166136261u;
        foreach (var c in (model ?? string.Empty).Trim().ToUpperInvariant())
        {
            hash = (hash ^ c) * 16777619u;
        }

        return (VehicleShape)(hash % 4);
    }

    /// <summary>The paint for a Turkish color name ("Beyaz", "Koyu Gri", "Metalik Mavi"); a neutral graphite otherwise.</summary>
    public static Color PaintFor(string? colorName)
    {
        var name = (colorName ?? string.Empty).ToLower(CultureInfo.GetCultureInfo("tr-TR"));
        foreach (var (word, rgb) in Paints)
        {
            if (name.Contains(word, StringComparison.Ordinal))
            {
                return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            }
        }

        return Color.FromArgb(0x4A, 0x55, 0x63);
    }

    /// <summary>
    /// Draws the vehicle centered in <paramref name="bounds"/>, keeping its proportions; <paramref name="background"/>
    /// is the color under it, which shows in the gap around the wheels.
    /// </summary>
    public static void Draw(Graphics graphics, RectangleF bounds, VehicleShape shape, Color paint, Color background)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (bounds.Width < 8 || bounds.Height < 8)
        {
            return;
        }

        var p = Theme.Current;
        var scale = Math.Min(bounds.Width / Canvas.Width, bounds.Height / Canvas.Height);
        var state = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TranslateTransform(
            bounds.X + ((bounds.Width - (Canvas.Width * scale)) / 2F),
            bounds.Y + ((bounds.Height - (Canvas.Height * scale)) / 2F));
        graphics.ScaleTransform(scale, scale);
        try
        {
            if (p.IsHighContrast)
            {
                DrawOutline(graphics, shape, p.Text, p.Surface);
            }
            else
            {
                DrawPainted(graphics, shape, paint, background, p);
            }
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    private static void DrawPainted(Graphics g, VehicleShape shape, Color paint, Color background, ThemePalette p)
    {
        var geometry = Geometry.Of(shape);
        var outline = Mix(paint, Color.Black, paint.GetBrightness() > 0.8F ? 0.35F : 0.3F);

        // A soft shadow on the road.
        using (var shadow = new SolidBrush(Color.FromArgb(p.IsDark ? 90 : 40, 0, 0, 0)))
        {
            g.FillEllipse(shadow, 10, 74, 180, 8);
        }

        using var body = geometry.Body();
        using var cabin = geometry.Cabin();
        using (var fill = new SolidBrush(paint))
        using (var edge = new Pen(outline, 8.8F) { LineJoin = LineJoin.Round })
        using (var round = new Pen(paint, 6F) { LineJoin = LineJoin.Round })
        {
            // The cabin's rounded edge: a wider stroke in the outline color under the paint, so a white car keeps
            // its roof line on a light tile.
            g.DrawPath(edge, cabin);
            g.FillPath(fill, cabin);
            g.DrawPath(round, cabin);
            g.FillPath(fill, body);
        }

        // A lighter band along the shoulder gives the paint some volume.
        using (var shine = new SolidBrush(Color.FromArgb(46, 255, 255, 255)))
        {
            g.FillRectangle(shine, geometry.BodyBounds.X + 8, geometry.BodyBounds.Y + 3, geometry.BodyBounds.Width - 16, 4);
        }

        using (var line = new Pen(outline, 1.4F) { LineJoin = LineJoin.Round })
        {
            g.DrawPath(line, body);
            var door = geometry.DoorX;
            g.DrawLine(line, door, geometry.BodyBounds.Y + 2, door, geometry.BodyBounds.Bottom - 6);
            g.DrawLine(line, door + 8, geometry.BodyBounds.Y + 9, door + 15, geometry.BodyBounds.Y + 9);
        }

        var glass = p.IsDark ? Color.FromArgb(0x34, 0x44, 0x56) : Color.FromArgb(0xC9, 0xD8, 0xE6);
        using (var windows = new SolidBrush(glass))
        {
            foreach (var window in geometry.Windows())
            {
                using (window)
                {
                    g.FillPath(windows, window);
                }
            }
        }

        using (var head = new SolidBrush(Color.FromArgb(0xFF, 0xE3, 0xA8)))
        {
            g.FillRectangle(head, geometry.BodyBounds.Right - 7, geometry.BodyBounds.Y + 6, 6, 5);
        }

        using (var tail = new SolidBrush(Color.FromArgb(0xC8, 0x3A, 0x32)))
        {
            g.FillRectangle(tail, geometry.BodyBounds.X + 1, geometry.BodyBounds.Y + 6, 5, 5);
        }

        foreach (var x in geometry.Wheels)
        {
            DrawWheel(g, x, geometry.WheelRadius, background);
        }
    }

    private static void DrawWheel(Graphics g, float x, float radius, Color arch)
    {
        const float Axle = 66F;
        using (var gap = new SolidBrush(arch))
        {
            g.FillEllipse(gap, x - radius - 3, Axle - radius - 3, (radius + 3) * 2, (radius + 3) * 2);
        }

        using (var tire = new SolidBrush(Color.FromArgb(0x1E, 0x22, 0x27)))
        {
            g.FillEllipse(tire, x - radius, Axle - radius, radius * 2, radius * 2);
        }

        using (var hub = new SolidBrush(Color.FromArgb(0xC6, 0xCC, 0xD3)))
        {
            g.FillEllipse(hub, x - (radius * 0.48F), Axle - (radius * 0.48F), radius * 0.96F, radius * 0.96F);
        }

        using var center = new SolidBrush(Color.FromArgb(0x6B, 0x74, 0x7F));
        g.FillEllipse(center, x - 2, Axle - 2, 4, 4);
    }

    private static void DrawOutline(Graphics g, VehicleShape shape, Color ink, Color window)
    {
        var geometry = Geometry.Of(shape);
        using var pen = new Pen(ink, 2.5F) { LineJoin = LineJoin.Round };
        using var body = geometry.Body();
        using var cabin = geometry.Cabin();
        using var clear = new SolidBrush(window);
        g.FillPath(clear, cabin);
        g.DrawPath(pen, cabin);
        g.FillPath(clear, body);
        g.DrawPath(pen, body);
        foreach (var x in geometry.Wheels)
        {
            const float Axle = 66F;
            var r = geometry.WheelRadius;
            g.FillEllipse(clear, x - r, Axle - r, r * 2, r * 2);
            g.DrawEllipse(pen, x - r, Axle - r, r * 2, r * 2);
            g.DrawEllipse(pen, x - (r * 0.45F), Axle - (r * 0.45F), r * 0.9F, r * 0.9F);
        }

        g.DrawLine(pen, 6, 80, 194, 80);
    }

    private static Color Mix(Color color, Color with, float amount) => Color.FromArgb(
        (int)Math.Round((color.R * (1 - amount)) + (with.R * amount)),
        (int)Math.Round((color.G * (1 - amount)) + (with.G * amount)),
        (int)Math.Round((color.B * (1 - amount)) + (with.B * amount)));

    /// <summary>The outline of one body style in the 200 x 84 drawing.</summary>
    private sealed record Geometry(RectangleF BodyBounds, PointF[] CabinPoints, float DoorX, float[] Wheels, float WheelRadius, PointF[][] WindowPoints)
    {
        public static Geometry Of(VehicleShape shape) => shape switch
        {
            VehicleShape.Hatchback => new(
                new RectangleF(20, 38, 162, 26),
                [new(40, 40), new(54, 15), new(118, 15), new(146, 40)],
                98,
                [56, 146],
                13,
                [[new(58, 37), new(68, 19), new(94, 19), new(94, 37)], [new(102, 19), new(116, 19), new(136, 37), new(102, 37)]]),
            VehicleShape.Suv => new(
                new RectangleF(8, 32, 184, 34),
                [new(28, 34), new(42, 8), new(138, 8), new(164, 34)],
                96,
                [48, 152],
                15,
                [[new(44, 31), new(52, 12), new(88, 12), new(88, 31)], [new(96, 12), new(134, 12), new(152, 31), new(96, 31)]]),
            VehicleShape.Van => new(
                new RectangleF(8, 28, 184, 38),
                [new(12, 30), new(18, 4), new(146, 4), new(180, 30)],
                118,
                [44, 156],
                14,
                [[new(22, 26), new(26, 9), new(70, 9), new(70, 26)], [new(78, 9), new(112, 9), new(112, 26), new(78, 26)], [new(120, 9), new(142, 9), new(166, 26), new(120, 26)]]),
            _ => new(
                new RectangleF(6, 38, 188, 26),
                [new(48, 40), new(72, 14), new(130, 14), new(158, 40)],
                102,
                [46, 154],
                13,
                [[new(62, 37), new(78, 18), new(98, 18), new(98, 37)], [new(106, 18), new(127, 18), new(146, 37), new(106, 37)]]),
        };

        public GraphicsPath Body() => Painting.RoundedRect(BodyBounds, 11F);

        public GraphicsPath Cabin()
        {
            var path = new GraphicsPath();
            path.AddPolygon(CabinPoints);
            return path;
        }

        public IEnumerable<GraphicsPath> Windows()
        {
            foreach (var points in WindowPoints)
            {
                var path = new GraphicsPath();
                path.AddPolygon(points);
                yield return path;
            }
        }
    }
}

/// <summary>
/// The picture of a car: a downloaded picture when the car has one that passed every check; otherwise the
/// bundled photograph of its make and model (<see cref="Infrastructure.CarPhotos"/>), filling the rounded tile;
/// otherwise the application's own <see cref="VehicleArt"/> on a quiet tile with a road line. The picture stays a
/// normal <see cref="PictureBox"/>, so <see cref="Infrastructure.CarPictures"/> loads into it as before.
/// </summary>
internal sealed class VehiclePicture : PictureBox, IThemeAware
{
    private VehicleShape _shape;
    private Color _paint = VehicleArt.PaintFor(null);
    private Image? _photo;

    /// <summary>The bundled photograph scaled to the tile once, so a repaint only fills the rounded shape with it.</summary>
    private Bitmap? _scaled;

    public VehiclePicture()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SizeMode = PictureBoxSizeMode.Zoom;
        Margin = Padding.Empty;
        TabStop = false;
        AccessibleRole = AccessibleRole.Graphic;
    }

    /// <summary>Body style and paint of the drawn car.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public VehicleShape Shape
    {
        get => _shape;
        set
        {
            _shape = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BodyPaint
    {
        get => _paint;
        set
        {
            _paint = value;
            Invalidate();
        }
    }

    /// <summary>Whether the bundled photograph of the car's make and model is shown (when no own picture is loaded).</summary>
    public bool HasPhoto => _photo is not null;

    /// <summary>
    /// Shows the bundled photograph of <paramref name="brand"/> <paramref name="model"/>, or else draws a car of the
    /// model in <paramref name="color"/>, and loads the car's own picture, if it has an acceptable one.
    /// </summary>
    public void ShowCar(string? brand, string? model, string? color, string? imageUrl, string description)
    {
        _shape = VehicleArt.ShapeFor(model);
        _paint = VehicleArt.PaintFor(color);
        SetPhoto(Infrastructure.CarPhotos.Find(brand, model));
        AccessibleName = description;
        Infrastructure.CarPictures.Show(this, imageUrl);
        Invalidate();
    }

    void IThemeAware.ApplyTheme(ThemeRemap remap) => Invalidate();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SetPhoto(null);
        }

        base.Dispose(disposing);
    }

    private void SetPhoto(Image? photo)
    {
        if (!ReferenceEquals(photo, _photo))
        {
            _scaled?.Dispose();
            _scaled = null;
        }

        // The photograph itself is shared by every screen and never disposed here.
        _photo = photo;
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        ArgumentNullException.ThrowIfNull(pevent);

        var p = Theme.Current;
        var g = pevent.Graphics;
        g.Clear(Painting.ParentBackground(this));
        var tile = new RectangleF(0, 0, Width, Height);
        Painting.FillRounded(g, tile, Theme.PxF(this, Radius.Card), p.IsHighContrast ? p.Surface : p.SurfaceAlt, p.IsHighContrast ? p.Border : null);
    }

    protected override void OnPaint(PaintEventArgs pe)
    {
        ArgumentNullException.ThrowIfNull(pe);
        if (Image is not null)
        {
            base.OnPaint(pe);
            return;
        }

        var p = Theme.Current;
        var g = pe.Graphics;
        if (_photo is not null)
        {
            DrawPhoto(g, _photo, p);
            return;
        }

        var pad = Theme.PxF(this, Spacing.M);
        var area = new RectangleF(pad, pad, Width - (2 * pad), Height - (2 * pad));
        if (!p.IsHighContrast)
        {
            // The road the car stands on: a hairline with a lane marking in the signal color.
            var ground = area.Bottom - (area.Height * 0.04F);
            using var road = new Pen(p.Border, Math.Max(1F, Theme.PxF(this, 1.5F)));
            g.DrawLine(road, area.Left, ground, area.Right, ground);
        }

        VehicleArt.Draw(g, area, _shape, _paint, p.IsHighContrast ? p.Surface : p.SurfaceAlt);
    }

    /// <summary>
    /// Fills the tile with the photograph, scaled to cover it and centered (the bundled photographs keep the car in
    /// the middle with room around it), with the tile's rounded corners and a hairline border.
    /// </summary>
    private void DrawPhoto(Graphics g, Image photo, ThemePalette p)
    {
        var size = ClientSize;
        if (size.Width < 2 || size.Height < 2)
        {
            return;
        }

        if (_scaled is null || _scaled.Size != size)
        {
            _scaled?.Dispose();
            _scaled = Cover(photo, size);
        }

        var state = g.Save();
        try
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var tile = new RectangleF(0, 0, size.Width, size.Height);
            var radius = Theme.PxF(this, Radius.Card);
            using (var brush = new TextureBrush(_scaled, WrapMode.Clamp))
            using (var shape = Painting.RoundedRect(tile, radius))
            {
                g.FillPath(brush, shape);
            }

            // A hairline keeps a light photograph apart from a light card (and is the tile's border under high contrast).
            Painting.FillRounded(g, tile, radius, Color.Transparent, p.Border);
        }
        finally
        {
            g.Restore(state);
        }
    }

    /// <summary>The photograph scaled with high quality so that it covers <paramref name="size"/>, cut to it around the center.</summary>
    private static Bitmap Cover(Image photo, Size size)
    {
        var scale = Math.Max((float)size.Width / photo.Width, (float)size.Height / photo.Height);
        var width = photo.Width * scale;
        var height = photo.Height * scale;
        var bitmap = new Bitmap(size.Width, size.Height);
        using var g = Graphics.FromImage(bitmap);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;

        // Mirrored edges keep the bicubic filter from blending the border with transparent pixels.
        using var attributes = new System.Drawing.Imaging.ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        var target = new Rectangle(
            (int)Math.Floor((size.Width - width) / 2F),
            (int)Math.Floor((size.Height - height) / 2F),
            (int)Math.Ceiling(width),
            (int)Math.Ceiling(height));
        g.DrawImage(photo, target, 0, 0, photo.Width, photo.Height, GraphicsUnit.Pixel, attributes);
        return bitmap;
    }
}
