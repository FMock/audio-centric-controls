using AudioCentric.Controls.Scales;

namespace AudioCentric.Controls.Input;

/// <summary>
/// UI-independent model of a continuous or stepped parameter (gain, pan, frequency, Q, mix...).
/// Owns the mapping between the control's normalized position and the engineering value.
/// It never touches audio: the host applies the target value and any smoothing.
/// </summary>
public sealed class ParameterModel
{
    private double _value;

    public ParameterModel(double minimum, double maximum, double defaultValue, IValueMapping? mapping = null)
    {
        if (!(maximum > minimum)) throw new ArgumentException("Maximum must exceed minimum.");
        Minimum = minimum;
        Maximum = maximum;
        Mapping = mapping ?? new LinearMapping(minimum, maximum);
        DefaultValue = Math.Clamp(defaultValue, minimum, maximum);
        _value = DefaultValue;
    }

    public double Minimum { get; }
    public double Maximum { get; }
    public double DefaultValue { get; }
    public IValueMapping Mapping { get; }

    /// <summary>Quantization step in engineering units; 0 for continuous.</summary>
    public double Step { get; set; }

    /// <summary>Detent positions (engineering units) that capture nearby values, e.g. 0 dB or centre pan.</summary>
    public IReadOnlyList<double> Detents { get; set; } = Array.Empty<double>();

    /// <summary>Capture width around each detent, as a fraction of normalized travel.</summary>
    public double DetentWidth { get; set; } = 0.015;

    public double Value
    {
        get => _value;
        set => _value = Coerce(value);
    }

    public double Normalized
    {
        get => Mapping.ToNormalized(_value);
        set => Value = ApplyDetent(Mapping.FromNormalized(value), value);
    }

    public double Coerce(double value)
    {
        if (double.IsNaN(value)) return _value;
        double v = Math.Clamp(value, Minimum, Maximum);
        if (Step > 0) v = Math.Clamp(Minimum + Math.Round((v - Minimum) / Step) * Step, Minimum, Maximum);
        return v;
    }

    /// <summary>Move by a fraction of total travel (e.g. 0.01 per arrow key, 0.001 with fine modifier).</summary>
    public void Nudge(double normalizedDelta) => Normalized = Math.Clamp(Normalized + normalizedDelta, 0, 1);

    public void ResetToDefault() => _value = DefaultValue;

    private double ApplyDetent(double value, double normalized)
    {
        foreach (var d in Detents)
        {
            if (Math.Abs(Mapping.ToNormalized(d) - normalized) <= DetentWidth) return d;
        }
        return value;
    }
}
