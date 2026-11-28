using AudioCentric.Controls.Input;
using AudioCentric.Controls.Measurements;
using AudioCentric.Controls.Plotting;

namespace AudioControlsGallery.Simulation;

/// <summary>
/// The host's equalizer: it owns the band parameters and their biquad coefficients, constrains change requests from the
/// graph, and computes the response two ways. The theoretical response comes from the coefficients,
/// H = Π (b0 + b1 z⁻¹ + b2 z⁻²)/(1 + a1 z⁻¹ + a2 z⁻²). The measured response drives noise through the same filters and
/// estimates H₁ = S_yx / S_xx with Welch averaging, masking bins whose coherence is low.
/// </summary>
public sealed class SimulatedFrequencyResponseSource
{
    public const double MinFrequency = 20, MaxFrequency = 20000, MaxGainDb = 18;

    private EqBand[] _bands;

    public SimulatedFrequencyResponseSource(int sampleRate)
    {
        SampleRate = sampleRate;
        _bands = DefaultBands.ToArray();
        Coefficients = Design(_bands);
    }

    public int SampleRate { get; }

    public static IReadOnlyList<EqBand> DefaultBands { get; } = new[]
    {
        new EqBand(EqBandType.HighPass, 40, 0, 0.707),
        new EqBand(EqBandType.LowShelf, 120, 3, 0.707),
        new EqBand(EqBandType.Peaking, 420, -2.5, 1.2),
        new EqBand(EqBandType.Peaking, 2600, 4, 1.0),
        new EqBand(EqBandType.HighShelf, 9000, 2, 0.707),
    };

    public IReadOnlyList<EqBand> Bands => _bands;

    /// <summary>Coefficients of the enabled cascade, in band order. Disabled bands are identity sections.</summary>
    public Biquad[] Coefficients { get; private set; }

    /// <summary>Applies a change request after constraining it, and returns what was actually applied.</summary>
    public EqBand Apply(EqBandChangeRequest request)
    {
        if (request.Index < 0 || request.Index >= _bands.Length) throw new ArgumentOutOfRangeException(nameof(request));
        var current = _bands[request.Index];
        var r = request.Requested;
        var applied = current with
        {
            FrequencyHz = Math.Clamp(r.FrequencyHz, MinFrequency, Math.Min(MaxFrequency, SampleRate * 0.45)),
            GainDb = current.HasGain ? Math.Round(Math.Clamp(r.GainDb, -MaxGainDb, MaxGainDb), 2) : 0,
            Q = Math.Clamp(r.Q, current.HasGain ? 0.3 : 0.5, current.HasGain ? 12 : 4),
            IsEnabled = r.IsEnabled,
        };
        var bands = (EqBand[])_bands.Clone();
        bands[request.Index] = applied;
        SetBands(bands);
        return applied;
    }

    public void SetBands(IReadOnlyList<EqBand> bands)
    {
        _bands = bands.ToArray();
        Coefficients = Design(_bands);
    }

    private Biquad[] Design(EqBand[] bands) => bands.Select(b => Biquad.Design(b, SampleRate)).ToArray();

    /// <summary>
    /// Response computed from the coefficients on a log grid up to min(20 kHz, Nyquist), with each band's centre
    /// frequency inserted so peaks and cutoffs are sampled exactly.
    /// </summary>
    public ResponseSnapshot ComputeTheoretical(int points = 600)
    {
        double top = Math.Min(MaxFrequency, SampleRate / 2.0);
        var grid = ResponseMath.LogSpacedFrequencies(MinFrequency, top, points).ToList();
        foreach (var b in _bands) if (b.FrequencyHz > MinFrequency && b.FrequencyHz < top) grid.Add(b.FrequencyHz);
        grid.Sort();
        var freqs = grid.Distinct().ToArray();
        var gain = new double[freqs.Length];
        var phase = new double[freqs.Length];
        var coeffs = Coefficients;
        for (int i = 0; i < freqs.Length; i++)
        {
            var (re, im) = CascadeResponse(coeffs, freqs[i], SampleRate);
            gain[i] = 10 * Math.Log10(re * re + im * im);
            phase[i] = Math.Atan2(im, re) * 180 / Math.PI;
        }
        return new ResponseSnapshot(freqs, gain, phase) { SampleRate = SampleRate };
    }

