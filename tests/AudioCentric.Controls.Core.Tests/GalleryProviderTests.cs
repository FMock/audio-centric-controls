using AudioCentric.Controls.Input;
using AudioCentric.Controls.Plotting;
using AudioControlsGallery.Simulation;

namespace AudioCentric.Controls.Core.Tests;

/// <summary>
/// Checks the gallery's host-side providers against the design reference's verification lists. The providers are not
/// part of the library; they show what a host must supply (calibrated spectra, coefficient-true responses, correlation).
/// </summary>
public class GalleryProviderTests
{
    private const int Rate = 48000;

    private static double[] Sine(int n, double cyclesPerSample, double amp, double phase = 0) =>
        Enumerable.Range(0, n).Select(i => amp * Math.Sin(2 * Math.PI * cyclesPerSample * i + phase)).ToArray();

    private static double[] Spectrum(double[] x)
    {
        int n = x.Length;
        var levels = new double[n / 2 + 1];
        SpectrumAnalysis.AmplitudeSpectrumDb(x, SpectrumAnalysis.Hann(n), new double[n], new double[n], levels);
        return levels;
    }

    // ---------------------------------------------------------------- FFT spectrum

    [Fact]
    public void Fft_MatchesReferenceDft()
    {
        var rng = new Random(3);
        var x = Enumerable.Range(0, 64).Select(_ => rng.NextDouble() * 2 - 1).ToArray();
        var re = (double[])x.Clone();
        var im = new double[64];
        SpectrumAnalysis.Fft(re, im);
        var rr = new double[64];
        var ri = new double[64];
        SpectrumAnalysis.Dft(x, rr, ri);
        for (int k = 0; k < 64; k++)
        {
            Assert.Equal(rr[k], re[k], 9);
            Assert.Equal(ri[k], im[k], 9);
        }
    }

    [Fact]
    public void HannNoiseBandwidth_IsOneAndAHalfBins() =>
        Assert.Equal(1.5, SpectrumAnalysis.NoiseBandwidthBins(SpectrumAnalysis.Hann(4096)), 9);

    [Fact]
    public void BinCentredSine_ReadsItsPeakLevel()
    {
        var l = Spectrum(Sine(4096, 100.0 / 4096, 0.5));
        Assert.Equal(-6.0206, l[100], 4);
        Assert.Equal(-12.0412, l[99], 4); // Hann main lobe: neighbours at half amplitude
        Assert.True(l[300] < -200);
    }

    [Fact]
    public void DcAndNyquist_UseSingleSidedScaling()
    {
        var dc = Spectrum(Enumerable.Repeat(0.25, 1024).ToArray());
        Assert.Equal(-12.0412, dc[0], 4);
        var nyq = Spectrum(Enumerable.Range(0, 1024).Select(i => i % 2 == 0 ? 0.5 : -0.5).ToArray());
        Assert.Equal(-6.0206, nyq[512], 4);
    }

    [Fact]
    public void TwoTones_Leakage_AndSilence()
    {
        var x = Sine(4096, 50.0 / 4096, 1.0).Zip(Sine(4096, 400.0 / 4096, 0.1), (a, b) => a + b).ToArray();
        var l = Spectrum(x);
        Assert.Equal(0, l[50], 3);
        Assert.Equal(-20, l[400], 3);

        // Halfway between bins the Hann window scallops by about 1.42 dB.
        var off = Spectrum(Sine(4096, 100.5 / 4096, 1.0));
        Assert.InRange(Math.Max(off[100], off[101]), -1.5, -1.3);

        Assert.All(Spectrum(new double[512]), v => Assert.Equal(double.NegativeInfinity, v));
    }

