using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AudioCentric.Controls.Wpf.Tests;

internal static class RenderHelper
{
    /// <summary>Runs <paramref name="action"/> on a fresh STA thread (WPF requirement) and rethrows failures.</summary>
    public static void RunSta(Action action)
    {
        ExceptionDispatchInfo? error = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        error?.Throw();
    }

    /// <summary>Lays out and renders an element at <paramref name="size"/> DIPs and <paramref name="dpi"/>.</summary>
    public static RenderTargetBitmap Render(FrameworkElement element, Size size, double dpi = 96)
    {
        element.Measure(size);
        element.Arrange(new Rect(size));
        element.UpdateLayout();
        int px = (int)Math.Ceiling(size.Width * dpi / 96), py = (int)Math.Ceiling(size.Height * dpi / 96);
        var bmp = new RenderTargetBitmap(px, py, dpi, dpi, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x16, 0x17, 0x16)), null, new Rect(size));
        bmp.Render(bg);
        bmp.Render(element);
        return bmp;
    }

    /// <summary>Saves a PNG when ACC_RENDER_DIR is set, for visual review of the artwork.</summary>
    public static void MaybeSave(BitmapSource bmp, string name)
    {
        var dir = Environment.GetEnvironmentVariable("ACC_RENDER_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(dir, name + ".png"));
        enc.Save(fs);
    }

    /// <summary>Fraction of pixels that are not the plain background.</summary>
    public static double InkCoverage(BitmapSource bmp)
    {
        int w = bmp.PixelWidth, h = bmp.PixelHeight;
        var px = new byte[w * h * 4];
        bmp.CopyPixels(px, w * 4, 0);
        int n = 0;
        for (int i = 0; i < px.Length; i += 4)
            if (Math.Abs(px[i] - 0x16) + Math.Abs(px[i + 1] - 0x17) + Math.Abs(px[i + 2] - 0x16) > 12) n++;
        return (double)n / (w * h);
    }
}
