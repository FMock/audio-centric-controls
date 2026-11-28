using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PackageConsumer;

/// <summary>
/// Normal start shows the window. <c>--smoke &lt;file.png&gt;</c> is used by scripts/verify-packages.ps1: it shows the window,
/// lets it animate, saves a screenshot, checks that every control type from the package is on screen, and exits with
/// 0 (success), 1 (missing controls) or 2 (an exception).
/// </summary>
public partial class App : Application
{
    private static readonly string[] ExpectedControls =
    {
        "AnalogMeter", "LedBarMeter", "RotaryKnob", "ConsoleFader", "IlluminatedToggle",
        "CorrelationMeter", "SpectrumAnalyzer", "FrequencyResponseGraph",
    };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        int i = Array.IndexOf(e.Args, "--smoke");
        if (i < 0 || i + 1 >= e.Args.Length)
        {
            window.Show();
            return;
        }

        string png = Path.GetFullPath(e.Args[i + 1]);
        DispatcherUnhandledException += (_, ex) =>
        {
            File.WriteAllText(Path.ChangeExtension(png, ".txt"), ex.Exception.ToString());
            ex.Handled = true;
            Shutdown(2);
        };
        window.Loaded += async (_, _) =>
        {
            await Task.Delay(1500);
            var found = new HashSet<string>();
            Walk(window, v => { if (v.GetType().Namespace == "AudioCentric.Controls.Wpf.Controls") found.Add(v.GetType().Name); });
            var missing = ExpectedControls.Where(c => !found.Contains(c)).ToArray();

            // A VisualBrush draws the content in its own coordinates (RenderTargetBitmap would apply its margin offset).
            var root = (FrameworkElement)window.Content;
            var dpi = VisualTreeHelper.GetDpi(root);
            double w = root.ActualWidth, h = root.ActualHeight;
            var shot = new DrawingVisual();
            using (var dc = shot.RenderOpen())
            {
                dc.DrawRectangle(window.Background, null, new Rect(0, 0, w, h));
                dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None }, null, new Rect(0, 0, w, h));
            }
            var bmp = new RenderTargetBitmap((int)Math.Ceiling(w * dpi.DpiScaleX), (int)Math.Ceiling(h * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bmp.Render(shot);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = File.Create(png)) enc.Save(fs);

            var assembly = typeof(AudioCentric.Controls.Wpf.Controls.AnalogMeter).Assembly;
            File.WriteAllText(Path.ChangeExtension(png, ".txt"),
                $"Loaded {assembly.FullName} from {assembly.Location}\nControls on screen: {string.Join(", ", found.OrderBy(x => x))}\nMissing: {string.Join(", ", missing)}\n");
            Shutdown(missing.Length == 0 ? 0 : 1);
        };
        window.Show();
    }

    private static void Walk(DependencyObject node, Action<DependencyObject> visit)
    {
        visit(node);
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Walk(VisualTreeHelper.GetChild(node, i), visit);
    }
}
