using System.Windows;
using System.Windows.Media;

namespace PackageConsumer;

public partial class MainWindow : Window
{
    private readonly DemoHost _host = new();
    private TimeSpan _last = TimeSpan.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _host;
        // Pull the host's newest values once per rendered frame, on the UI thread.
        CompositionTarget.Rendering += OnFrame;
        Closed += (_, _) => CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _last) return;
        double dt = _last == TimeSpan.MinValue ? 0 : (now - _last).TotalSeconds;
        _last = now;
        _host.Advance(Math.Min(dt, 0.25));
    }

    // The button's Command resets the VU lamp through AnalogMeter.ResetOverloadCommand; the peak meter is reset directly.
    private void OnResetPeak(object sender, RoutedEventArgs e) => Peak.ResetOverload();
}