    [Fact]
    public void SpectrumSource_PublishesCalibratedFramesWithMetadata()
    {
        var src = new SimulatedSpectrumSource(Rate) { FftSize = 4096 };
        var block = new float[256];
        long n = 0;
        for (int b = 0; b < 40; b++)
        {
            for (int i = 0; i < block.Length; i++, n++) block[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 1500.0 * n / Rate));
            src.Process(block, block);
        }
        var s = src.Latest;
        Assert.NotNull(s);
        Assert.Equal(4096, s!.FftSize);
        Assert.Equal(2049, s.LevelsDb.Length);
        Assert.Equal(1.5, s.NoiseBandwidthBins, 9);
        Assert.Equal(Rate / 4096.0, s.BinSpacing, 9);
        Assert.Equal(128, Array.IndexOf(s.LevelsDb, s.LevelsDb.Max())); // 1500 Hz = bin 128
        Assert.Equal(-6.02, s.LevelsDb[128], 1);
    }

    // ---------------------------------------------------------------- biquads and response

    private static double Db(Biquad c, double hz)
    {
        var (re, im) = c.Response(hz, Rate);
        return 10 * Math.Log10(re * re + im * im);
    }

    [Fact]
    public void ZeroGainPeak_IsUnity()
    {
        var c = Biquad.Design(new EqBand(EqBandType.Peaking, 1000, 0, 1), Rate);
        foreach (var f in new[] { 20.0, 1000, 23999 }) Assert.Equal(0, Db(c, f), 9);
    }

    [Fact]
    public void Peak_HighPass_AndShelves_HitTheirDesignPoints()
    {
        var peak = Biquad.Design(new EqBand(EqBandType.Peaking, 1000, 6, 1), Rate);
        Assert.Equal(6, Db(peak, 1000), 6);
        Assert.InRange(Db(peak, 20), -0.05, 0.05);

        var hp = Biquad.Design(new EqBand(EqBandType.HighPass, 1000, 0, Math.Sqrt(0.5)), Rate);
        Assert.Equal(-3.0103, Db(hp, 1000), 3);
        Assert.True(Db(hp, 100) < -35);          // 12 dB/octave below the cutoff
        Assert.InRange(Db(hp, 10000), -0.05, 0.05);

        var lp = Biquad.Design(new EqBand(EqBandType.LowPass, 1000, 0, Math.Sqrt(0.5)), Rate);
        Assert.True(Db(lp, 23990) < -60);          // tends to a zero at Nyquist

        var ls = Biquad.Design(new EqBand(EqBandType.LowShelf, 200, 6, 0.707), Rate);
        Assert.InRange(Db(ls, 20), 5.8, 6.05);
        Assert.Equal(3, Db(ls, 200), 1);          // shelf midpoint is half the gain in dB
        Assert.InRange(Db(ls, 15000), -0.05, 0.05);

        var disabled = Biquad.Design(new EqBand(EqBandType.Peaking, 1000, 12, 1, IsEnabled: false), Rate);
        Assert.Equal(Biquad.Identity, disabled);
    }

    [Fact]
    public void Cascade_AddsDecibels_AndTheoreticalResponseMatchesCoefficients()
    {
        var eq = new SimulatedFrequencyResponseSource(Rate);
        var coeffs = eq.Coefficients;
        foreach (var f in new[] { 30.0, 120, 420, 2600, 9000, 18000 })
        {
            var (re, im) = SimulatedFrequencyResponseSource.CascadeResponse(coeffs, f, Rate);
            Assert.Equal(coeffs.Sum(c => Db(c, f)), 10 * Math.Log10(re * re + im * im), 9);
        }

        var r = eq.ComputeTheoretical();
        Assert.False(r.IsMeasured);
        foreach (var band in eq.Bands)
        {
            int i = Array.IndexOf(r.Frequencies, band.FrequencyHz);
            Assert.True(i >= 0, $"{band.FrequencyHz} Hz is sampled exactly");
            var (re, im) = SimulatedFrequencyResponseSource.CascadeResponse(coeffs, band.FrequencyHz, Rate);
            Assert.Equal(10 * Math.Log10(re * re + im * im), r.GainDb[i], 9);
        }
        Assert.Equal(20000, r.Frequencies[^1], 9);
    }

    [Fact]
    public void Requests_AreConstrainedByTheHost()
    {
        var eq = new SimulatedFrequencyResponseSource(Rate);
        var applied = eq.Apply(new EqBandChangeRequest(3, eq.Bands[3] with { GainDb = 40, FrequencyHz = 5 }));
        Assert.Equal(18, applied.GainDb);
        Assert.Equal(20, applied.FrequencyHz);
        var hp = eq.Apply(new EqBandChangeRequest(0, eq.Bands[0] with { GainDb = 6 }));
        Assert.Equal(0, hp.GainDb);                // pass filters have no gain
        Assert.Same(eq.Bands[3], eq.Bands[3]);
        Assert.Equal(applied, eq.Bands[3]);
    }

    [Fact]
    public void MeasuredResponse_AgreesWithTheory()
    {
        var eq = new SimulatedFrequencyResponseSource(Rate);
        var theory = eq.ComputeTheoretical();
        var m = eq.Measure();
        Assert.True(m.IsMeasured);
        var errors = new List<double>();
        for (int i = 0; i < m.Frequencies.Length; i++)
        {
            if (m.Frequencies[i] < 30 || double.IsNaN(m.GainDb[i])) continue;
            errors.Add(Math.Abs(m.GainDb[i] - ResponseMath.Interpolate(theory.Frequencies, theory.GainDb, m.Frequencies[i])));
        }
        Assert.True(errors.Count > 1000);
        errors.Sort();
        Assert.True(errors[errors.Count / 2] < 0.3, $"median error {errors[errors.Count / 2]:0.00} dB");
        Assert.True(errors[(int)(errors.Count * 0.98)] < 1.5);
    }

    [Fact]
    public void Measurement_MasksBinsWithoutEnoughSignal()
    {
        var eq = new SimulatedFrequencyResponseSource(Rate);
        eq.SetBands(new[] { new EqBand(EqBandType.LowPass, 300, 0, 0.707) });
        var m = eq.Measure(noiseDb: -40);
        bool Masked(double lo, double hi) => m.Frequencies.Select((f, i) => (f, i)).Where(p => p.f >= lo && p.f <= hi).All(p => double.IsNaN(m.GainDb[p.i]));
        bool AllValid(double lo, double hi) => m.Frequencies.Select((f, i) => (f, i)).Where(p => p.f >= lo && p.f <= hi).All(p => !double.IsNaN(m.GainDb[p.i]));
        Assert.True(AllValid(20, 200));
        Assert.True(Masked(12000, 20000));
    }

    // ---------------------------------------------------------------- correlation

    private static double Correlate(Func<int, double> left, Func<int, double> right, int n = Rate)
    {
        var c = new SimulatedCorrelationSource(Rate);
        var l = new float[n];
        var r = new float[n];
        for (int i = 0; i < n; i++) { l[i] = (float)left(i); r[i] = (float)right(i); }
        c.Process(l, r);
        return c.Value;
    }

    [Fact]
    public void Correlation_FollowsTheReferenceCases()
    {
        double S(int i) => 0.5 * Math.Sin(2 * Math.PI * 1000.0 * i / Rate);
        double C(int i) => 0.5 * Math.Cos(2 * Math.PI * 1000.0 * i / Rate);
        Assert.Equal(1, Correlate(S, S), 6);                 // identical
        Assert.Equal(-1, Correlate(S, i => -S(i)), 6);       // inverted
        Assert.Equal(1, Correlate(S, i => 0.2 * S(i)), 6);   // unequal gains
        Assert.InRange(Correlate(S, C), -0.02, 0.02);        // quadrature
        var a = new Random(1);
        var b = new Random(2);
        Assert.InRange(Correlate(_ => a.NextDouble() - 0.5, _ => b.NextDouble() - 0.5), -0.1, 0.1); // independent noise
        Assert.True(double.IsNaN(Correlate(S, _ => 0)));     // one silent channel: unavailable, not 0
        Assert.True(double.IsNaN(Correlate(_ => 0, _ => 0)));
    }
}
