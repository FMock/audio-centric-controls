namespace AudioCentric.Controls.Scales;

/// <summary>
/// The single value-to-angle transform shared by scale ticks, zones, labels and the needle.
/// Angles are in degrees, 0 = straight up, positive = clockwise (screen coordinates, y down).
/// </summary>
public readonly record struct ArcGeometry(double StartAngle, double EndAngle)
{
    public double Sweep => EndAngle - StartAngle;

    public double AngleFor(double normalized) =>
        StartAngle + (double.IsNaN(normalized) ? 0 : Math.Clamp(normalized, 0, 1)) * Sweep;

    public double NormalizedFor(double angle) => Sweep == 0 ? 0 : Math.Clamp((angle - StartAngle) / Sweep, 0, 1);

    /// <summary>Point on a circle of <paramref name="radius"/> about (cx, cy) at the given angle (screen coordinates).</summary>
    public static (double X, double Y) PointAt(double cx, double cy, double radius, double angleDegrees)
    {
        double r = angleDegrees * Math.PI / 180.0;
        return (cx + radius * Math.Sin(r), cy - radius * Math.Cos(r));
    }
}
