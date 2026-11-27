namespace AudioCentric.Controls.Scales;

/// <summary>
/// Maps a value in engineering units to a normalized position u in [0,1] and back.
/// Implementations must be monotonic non-decreasing and clamp to [0,1].
/// </summary>
public interface IValueMapping
{
    double ToNormalized(double value);
    double FromNormalized(double normalized);
}

internal static class MappingMath
{
    public static double Clamp01(double u) => double.IsNaN(u) ? 0 : Math.Clamp(u, 0.0, 1.0);
}
