namespace AudioCentric.Controls.Scales;

/// <summary>Logarithmic mapping f = fmin (fmax/fmin)^u, e.g. a 20 Hz - 20 kHz frequency knob (midpoint ~632 Hz).</summary>
public sealed class LogMapping : IValueMapping
{
    public LogMapping(double minimum, double maximum)
    {
        if (minimum <= 0) throw new ArgumentOutOfRangeException(nameof(minimum), "Logarithmic minimum must be > 0.");
        if (!(maximum > minimum)) throw new ArgumentException("Maximum must exceed minimum.");
        Minimum = minimum;
        Maximum = maximum;
    }

    public double Minimum { get; }
    public double Maximum { get; }

    public double ToNormalized(double value)
    {
        if (double.IsNaN(value) || value <= Minimum) return 0;
        return MappingMath.Clamp01(Math.Log(value / Minimum) / Math.Log(Maximum / Minimum));
    }

    public double FromNormalized(double normalized) =>
        Minimum * Math.Pow(Maximum / Minimum, MappingMath.Clamp01(normalized));
}
