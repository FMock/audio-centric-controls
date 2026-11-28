using System.Globalization;

namespace AudioCentric.Controls.Plotting;

public enum FrequencyScale
{
    /// <summary>Equal width per octave (f = fmin (fmax/fmin)^u). The usual audio view.</summary>
    Logarithmic,
    /// <summary>Equal width per hertz.</summary>
    Linear,
}

/// <summary>A grid line or tick on a plot axis. Major ticks carry a label.</summary>
public readonly record struct AxisTick(double Value, bool IsMajor, string? Label = null);

/// <summary>
/// Frequency axis for spectrum and response plots: maps hertz to a normalized 0…1 position and back, and generates
/// the grid. Logarithmic axes label 1-2-5 per decade (20, 50, 100, 200, 500, 1k …) with minor lines at the other integers.
/// </summary>
public sealed class FrequencyAxis
{
    public FrequencyAxis(double minimum, double maximum, FrequencyScale scale = FrequencyScale.Logarithmic)
    {
        if (scale == FrequencyScale.Logarithmic && minimum <= 0) throw new ArgumentOutOfRangeException(nameof(minimum), "A logarithmic axis needs a minimum above 0 Hz.");
        if (!(maximum > minimum)) throw new ArgumentException("Maximum must exceed minimum.");
        Minimum = minimum;
        Maximum = maximum;
        Scale = scale;
    }

    public double Minimum { get; }
    public double Maximum { get; }
    public FrequencyScale Scale { get; }

    /// <summary>
    /// Normalized position of <paramref name="hz"/>; not clamped, so values outside the range fall outside 0…1.
    /// On a logarithmic axis 0 Hz and below map to −∞ (DC cannot be placed on a log axis).
    /// </summary>
    public double ToNormalized(double hz)
    {
        if (Scale == FrequencyScale.Linear) return (hz - Minimum) / (Maximum - Minimum);
        if (!(hz > 0)) return double.NegativeInfinity;
        return Math.Log(hz / Minimum) / Math.Log(Maximum / Minimum);
    }

    public double FromNormalized(double u) =>
        Scale == FrequencyScale.Linear ? Minimum + u * (Maximum - Minimum) : Minimum * Math.Pow(Maximum / Minimum, u);

    /// <summary>Grid lines inside the range, ascending.</summary>
    public IReadOnlyList<AxisTick> Ticks()
    {
        var ticks = new List<AxisTick>();
        if (Scale == FrequencyScale.Linear)
        {
            foreach (var t in AxisTicks.Linear(Minimum, Maximum, 10))
                ticks.Add(t.Label == null ? t : t with { Label = FormatHz(t.Value) });
            return ticks;
        }

        // On a narrow log range label every integer, otherwise 1-2-5.
        bool dense = Maximum / Minimum < 12;
        for (int exp = (int)Math.Floor(Math.Log10(Minimum)); exp <= (int)Math.Ceiling(Math.Log10(Maximum)); exp++)
        {
            double decade = Math.Pow(10, exp);
            for (int m = 1; m <= 9; m++)
            {
                double f = Math.Round(m * decade, 9);
                if (f < Minimum * (1 - 1e-9) || f > Maximum * (1 + 1e-9)) continue;
                bool major = dense || m is 1 or 2 or 5;
                ticks.Add(new AxisTick(f, major, major ? FormatHz(f) : null));
            }
        }
        return ticks;
    }

    /// <summary>Compact axis label: "20", "500", "1k", "1.5k", "20k".</summary>
    public static string FormatHz(double hz)
    {
        var ci = CultureInfo.InvariantCulture;
        if (Math.Abs(hz) >= 1000) return (hz / 1000).ToString("0.##", ci) + "k";
        return hz.ToString(hz < 10 ? "0.##" : "0.#", ci);
    }

    /// <summary>Readout text with unit: "48.0 Hz", "1.02 kHz", "15.6 kHz".</summary>
    public static string FormatHzWithUnit(double hz)
    {
        var ci = CultureInfo.InvariantCulture;
        if (Math.Abs(hz) >= 10000) return (hz / 1000).ToString("0.0", ci) + " kHz";
        if (Math.Abs(hz) >= 1000) return (hz / 1000).ToString("0.00", ci) + " kHz";
        return hz.ToString(hz < 100 ? "0.0" : "0", ci) + " Hz";
    }
}

/// <summary>Grid generation for linear value axes (dB, degrees).</summary>
public static class AxisTicks
{
    /// <summary>The smallest 1, 2 or 5 × 10ⁿ step that is at least <paramref name="rough"/>.</summary>
    public static double NiceStep(double rough)
    {
        if (!(rough > 0) || double.IsInfinity(rough)) return 1;
        double exp = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        double m = rough / exp;
        double nice = m <= 1 + 1e-9 ? 1 : m <= 2 + 1e-9 ? 2 : m <= 5 + 1e-9 ? 5 : 10;
        return nice * exp;
    }

    /// <summary>
    /// Major ticks (labelled with invariant numbers) on a 1-2-5 step giving at most <paramref name="maxMajor"/> lines,
    /// plus minor ticks between them.
    /// </summary>
    public static IReadOnlyList<AxisTick> Linear(double min, double max, int maxMajor, double? step = null)
    {
        var ticks = new List<AxisTick>();
        if (!(max > min)) return ticks;
        double s = step ?? NiceStep((max - min) / Math.Max(1, maxMajor - 1));
        double mantissa = s / Math.Pow(10, Math.Floor(Math.Log10(s)));
        double minor = Math.Abs(mantissa - 2) < 1e-6 ? s / 4 : s / 5;
        var ci = CultureInfo.InvariantCulture;
        long first = (long)Math.Ceiling(min / minor - 1e-9), last = (long)Math.Floor(max / minor + 1e-9);
        int perMajor = (int)Math.Round(s / minor);
        for (long i = first; i <= last; i++)
        {
            double v = Math.Round(i * minor, 9);
            bool major = i % perMajor == 0;
            ticks.Add(new AxisTick(v, major, major ? v.ToString("0.###", ci) : null));
        }
        return ticks;
    }
}
