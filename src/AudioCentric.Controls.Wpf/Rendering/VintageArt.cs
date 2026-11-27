using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AudioCentric.Controls.Wpf.Rendering;

/// <summary>
/// Shared vector artwork for the vintage instrument look: bezels, screws, lamps, panel texture and text.
/// Everything is drawn procedurally in the caller's design coordinate space, so it stays crisp at any size or DPI.
/// </summary>
public static class VintageArt
{
    private static BitmapSource? _noise;

    /// <summary>Instrument lettering. Bahnschrift (a DIN-style face shipped with Windows) with fallbacks.</summary>
    public static readonly FontFamily InstrumentFont = new("Bahnschrift, DIN Alternate, Arial");

    public static Typeface Typeface(FontWeight weight) =>
        new(InstrumentFont, FontStyles.Normal, weight, FontStretches.Normal);

    public static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public static Pen FrozenPen(Brush brush, double thickness, PenLineCap cap = PenLineCap.Flat)
    {
        var p = new Pen(brush, thickness) { StartLineCap = cap, EndLineCap = cap, LineJoin = PenLineJoin.Round };
        p.Freeze();
        return p;
    }

    public static Color Blend(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    public static Color WithAlpha(Color c, double alpha) => Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), c.R, c.G, c.B);

    /// <summary>
    /// Deterministic fine grain texture (128 px tile), generated once. It is tiled at one texel per device pixel,
    /// so it never magnifies into visible pixels at any size or DPI.
    /// </summary>
    public static BitmapSource NoiseTexture
    {
        get
        {
            if (_noise != null) return _noise;
            const int size = 128;
            var pixels = new byte[size * size * 4];
            var rng = new Random(1940);
            for (int i = 0; i < size * size; i++)
            {
                int v = rng.Next(0, 256);
                bool light = v > 128;
                byte c = light ? (byte)255 : (byte)0;
                pixels[i * 4 + 0] = c;
                pixels[i * 4 + 1] = c;
                pixels[i * 4 + 2] = c;
                pixels[i * 4 + 3] = (byte)(Math.Abs(v - 128) / 128.0 * 38); // low-contrast grain
            }
            var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
            bmp.Freeze();
            _noise = bmp;
            return bmp;
        }
    }

    /// <summary>A tiled grain brush whose texels map to device pixels. <paramref name="devicePixelsPerUnit"/> = design scale × DPI scale.</summary>
    public static Brush GrainBrush(double devicePixelsPerUnit, double opacity)
    {
        double tile = NoiseTexture.PixelWidth / Math.Max(devicePixelsPerUnit, 1e-3);
        var brush = new ImageBrush(NoiseTexture)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, tile, tile),
            Stretch = Stretch.Fill,
            Opacity = opacity,
        };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        brush.Freeze();
        return brush;
    }

    /// <summary>Slotted screw head with a metallic dome and a recessed slot at <paramref name="slotAngle"/> degrees.</summary>
    public static void DrawScrew(DrawingContext dc, Point c, double r, double slotAngle)
    {
        // Countersink shadow
        var sink = new RadialGradientBrush(
            new GradientStopCollection
            {
                new(Color.FromArgb(200, 0, 0, 0), 0.75),
                new(Color.FromArgb(0, 0, 0, 0), 1.0),
            })
        { Center = new Point(0.5, 0.55), GradientOrigin = new Point(0.5, 0.55) };
        sink.Freeze();
        dc.DrawEllipse(sink, null, c, r * 1.35, r * 1.35);

        var head = new RadialGradientBrush(
            new GradientStopCollection
            {
                new(Color.FromRgb(0xC8, 0xC6, 0xC0), 0.0),
                new(Color.FromRgb(0x8A, 0x88, 0x84), 0.35),
                new(Color.FromRgb(0x4A, 0x48, 0x46), 0.8),
                new(Color.FromRgb(0x1E, 0x1D, 0x1C), 1.0),
            })
        { GradientOrigin = new Point(0.35, 0.3), Center = new Point(0.42, 0.4), RadiusX = 0.65, RadiusY = 0.65 };
        head.Freeze();
        dc.DrawEllipse(head, FrozenPen(Frozen(Color.FromRgb(0x10, 0x10, 0x10)), r * 0.08), c, r, r);

        dc.PushTransform(new RotateTransform(slotAngle, c.X, c.Y));
        double w = r * 0.22, len = r * 0.92;
        var slot = new Rect(c.X - len, c.Y - w / 2, len * 2, w);
        dc.DrawRoundedRectangle(Frozen(Color.FromRgb(0x14, 0x13, 0x12)), null, slot, w * 0.3, w * 0.3);
        // Light catching the lower lip of the slot
        dc.DrawLine(FrozenPen(Frozen(Color.FromArgb(150, 0xE0, 0xDE, 0xD8)), r * 0.05),
            new Point(c.X - len * 0.85, c.Y + w / 2), new Point(c.X + len * 0.85, c.Y + w / 2));
        dc.Pop();

        // Specular glint
        var glint = new RadialGradientBrush(Color.FromArgb(150, 255, 255, 255), Color.FromArgb(0, 255, 255, 255));
        glint.Freeze();
        dc.DrawEllipse(glint, null, new Point(c.X - r * 0.38, c.Y - r * 0.42), r * 0.35, r * 0.25);
    }

    /// <summary>A round indicator lamp (glass dome) in <paramref name="color"/>, lit or unlit, with optional glow.</summary>
    public static void DrawLamp(DrawingContext dc, Point c, double r, Color color, bool lit, double glow = 1.0)
    {
        // Bezel ring
        var ring = new LinearGradientBrush(Color.FromRgb(0x5A, 0x5A, 0x5A), Color.FromRgb(0x0C, 0x0C, 0x0C), 90);
        ring.Freeze();
        dc.DrawEllipse(ring, FrozenPen(Frozen(Color.FromRgb(0x05, 0x05, 0x05)), r * 0.06), c, r * 1.28, r * 1.28);

        if (lit && glow > 0)
        {
            var halo = new RadialGradientBrush(
                new GradientStopCollection
                {
                    new(WithAlpha(color, 0.55 * glow), 0.0),
                    new(WithAlpha(color, 0.25 * glow), 0.45),
                    new(WithAlpha(color, 0.0), 1.0),
                });
            halo.Freeze();
            dc.DrawEllipse(halo, null, c, r * 3.2, r * 3.2);
        }

        Color center = lit ? Blend(color, Colors.White, 0.55) : Blend(color, Colors.Black, 0.55);
        Color mid = lit ? color : Blend(color, Colors.Black, 0.72);
        Color edge = lit ? Blend(color, Colors.Black, 0.35) : Blend(color, Colors.Black, 0.88);
        var dome = new RadialGradientBrush(
            new GradientStopCollection { new(center, 0.0), new(mid, 0.55), new(edge, 1.0) })
        { GradientOrigin = new Point(0.45, 0.4), Center = new Point(0.5, 0.48) };
        dome.Freeze();
        dc.DrawEllipse(dome, FrozenPen(Frozen(Color.FromArgb(200, 0, 0, 0)), r * 0.08), c, r, r);

        var spec = new RadialGradientBrush(Color.FromArgb((byte)(lit ? 200 : 110), 255, 255, 255), Color.FromArgb(0, 255, 255, 255));
        spec.Freeze();
        dc.DrawEllipse(spec, null, new Point(c.X - r * 0.32, c.Y - r * 0.38), r * 0.42, r * 0.28);
    }

    public static FormattedText Text(string text, double size, Brush brush, FontWeight weight, double pixelsPerDip) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface(weight), size, brush, pixelsPerDip);

    /// <summary>Draws text centred on <paramref name="center"/>, rotated by <paramref name="angle"/> degrees.</summary>
    public static void DrawCenteredText(DrawingContext dc, FormattedText ft, Point center, double angle = 0)
    {
        if (angle != 0) dc.PushTransform(new RotateTransform(angle, center.X, center.Y));
        // Use the ink box (baseline/extent), not line height, so numerals sit visually centred.
        double inkTop = ft.Height + ft.OverhangAfter - ft.Extent;
        double top = center.Y - inkTop - ft.Extent / 2;
        dc.DrawText(ft, new Point(center.X - ft.Width / 2, top));
        if (angle != 0) dc.Pop();
    }
}
