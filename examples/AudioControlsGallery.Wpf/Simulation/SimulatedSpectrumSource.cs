using AudioCentric.Controls.Measurements;

namespace AudioControlsGallery.Simulation;

/// <summary>
/// The host's FFT analysis provider. On the audio thread it collects the mid signal (L+R)/2, and every hop
/// (N × (1 − overlap) samples) it analyses the newest N samples with a Hann window. The newest
/// <see cref="SpectrumSnapshot"/> is published with a lock-free swap for the UI to read at frame rate.
/// </summary>
public sealed class SimulatedSpectrumSource
{
    public const int MaxFftSize = 16384;

    private readonly int _sampleRate;
    private readonly float[] _ring = new float[MaxFftSize];
    private int _write;
    private long _written;
    private int _sinceFrame;
    private volatile int _requestedSize = 4096;
    private int _size;
    private double[] _window = Array.Empty<double>(), _frame = Array.Empty<double>(), _re = Array.Empty<double>(), _im = Array.Empty<double>();
    private double[] _freqs = Array.Empty<double>();
    private double _enbw;
    private SpectrumSnapshot? _latest;

    public SimulatedSpectrumSource(int sampleRate) => _sampleRate = sampleRate;

    /// <summary>FFT length N, a power of two from 256 to 16384. Takes effect on the next frame.</summary>
    public int FftSize
    {
        get => _requestedSize;
        set
        {
            if (value < 256 || value > MaxFftSize || (value & (value - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(value));
            _requestedSize = value;
        }
    }

    /// <summary>Frame overlap, 0…0.9. Default 0.75 (a new frame every N/4 samples).</summary>
    public double Overlap { get; set; } = 0.75;

    /// <summary>Newest analysed frame, or null before the first.</summary>
    public SpectrumSnapshot? Latest => Volatile.Read(ref _latest);

    public void Process(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        for (int i = 0; i < left.Length; i++)
        {
            _ring[_write] = 0.5f * (left[i] + right[i]);
            _write = (_write + 1) % MaxFftSize;
        }
        _written += left.Length;
        _sinceFrame += left.Length;

        int n = _requestedSize;
        if (n != _size) Configure(n);
        int hop = Math.Max(1, (int)(n * (1 - Math.Clamp(Overlap, 0, 0.9))));
        if (_sinceFrame < hop || _written < n) return;
        _sinceFrame = 0; // analyse only the newest frame; there is no point catching up on stale ones

        for (int i = 0; i < n; i++) _frame[i] = _ring[(_write - n + i + MaxFftSize) % MaxFftSize];
        var levels = new double[n / 2 + 1]; // a fresh array per snapshot, because the UI reads it on another thread
        SpectrumAnalysis.AmplitudeSpectrumDb(_frame, _window, _re, _im, levels);
        Volatile.Write(ref _latest, new SpectrumSnapshot(_sampleRate, 0, _freqs, levels)
        {
            FftSize = n,
            Window = "Hann",
            NoiseBandwidthBins = _enbw,
            StartSample = _written - n,
        });
    }

    private void Configure(int n)
    {
        _size = n;
        _window = SpectrumAnalysis.Hann(n);
        _enbw = SpectrumAnalysis.NoiseBandwidthBins(_window);
        _frame = new double[n];
        _re = new double[n];
        _im = new double[n];
        _freqs = SpectrumAnalysis.BinFrequencies(n, _sampleRate);
        _sinceFrame = int.MaxValue / 2; // analyse at once with the new size
    }
}
