using AudioCentric.Controls.Plotting;
using AudioCentric.Controls.Scales;

namespace AudioCentric.Controls.Core.Tests;

public class SpectrumAndResponseTests
{
    // ---------------------------------------------------------------- frequency axis

    [Fact]
    public void LogAxis_PlacesDecadesEvenly_AndHasNoPlaceForDc()
    {
        var axis = new FrequencyAxis(20, 20000);
        Assert.Equal(1.0 / 3, axis.ToNormalized(200), 9);
        Assert.Equal(2.0 / 3, axis.ToNormalized(2000), 9);
        Assert.Equal(632.456, axis.FromNormalized(0.5), 3);
        Assert.Equal(double.NegativeInfinity, axis.ToNormalized(0));
    }

    [Fact]
    public void LogAxis_LabelsOneTwoFivePerDecade()
    {
        var ticks = new FrequencyAxis(20, 20000).Ticks();
        Assert.Equal(new[] { "20", "50", "100", "200", "500", "1k", "2k", "5k", "10k", "20k" },
            ticks.Where(t => t.IsMajor).Select(t => t.Label));
        Assert.Contains(ticks, t => !t.IsMajor && Math.Abs(t.Value - 30) < 1e-9);
        Assert.Equal("1.5k", FrequencyAxis.FormatHz(1500));
        Assert.Equal("1.02 kHz", FrequencyAxis.FormatHzWithUnit(1020));
        Assert.Equal("48.0 Hz", FrequencyAxis.FormatHzWithUnit(48));
    }

    [Fact]
    public void LinearTicks_UseNiceSteps()
    {
        Assert.Equal(10, AxisTicks.NiceStep(6));
        Assert.Equal(5, AxisTicks.NiceStep(2.1));
        Assert.Equal(0.2, AxisTicks.NiceStep(0.15), 12);
        var majors = AxisTicks.Linear(-90, 0, 10).Where(t => t.IsMajor).Select(t => t.Value).ToArray();
        Assert.Equal(new[] { -90.0, -80, -70, -60, -50, -40, -30, -20, -10, 0 }, majors);
    }

    // ---------------------------------------------------------------- bands and reduction

    [Fact]
    public void ThirdOctaves_Cover20HzTo20kHzWith31ContiguousBands()
    {
        var bands = FractionalOctaveBands.Create(3);
        Assert.Equal(31, bands.Length);
        Assert.Contains(bands, b => Math.Abs(b.Center - 1000) < 1e-9);
        Assert.InRange(bands[0].Center, 19.6, 19.8);
        Assert.InRange(bands[^1].Center, 20100, 20200);
        for (int i = 1; i < bands.Length; i++) Assert.Equal(bands[i - 1].High, bands[i].Low, 6);
        Assert.Equal(10, FractionalOctaveBands.Create(1).Length);
    }

    private static (double[] F, double[] L) ToneSpectrum(int n, int rate, int bin, double db)
    {
        var f = Enumerable.Range(0, n / 2 + 1).Select(k => (double)k * rate / n).ToArray();
        var l = Enumerable.Repeat(-120.0, f.Length).ToArray();
        l[0] = 0; // a loud DC bin must not leak onto the log axis
        l[bin] = db;
        l[bin - 1] = l[bin + 1] = db - 6.0206; // Hann main lobe of a bin-centred tone
        return (f, l);
    }

    [Fact]
    public void Columns_KeepPeaks_InterpolateSparseBins_AndIgnoreDc()
    {
        var (f, l) = ToneSpectrum(4096, 48000, 100, -6);
        var axis = new FrequencyAxis(20, 20000);
        var cols = new double[500];
        SpectrumReducer.ToColumns(f, l, axis, cols);
        Assert.Equal(-6, cols.Max(), 9);
        int peakCol = Array.IndexOf(cols, cols.Max());
        Assert.InRange(axis.FromNormalized((peakCol + 0.5) / 500), 1150, 1195);
        Assert.Equal(-120, cols[0], 9);            // 20 Hz lies between bins 1 and 2: interpolated, DC ignored
        Assert.DoesNotContain(cols, double.IsNaN);
    }

    [Fact]
    public void Bands_MaximumKeepsToneLevel_PowerSumIsCalibratedByNoiseBandwidth()
    {
        var (f, l) = ToneSpectrum(4096, 48000, 100, -10);
        for (int i = 1; i < l.Length; i++) if (l[i] == -120) l[i] = double.NegativeInfinity;
        var axis = new FrequencyAxis(20, 20000);
        var bands = FractionalOctaveBands.Create(3);
        var max = new double[bands.Length];
        var power = new double[bands.Length];
        SpectrumReducer.ToBands(f, l, bands, BandAggregation.Maximum, 1.5, axis, max);
        SpectrumReducer.ToBands(f, l, bands, BandAggregation.PowerSum, 1.5, axis, power);
        int b = Array.FindIndex(bands, x => 1171.875 >= x.Low && 1171.875 < x.High);
        Assert.Equal(-10, max[b], 9);
        Assert.Equal(-10, power[b], 6); // (1 + ¼ + ¼) / 1.5 = 1
        Assert.Equal(double.NegativeInfinity, power[b - 2]);
    }

    // ---------------------------------------------------------------- response helpers

    [Fact]
    public void Phase_WrapsAndUnwraps()
    {
        Assert.Equal(-170, ResponseMath.WrapDegrees(190), 9);
        Assert.Equal(180, ResponseMath.WrapDegrees(-180), 9);
        Assert.Equal(new[] { 170.0, 190, 210, 350 }, ResponseMath.UnwrapDegrees(new[] { 170.0, -170, -150, -10 }));
    }

    [Fact]
    public void Crossings_AndInterpolation_WorkInLogFrequency()
    {
        // First-order low-pass at 1 kHz: −3.01 dB at the cutoff.
        var f = ResponseMath.LogSpacedFrequencies(20, 20000, 301);
        var g = f.Select(x => -10 * Math.Log10(1 + x * x / 1e6)).ToArray();
        var c = ResponseMath.FindCrossings(f, g, -3.0103);
        Assert.Single(c);
        Assert.Equal(1000, c[0], 0);
        Assert.Equal(-3.0103, ResponseMath.Interpolate(f, g, 1000), 2);
        Assert.True(double.IsNaN(ResponseMath.Interpolate(f, g, 10)));
    }

    [Fact]
    public void CorrelationScale_HasGoodNeutralWarningAndDangerZones()
    {
        var s = ScalePresets.Correlation;
        Assert.Equal(0.5, s.ToNormalized(0), 12);
        Assert.Equal(ZoneKind.Good, s.ZoneAt(0.5));
        Assert.Equal(ZoneKind.Normal, s.ZoneAt(0));
        Assert.Equal(ZoneKind.Warning, s.ZoneAt(-0.3));
        Assert.Equal(ZoneKind.Danger, s.ZoneAt(-0.8));
        Assert.Equal(ZoneKind.Normal, ScalePresets.Vu.ZoneAt(-10)); // unchanged for scales without a Good zone
    }
}
