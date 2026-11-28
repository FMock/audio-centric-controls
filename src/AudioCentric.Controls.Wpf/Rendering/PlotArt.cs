using System.Windows;
using System.Windows.Media;

namespace AudioCentric.Controls.Wpf.Rendering;

/// <summary>
/// Shared artwork for resizable plot instruments (spectrum analyzer, response graph): a textured bezel with screws
/// around a dark phosphor screen, and the graticule colours. Unlike the dial instruments these fill any aspect ratio,
/// so they draw in device-independent pixels and keep text at a readable size instead of scaling it with the plot.
/// </summary>
internal static class PlotArt
{
    public static readonly Color Graticule = Color.FromRgb(0x9C, 0xE8, 0x8A);
    public static readonly Color Amber = Color.FromRgb(0xFF, 0xC0, 0x3A);

    /// <summary>Bezel width for a control of <paramref name="size"/>.</summary>
    public static double BezelWidth(Size size) => Math.Clamp(Math.Min(size.Width, size.Height) * 0.04, 6, 16);

    /// <summary>Label text size for a control of <paramref name="size"/>.</summary>
    public static double TextSize(Size size) => Math.Clamp(Math.Min(size.Width, size.Height * 1.6) / 34, 9, 13);

    /// <summary>Draws the bezel and screen and returns the screen rectangle.</summary>
    public static Rect DrawFrame(DrawingContext dc, Size size, double devicePixelsPerDip)
    {
        double b = BezelWidth(size);
        var outer = new Rect(0.5, 0.5, Math.Max(1, size.Width - 1), Math.Max(1, size.Height - 1));
        double r = b * 1.3;
        var bezel = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(0x3A, 0x3C, 0x3A), 0), new(Color.FromRgb(0x1A, 0x1B, 0x1A), 0.35), new(Color.FromRgb(0x0C, 0x0C, 0x0C), 1),
        }, new Point(0.3, 0), new Point(0.7, 1));
        bezel.Freeze();
        dc.DrawRoundedRectangle(bezel, VintageArt.FrozenPen(VintageArt.Frozen(Colors.Black), 1), outer, r, r);
        dc.DrawRoundedRectangle(VintageArt.GrainBrush(devicePixelsPerDip, 0.45), null, outer, r, r);
        var rim = new LinearGradientBrush(Color.FromArgb(80, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0.5, 0), new Point(0.5, 0.35));
        rim.Freeze();
        var rimRect = outer;
        rimRect.Inflate(-1.2, -1.2);
        if (rimRect.Width > 0 && rimRect.Height > 0) dc.DrawRoundedRectangle(null, new Pen(rim, 1.2), rimRect, r - 1, r - 1);

        var screen = new Rect(b, b, Math.Max(1, size.Width - 2 * b), Math.Max(1, size.Height - 2 * b));
        var recess = screen;
        recess.Inflate(2, 2);
        var recessBrush = new LinearGradientBrush(Color.FromRgb(0x02, 0x02, 0x02), Color.FromRgb(0x26, 0x28, 0x26), 90);
        recessBrush.Freeze();
        dc.DrawRoundedRectangle(recessBrush, null, recess, 5, 5);

        var face = new RadialGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(0x0C, 0x1C, 0x10), 0), new(Color.FromRgb(0x07, 0x11, 0x09), 0.7), new(Color.FromRgb(0x02, 0x05, 0x03), 1),
        })
        { RadiusX = 0.7, RadiusY = 0.75 };
        face.Freeze();
        dc.DrawRoundedRectangle(face, null, screen, 4, 4);

        if (b >= 9)
        {
            double sr = b * 0.3;
            foreach (var p in new[] { new Point(b / 2, b / 2), new Point(size.Width - b / 2, b / 2), new Point(b / 2, size.Height - b / 2), new Point(size.Width - b / 2, size.Height - b / 2) })
                VintageArt.DrawScrew(dc, p, sr, p.X * 0.37 + p.Y * 0.11);
        }
        return screen;
    }

    /// <summary>A faint diagonal glare over the screen.</summary>
    public static void DrawGlass(DrawingContext dc, Rect screen)
    {
        var glare = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(26, 255, 255, 255), 0), new(Color.FromArgb(6, 255, 255, 255), 0.35), new(Color.FromArgb(0, 255, 255, 255), 0.5),
        }, new Point(0, 0), new Point(0.5, 1));
        glare.Freeze();
        dc.DrawRoundedRectangle(glare, null, screen, 4, 4);
    }

    public static Pen GridPen(byte alpha, double thickness = 1) =>
        VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(alpha, Graticule.R, Graticule.G, Graticule.B)), thickness);

    public static Brush LabelBrush(byte alpha = 190) => VintageArt.Frozen(Color.FromArgb(alpha, Graticule.R, Graticule.G, Graticule.B));

    /// <summary>Draws <paramref name="ft"/> with its right edge at <paramref name="right"/> and vertically centred on <paramref name="y"/>.</summary>
    public static void DrawRightAligned(DrawingContext dc, FormattedText ft, double right, double y) =>
        dc.DrawText(ft, new Point(right - ft.Width, y - ft.Height / 2));

    /// <summary>A small rounded readout box with text, used for cursor and handle readouts.</summary>
    public static void DrawReadoutBox(DrawingContext dc, FormattedText ft, Point topLeft, Rect bounds)
    {
        double w = ft.Width + 10, h = ft.Height + 4;
        double x = Math.Clamp(topLeft.X, bounds.Left + 2, Math.Max(bounds.Left + 2, bounds.Right - w - 2));
        double y = Math.Clamp(topLeft.Y, bounds.Top + 2, Math.Max(bounds.Top + 2, bounds.Bottom - h - 2));
        dc.DrawRoundedRectangle(VintageArt.Frozen(Color.FromArgb(215, 0x05, 0x0C, 0x07)), GridPen(110), new Rect(x, y, w, h), 3, 3);
        dc.DrawText(ft, new Point(x + 5, y + 2));
    }
}
