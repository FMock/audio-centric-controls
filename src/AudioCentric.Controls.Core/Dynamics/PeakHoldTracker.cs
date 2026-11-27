namespace AudioCentric.Controls.Dynamics;

/// <summary>
/// Display peak-hold marker: jumps to new maxima immediately, holds for <see cref="HoldSeconds"/>,
/// then falls at <see cref="DecayDbPerSecond"/> (never below the live level). Values are in dB.
/// </summary>
public sealed class PeakHoldTracker
{
    private double _holdRemaining;

    public double HoldSeconds { get; set; } = 2.0;
    public double DecayDbPerSecond { get; set; } = 20.0;
    public double Floor { get; set; } = -120.0;

    public double Value { get; private set; } = double.NegativeInfinity;

    public void Reset()
    {
        Value = double.NegativeInfinity;
        _holdRemaining = 0;
    }

    public double Update(double level, double elapsedSeconds)
    {
        if (double.IsNaN(level)) level = double.NegativeInfinity;
        if (level >= Value)
        {
            Value = level;
            _holdRemaining = HoldSeconds;
            return Value;
        }

        double dt = Math.Max(0, elapsedSeconds);
        if (_holdRemaining > 0)
        {
            double used = Math.Min(_holdRemaining, dt);
            _holdRemaining -= used;
            dt -= used;
        }

        if (dt > 0 && !double.IsNegativeInfinity(Value))
        {
            Value -= DecayDbPerSecond * dt;
            if (Value < level) Value = level;
            if (Value < Floor) Value = double.IsNegativeInfinity(level) ? double.NegativeInfinity : Math.Max(level, Floor);
        }
        return Value;
    }
}
