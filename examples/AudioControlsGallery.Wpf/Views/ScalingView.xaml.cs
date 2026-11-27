using System.ComponentModel;
using System.Windows.Controls;
using AudioCentric.Controls.Measurements;
using AudioCentric.Controls.Wpf.Controls;

namespace AudioControlsGallery.Views;

public partial class ScalingView : UserControl
{
    public ScalingView()
    {
        InitializeComponent();
        DependencyPropertyDescriptor.FromProperty(ConsoleFader.ValueDbProperty, typeof(ConsoleFader))
            .AddValueChanged(Zoom, (_, _) => ApplyZoom());
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        // The fader's dB value doubles as a zoom law: 0 dB = 1×, +10 dB ≈ 3.16×; the bottom of travel clamps at 0.25×.
        double k = Math.Max(0.25, Decibels.ToAmplitude(Zoom.ValueDb));
        ZoomTransform.ScaleX = ZoomTransform.ScaleY = k;
        ZoomText.Text = $"{k:0.00}×";
    }
}
