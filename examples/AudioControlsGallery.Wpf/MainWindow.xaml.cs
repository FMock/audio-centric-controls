using System.Windows;
using System.Windows.Controls;
using AudioControlsGallery.ViewModels;
using AudioControlsGallery.Views;

namespace AudioControlsGallery;

public partial class MainWindow : Window
{
    private readonly GalleryViewModel _vm = new();

    /// <summary>
    /// Gallery pages. Add a page here as soon as a new control exists, so every control can be seen working.
    /// Views are created on first visit and then kept, so their state persists while you navigate.
    /// </summary>
    private readonly List<NavItem> _pages = new()
    {
        new("Overview", "Console strip", () => new OverviewView()),
        new("Meters", "VU · Peak · RMS · LED · Phase", () => new MetersView()),
        new("Inputs", "Knobs · Faders · Switches", () => new InputsView()),
        new("Displays", "Scope · Spectrum · EQ · Waveform", () => new DisplaysView()),
        new("Scaling", "Vector rendering at any size", () => new ScalingView()),
    };

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Nav.ItemsSource = _pages;
        Nav.SelectedIndex = 0;
        Closed += (_, _) => _vm.Dispose();

        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "--screenshots");
        if (i >= 0 && i + 1 < args.Length) Loaded += async (_, _) => await CaptureAllPagesAsync(args[i + 1]);
    }

    /// <summary>
    /// Developer aid: <c>AudioControlsGallery.Wpf.exe --screenshots &lt;dir&gt;</c> visits every page, lets the
    /// simulation run, saves a PNG of the window per page at the monitor's DPI, then exits. Pages that scroll also get
    /// a "-full" PNG of their whole content.
    /// </summary>
    private async Task CaptureAllPagesAsync(string directory)
    {
        System.IO.Directory.CreateDirectory(directory);
        await Task.Delay(1500);
        for (int p = 0; p < _pages.Count; p++)
        {
            Nav.SelectedIndex = p;
            await Task.Delay(2500);
            string name = $"{p + 1:00}-{_pages[p].Title.ToLowerInvariant()}";
            var root = (FrameworkElement)Content;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(root);
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(root);
            SavePng(bmp, System.IO.Path.Combine(directory, name + ".png"));

            if (_pages[p].View is UserControl { Content: ScrollViewer { Content: FrameworkElement page } sv } && sv.ExtentHeight > sv.ViewportHeight + 1)
            {
                // A VisualBrush draws the content in its own coordinates, ignoring the current scroll offset.
                double w = page.ActualWidth, h = page.ActualHeight;
                var visual = new System.Windows.Media.DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle((System.Windows.Media.Brush)FindResource("WindowBrush"), null, new Rect(0, 0, w, h));
                    dc.DrawRectangle(new System.Windows.Media.VisualBrush(page) { Stretch = System.Windows.Media.Stretch.None }, null, new Rect(0, 0, w, h));
                }
                var full = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)Math.Ceiling(w * dpi.DpiScaleX), (int)Math.Ceiling(h * dpi.DpiScaleY),
                    dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
                full.Render(visual);
                SavePng(full, System.IO.Path.Combine(directory, name + "-full.png"));
            }
        }
        Close();
    }

    private static void SavePng(System.Windows.Media.Imaging.BitmapSource bmp, string path)
    {
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var fs = System.IO.File.Create(path);
        enc.Save(fs);
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is NavItem item) PageHost.Content = item.View;
    }

    private sealed class NavItem
    {
        private readonly Func<FrameworkElement> _factory;
        private FrameworkElement? _view;

        public NavItem(string title, string subtitle, Func<FrameworkElement> factory)
        {
            Title = title;
            Subtitle = subtitle;
            _factory = factory;
        }

        public string Title { get; }
        public string Subtitle { get; }
        public FrameworkElement View => _view ??= _factory();
    }
}
