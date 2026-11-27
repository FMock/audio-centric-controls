namespace AudioControlsGallery.Simulation;

/// <summary>
/// Stereo phase correlation, r = Σ L·R / √(Σ L² · Σ R²), integrated with a 300 ms exponential window.
/// Reports invalid (NaN) when either channel is effectively silent, because 0/0 is undefined.
/// </summary>
public sealed class SimulatedCorrelationSource
{
    private readonly double _a;
    private double _lr, _ll, _rr;
    private double _value = double.NaN;

    public SimulatedCorrelationSource(int sampleRate, double integrationSeconds = 0.3)
    {
        _a = Math.Exp(-1.0 / (sampleRate * integrationSeconds));
    }

    public void Process(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        double a = _a, b = 1 - _a;
        for (int i = 0; i < left.Length; i++)
        {
            double l = left[i], r = right[i];
            _lr = a * _lr + b * l * r;
            _ll = a * _ll + b * l * l;
            _rr = a * _rr + b * r * r;
        }
        double den = Math.Sqrt(_ll * _rr);
        Volatile.Write(ref _value, den < 1e-10 ? double.NaN : Math.Clamp(_lr / den, -1, 1));
    }

    /// <summary>Latest correlation (−1…+1), or NaN when unavailable.</summary>
    public double Value => Volatile.Read(ref _value);
}
