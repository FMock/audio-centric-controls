namespace AudioCentric.Controls.Scales;

/// <summary>Uniform mapping: v = min + u(max - min). Used for linear knobs and uniform dB layouts.</summary>
public sealed class LinearMapping : IValueMapping
{
    public LinearMapping(double minimum, double maximum)
    {
        if (!(maximum > minimum)) throw new ArgumentException("Maximum must exceed minimum.");
        Minimum = minimum;
        Maximum = maximum;
    }

    public double Minimum { get; }
    public double Maximum { get; }

    public double ToNormalized(double value)
    {
        if (double.IsNegativeInfinity(value)) return 0;
        return MappingMath.Clamp01((value - Minimum) / (Maximum - Minimum));
    }

    public double FromNormalized(double normalized) =>
        Minimum + MappingMath.Clamp01(normalized) * (Maximum - Minimum);
}
