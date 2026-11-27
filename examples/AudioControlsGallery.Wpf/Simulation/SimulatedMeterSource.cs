using AudioCentric.Controls.Measurements;

namespace AudioControlsGallery.Simulation;

/// <summary>
/// Stand-in for a host's analysis provider for one channel. It runs on the engine thread and computes calibrated
/// measurements from samples: VU (second-order mechanical model, −18 dBFS sine = 0 VU), sample peak
/// (aggregated across blocks until the UI reads it, so short peaks are never lost), 300 ms sliding RMS, and a
/// full-scale overload flag.
/// <para>
/// These are the host-side duties in the design reference. The controls themselves never touch samples.
/// The audio thread hands results to the UI without locks: latest values are written atomically, and peak and
/// overload are accumulated and consumed with interlocked operations.
/// </para>
/// </summary>
public sealed class SimulatedMeterSource
{
    // VU reference: a −18 dBFS sample-peak sine reads 0 VU. Its rectified mean is 2A/π.
    private static readonly double VuReference = 2.0 * Decibels.ToAmplitude(-18) / Math.PI;

    // Second-order needle model: fn ≈ 2.15 Hz, ζ = 0.81 → 99% in ~300 ms with ~1.3% overshoot.
    private const double VuNaturalHz = 2.15;
    private const double VuDamping = 0.81;

    private readonly double _dt;
    private readonly double _w;
    private double _vuPos, _vuVel;

    private readonly double[] _squares;
    private int _sqIndex;
    private double _sqSum;
    private int _sinceRebase;

    private long _peakBits;     // max |x| since last read, as double bits (non-negative doubles order like longs)
    private int _overload;

    private double _vuDb = double.NegativeInfinity;
    private double _rmsDb = double.NegativeInfinity;

    public SimulatedMeterSource(int sampleRate, double rmsWindowSeconds = 0.3)
    {
        _dt = 1.0 / sampleRate;
        _w = 2 * Math.PI * VuNaturalHz;
        _squares = new double[Math.Max(1, (int)Math.Round(sampleRate * rmsWindowSeconds))];
    }

    public double RmsWindowSeconds => _squares.Length * _dt;

    /// <summary>Engine thread: process one block of samples.</summary>
    public void Process(ReadOnlySpan<float> block)
    {
        double peak = 0;
        bool over = false;
        for (int i = 0; i < block.Length; i++)
        {
            double x = block[i];
            double a = Math.Abs(x);
            if (a > peak) peak = a;
            if (a >= 1.0) over = true;

            // VU: rectify, then drive the damped second-order movement (semi-implicit Euler at audio rate).
            double acc = _w * _w * (a - _vuPos) - 2 * VuDamping * _w * _vuVel;
            _vuVel += acc * _dt;
            _vuPos += _vuVel * _dt;

            // RMS: ring of squared samples with a running double-precision sum.
            double sq = x * x;
            _sqSum += sq - _squares[_sqIndex];
            _squares[_sqIndex] = sq;
            if (++_sqIndex == _squares.Length) _sqIndex = 0;
        }

        // Periodically rebase the running sum to stop floating-point drift.
        _sinceRebase += block.Length;
        if (_sinceRebase >= _squares.Length * 8)
        {
            _sinceRebase = 0;
            double s = 0;
            foreach (var v in _squares) s += v;
            _sqSum = s;
        }

        double rms = Math.Sqrt(Math.Max(0, _sqSum) / _squares.Length);
        Volatile.Write(ref _rmsDb, rms < 1e-7 ? double.NegativeInfinity : Decibels.FromAmplitude(rms));
        double m = Math.Max(0, _vuPos);
        Volatile.Write(ref _vuDb, m < 1e-7 ? double.NegativeInfinity : 20 * Math.Log10(m / VuReference));

        AccumulatePeak(peak);
        if (over) Interlocked.Exchange(ref _overload, 1);
    }

    private void AccumulatePeak(double peak)
    {
        long bits = BitConverter.DoubleToInt64Bits(peak);
        long current = Interlocked.Read(ref _peakBits);
        while (bits > current)
        {
            long seen = Interlocked.CompareExchange(ref _peakBits, bits, current);
            if (seen == current) break;
            current = seen;
        }
    }

    /// <summary>UI thread: latest calibrated VU reading.</summary>
    public double VuDb => Volatile.Read(ref _vuDb);

    /// <summary>UI thread: latest RMS reading in dBFS.</summary>
    public double RmsDbfs => Volatile.Read(ref _rmsDb);

    /// <summary>UI thread: maximum sample peak (dBFS) since the previous call, then reset.</summary>
    public double ReadPeakDbfs()
    {
        double p = BitConverter.Int64BitsToDouble(Interlocked.Exchange(ref _peakBits, 0));
        return p <= 0 ? double.NegativeInfinity : Decibels.FromAmplitude(p);
    }

    /// <summary>UI thread: whether any sample reached full scale since the previous call, then reset.</summary>
    public bool ReadOverload() => Interlocked.Exchange(ref _overload, 0) != 0;

    public void Reset()
    {
        _vuPos = _vuVel = 0;
        Array.Clear(_squares);
        _sqSum = 0;
        _sqIndex = 0;
    }
}
