namespace AudioCentric.Controls.Dynamics;

/// <summary>
/// Display level with immediate attack and linear-in-dB release (e.g. 20 dB/s for peak bars).
/// Used for LED segment decay. It adds no smoothing on the way up.
/// </summary>
public sealed class DecayFollower
{
    public double ReleaseDbPerSecond { get; set; } = 20.0;
    public double Floor { get; set; } = -120.0;
    public double Value { get; private set; } = double.NegativeInfinity;

    public void Reset(double value = double.NegativeInfinity) => Value = value;

    public double Update(double level, double elapsedSeconds)
    {
        if (double.IsNaN(level)) level = double.NegativeInfinity;
        if (level >= Value || ReleaseDbPerSecond <= 0)
        {
            Value = level;
            return Value;
        }

        if (double.IsNegativeInfinity(Value)) return Value;
        double next = Value - ReleaseDbPerSecond * Math.Max(0, elapsedSeconds);
        if (next < level) next = level;
        if (next < Floor) next = level;
        Value = next;
        return Value;
    }
}
