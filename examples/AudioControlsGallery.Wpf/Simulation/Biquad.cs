using AudioCentric.Controls.Input;

namespace AudioControlsGallery.Simulation;

/// <summary>
/// Normalized biquad coefficients (a0 = 1). Difference equation y[n] = b0 x[n] + b1 x[n−1] + b2 x[n−2] − a1 y[n−1] − a2 y[n−2],
/// so H(z) = (b0 + b1 z⁻¹ + b2 z⁻²) / (1 + a1 z⁻¹ + a2 z⁻²). Designs follow the RBJ Audio EQ Cookbook.
/// </summary>
public readonly record struct Biquad(double B0, double B1, double B2, double A1, double A2)
{
    public static Biquad Identity { get; } = new(1, 0, 0, 0, 0);

    public static Biquad Design(EqBand band, double sampleRate)
    {
        if (!band.IsEnabled) return Identity;
        double f0 = Math.Clamp(band.FrequencyHz, 1, sampleRate * 0.49);
        double q = Math.Max(0.05, band.Q);
        double w0 = 2 * Math.PI * f0 / sampleRate;
        double cos = Math.Cos(w0), alpha = Math.Sin(w0) / (2 * q);
        double a = Math.Pow(10, band.GainDb / 40), sq = 2 * Math.Sqrt(a) * alpha;
        double b0, b1, b2, a0, a1, a2;
        switch (band.Type)
        {
            case EqBandType.LowShelf:
                b0 = a * ((a + 1) - (a - 1) * cos + sq);
                b1 = 2 * a * ((a - 1) - (a + 1) * cos);
                b2 = a * ((a + 1) - (a - 1) * cos - sq);
                a0 = (a + 1) + (a - 1) * cos + sq;
                a1 = -2 * ((a - 1) + (a + 1) * cos);
                a2 = (a + 1) + (a - 1) * cos - sq;
                break;
            case EqBandType.HighShelf:
                b0 = a * ((a + 1) + (a - 1) * cos + sq);
                b1 = -2 * a * ((a - 1) + (a + 1) * cos);
                b2 = a * ((a + 1) + (a - 1) * cos - sq);
                a0 = (a + 1) - (a - 1) * cos + sq;
                a1 = 2 * ((a - 1) - (a + 1) * cos);
                a2 = (a + 1) - (a - 1) * cos - sq;
                break;
            case EqBandType.LowPass:
                b0 = (1 - cos) / 2; b1 = 1 - cos; b2 = (1 - cos) / 2;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            case EqBandType.HighPass:
                b0 = (1 + cos) / 2; b1 = -(1 + cos); b2 = (1 + cos) / 2;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            default: // Peaking
                b0 = 1 + alpha * a; b1 = -2 * cos; b2 = 1 - alpha * a;
                a0 = 1 + alpha / a; a1 = -2 * cos; a2 = 1 - alpha / a;
                break;
        }
        return new Biquad(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }

    /// <summary>Complex response H(e^jω) at <paramref name="hz"/>, ω = 2πf/fs.</summary>
    public (double Re, double Im) Response(double hz, double sampleRate)
    {
        double w = 2 * Math.PI * hz / sampleRate;
        double c1 = Math.Cos(w), s1 = -Math.Sin(w), c2 = Math.Cos(2 * w), s2 = -Math.Sin(2 * w);
        double nr = B0 + B1 * c1 + B2 * c2, ni = B1 * s1 + B2 * s2;
        double dr = 1 + A1 * c1 + A2 * c2, di = A1 * s1 + A2 * s2;
        double d = dr * dr + di * di;
        return ((nr * dr + ni * di) / d, (ni * dr - nr * di) / d);
    }
}

/// <summary>Transposed direct form II state for one channel of one biquad.</summary>
public struct BiquadState
{
    private double _z1, _z2;

    public double Process(in Biquad c, double x)
    {
        double y = c.B0 * x + _z1;
        _z1 = c.B1 * x - c.A1 * y + _z2;
        _z2 = c.B2 * x - c.A2 * y;
        return y;
    }

    public void Reset() => _z1 = _z2 = 0;
}