    /// <summary>The response of a bypassed processor: 0 dB and 0° everywhere.</summary>
    public static ResponseSnapshot Flat(int sampleRate)
    {
        var f = ResponseMath.LogSpacedFrequencies(MinFrequency, Math.Min(MaxFrequency, sampleRate / 2.0), 64);
        return new ResponseSnapshot(f, new double[f.Length], new double[f.Length]) { SampleRate = sampleRate };
    }

    public static (double Re, double Im) CascadeResponse(IReadOnlyList<Biquad> coeffs, double hz, double sampleRate)
    {
        double re = 1, im = 0;
        foreach (var c in coeffs)
        {
            var (r, i) = c.Response(hz, sampleRate);
            (re, im) = (re * r - im * i, re * i + im * r);
        }
        return (re, im);
    }

    /// <summary>
    /// Measures the cascade like an analyser would: white-noise excitation x, captured output y (with optional added
    /// measurement noise), Hann-windowed frames at 50 % overlap, H₁ = S_yx / S_xx. Bins with coherence below
    /// <paramref name="minCoherence"/> are masked (NaN). Deterministic for a given seed.
    /// </summary>
    public ResponseSnapshot Measure(int fftSize = 8192, int frames = 32, double noiseDb = -60, double minCoherence = 0.6, int seed = 1)
    {
        var coeffs = Coefficients;
        var rng = new Random(seed);
        int hop = fftSize / 2;
        int settle = fftSize; // let the filters settle before the first frame
        int length = settle + hop * (frames - 1) + fftSize;
        var x = new double[length];
        var y = new double[length];
        var states = new BiquadState[coeffs.Length];
        double noise = Math.Pow(10, noiseDb / 20);
        for (int n = 0; n < length; n++)
        {
            double v = (rng.NextDouble() * 2 - 1) * 0.5;
            x[n] = v;
            for (int k = 0; k < coeffs.Length; k++) v = states[k].Process(coeffs[k], v);
            y[n] = v + (rng.NextDouble() * 2 - 1) * noise;
        }

        var w = SpectrumAnalysis.Hann(fftSize);
        int bins = fftSize / 2 + 1;
        var sxx = new double[bins];
        var syy = new double[bins];
        var syxRe = new double[bins];
        var syxIm = new double[bins];
        var xr = new double[fftSize]; var xi = new double[fftSize];
        var yr = new double[fftSize]; var yi = new double[fftSize];
        for (int f = 0; f < frames; f++)
        {
            int o = settle + f * hop;
            for (int i = 0; i < fftSize; i++)
            {
                xr[i] = x[o + i] * w[i]; xi[i] = 0;
                yr[i] = y[o + i] * w[i]; yi[i] = 0;
            }
            SpectrumAnalysis.Fft(xr, xi);
            SpectrumAnalysis.Fft(yr, yi);
            for (int k = 0; k < bins; k++)
            {
                sxx[k] += xr[k] * xr[k] + xi[k] * xi[k];
                syy[k] += yr[k] * yr[k] + yi[k] * yi[k];
                // Y · conj(X)
                syxRe[k] += yr[k] * xr[k] + yi[k] * xi[k];
                syxIm[k] += yi[k] * xr[k] - yr[k] * xi[k];
            }
        }

        double top = Math.Min(MaxFrequency, SampleRate / 2.0);
        var freqs = new List<double>();
        var gain = new List<double>();
        var phase = new List<double>();
        for (int k = 1; k < bins; k++)
        {
            double hz = (double)k * SampleRate / fftSize;
            if (hz < MinFrequency) continue;
            if (hz > top) break;
            double hr = syxRe[k] / sxx[k], hi = syxIm[k] / sxx[k];
            double coherence = (syxRe[k] * syxRe[k] + syxIm[k] * syxIm[k]) / (sxx[k] * syy[k]);
            bool ok = sxx[k] > 0 && syy[k] > 0 && coherence >= minCoherence;
            freqs.Add(hz);
            gain.Add(ok ? 10 * Math.Log10(hr * hr + hi * hi) : double.NaN);
            phase.Add(ok ? Math.Atan2(hi, hr) * 180 / Math.PI : double.NaN);
        }
        return new ResponseSnapshot(freqs.ToArray(), gain.ToArray(), phase.ToArray()) { IsMeasured = true, SampleRate = SampleRate };
    }
}
