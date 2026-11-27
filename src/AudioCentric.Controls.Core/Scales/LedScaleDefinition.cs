namespace AudioCentric.Controls.Scales;

/// <summary>
/// Ordered segment thresholds for an LED bar meter. Segment i is lit when L &gt;= Thresholds[i].
/// Segment colors follow <see cref="WarningThreshold"/> (amber above) and <see cref="DangerThreshold"/> (red above).
/// </summary>
public sealed class LedScaleDefinition
{
    public LedScaleDefinition(IEnumerable<double> thresholds, double warningThreshold, double dangerThreshold)
    {
        var t = thresholds.ToArray();
        if (t.Length == 0) throw new ArgumentException("At least one threshold is required.");
        for (int i = 1; i < t.Length; i++)
            if (!(t[i] > t[i - 1])) throw new ArgumentException("Thresholds must be strictly increasing.");
        Thresholds = t;
        WarningThreshold = warningThreshold;
        DangerThreshold = dangerThreshold;
    }

    public IReadOnlyList<double> Thresholds { get; }
    public double WarningThreshold { get; }
    public double DangerThreshold { get; }
    public int SegmentCount => Thresholds.Count;
    public double Minimum => Thresholds[0];
    public double Maximum => Thresholds[^1];

    /// <summary>Number of lit segments in bar mode for <paramref name="level"/>.</summary>
    public int LitCount(double level)
    {
        if (double.IsNaN(level)) return 0;
        int n = 0;
        for (int i = 0; i < Thresholds.Count && level >= Thresholds[i]; i++) n = i + 1;
        return n;
    }

    /// <summary>Index of the single lit segment in dot mode, or -1 when none is lit.</summary>
    public int DotIndex(double level) => LitCount(level) - 1;

    public ZoneKind ZoneOf(int segment)
    {
        double t = Thresholds[segment];
        if (t > DangerThreshold) return ZoneKind.Danger;
        if (t > WarningThreshold) return ZoneKind.Warning;
        return ZoneKind.Normal;
    }

    /// <summary>Uniform dB thresholds from <paramref name="min"/> to <paramref name="max"/> inclusive.</summary>
    public static LedScaleDefinition Uniform(double min, double max, int segments, double warning, double danger)
    {
        if (segments < 2) throw new ArgumentOutOfRangeException(nameof(segments));
        var t = Enumerable.Range(0, segments).Select(i => Math.Round(min + i * (max - min) / (segments - 1), 6));
        return new LedScaleDefinition(t, warning, danger);
    }
}
