using AudioCentric.Controls.Input;
using AudioCentric.Controls.Plotting;
using AudioCentric.Controls.Scales;

namespace AudioCentric.Controls.Core.Tests;

public class LedAndPlottingTests
{
    [Fact]
    public void Led_ExactThresholdsLightSegments()
    {
        var s = ScalePresets.LedPeak; // -60 -50 -40 -30 -24 -18 -12 -9 -6 -3 0
        Assert.Equal(0, s.LitCount(double.NegativeInfinity));
        Assert.Equal(0, s.LitCount(-60.0001));
        Assert.Equal(1, s.LitCount(-60));
        Assert.Equal(7, s.LitCount(-12));
        Assert.Equal(6, s.LitCount(-12.0001));
        Assert.Equal(11, s.LitCount(0));
        Assert.Equal(11, s.LitCount(5));
        Assert.Equal(6, s.DotIndex(-12));
    }

    [Fact]
    public void Led_ColorsByZone()
    {
        var s = ScalePresets.LedPeak;
        Assert.Equal(ZoneKind.Normal, s.ZoneOf(5));   // -18
        Assert.Equal(ZoneKind.Warning, s.ZoneOf(6));  // -12
        Assert.Equal(ZoneKind.Danger, s.ZoneOf(9));   // -3
    }

    [Fact]
    public void Bucketer_PreservesSingleImpulseAtBroadZoom()
    {
        var x = new float[48000];
        x[12345] = 0.9f;
        var lo = new float[100];
        var hi = new float[100];
        MinMaxBucketer.Reduce(x, 0, x.Length, lo, hi);
        Assert.Equal(0.9f, hi.Max());
        Assert.Equal(1, hi.Count(v => v > 0));
    }

    [Fact]
    public void Bucketer_NegativeOnlyBucketAndPartialLastBucket()
    {
        var x = new float[] { -0.5f, -0.2f, -0.9f, 0.1f, 0.3f, -0.4f, 0.7f };
        var lo = new float[3];
        var hi = new float[3];
        MinMaxBucketer.Reduce(x, 0, x.Length, lo, hi);
        // buckets: [0,2) [2,4) [4,7)
        Assert.Equal(-0.5f, lo[0]); Assert.Equal(-0.2f, hi[0]);
        Assert.Equal(-0.9f, lo[1]); Assert.Equal(0.1f, hi[1]);
        Assert.Equal(-0.4f, lo[2]); Assert.Equal(0.7f, hi[2]);
    }

    [Fact]
    public void Bucketer_NeverReducesEmptyBuckets()
    {
        var x = new float[] { 0.25f };
        var lo = new float[4];
        var hi = new float[4];
        MinMaxBucketer.Reduce(x, 0, 1, lo, hi);
        Assert.Equal(1, hi.Count(v => !float.IsNaN(v)));
        Assert.Equal(0.25f, hi.First(v => !float.IsNaN(v)));
        Assert.True(MinMaxBucketer.ShouldDrawSamples(1, 4));
    }

    [Fact]
    public void Parameter_StepDetentAndReset()
    {
        var p = new ParameterModel(-24, 24, 0) { Step = 0.5, Detents = new[] { 0.0 } };
        p.Normalized = 0.505;     // within detent width of centre
        Assert.Equal(0, p.Value);
        p.Value = 7.3;
        Assert.Equal(7.5, p.Value);
        p.Value = 100;
        Assert.Equal(24, p.Value);
        p.ResetToDefault();
        Assert.Equal(0, p.Value);
    }

    [Fact]
    public void Toggle_RequestedVersusActual()
    {
        var t = new ToggleStateModel { AutoAcknowledge = false };
        t.Request(true);
        Assert.Equal(ToggleStatus.Pending, t.Status);
        Assert.False(t.Actual);
        t.Acknowledge(true);
        Assert.Equal(ToggleStatus.Ready, t.Status);
        Assert.True(t.Actual);
        t.Request(false);
        t.Acknowledge(true);
        Assert.Equal(ToggleStatus.Fault, t.Status);
    }
}
