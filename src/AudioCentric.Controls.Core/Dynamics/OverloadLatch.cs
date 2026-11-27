namespace AudioCentric.Controls.Dynamics;

/// <summary>How an overload lamp behaves after the overload condition ends.</summary>
public enum OverloadLatchMode
{
    /// <summary>The lamp is lit only while the host reports overload.</summary>
    Momentary,
    /// <summary>The lamp stays lit for <see cref="OverloadLatch.HoldSeconds"/> after the last overload.</summary>
    HoldTime,
    /// <summary>The lamp stays lit until it is reset (the engineer presses the reset button).</summary>
    Latched,
}

/// <summary>
/// Overload lamp state, kept separate from the measurement. The host reports overload; this class owns
/// only the lamp's presentation (hold or latch) and reset.
/// </summary>
public sealed class OverloadLatch
{
    private double _holdRemaining;

    public OverloadLatchMode Mode { get; set; } = OverloadLatchMode.Latched;
    public double HoldSeconds { get; set; } = 2.0;

    public bool IsLit { get; private set; }

    /// <summary>Number of overload events (rising edges) since the last reset.</summary>
    public int EventCount { get; private set; }

    private bool _wasOverloaded;

    /// <summary>Feed the host's current overload flag. Returns <see cref="IsLit"/>.</summary>
    public bool Update(bool isOverloaded, double elapsedSeconds)
    {
        if (isOverloaded && !_wasOverloaded) EventCount++;
        _wasOverloaded = isOverloaded;

        switch (Mode)
        {
            case OverloadLatchMode.Momentary:
                IsLit = isOverloaded;
                break;
            case OverloadLatchMode.HoldTime:
                if (isOverloaded)
                {
                    _holdRemaining = HoldSeconds;
                    IsLit = true;
                }
                else if (IsLit)
                {
                    _holdRemaining -= Math.Max(0, elapsedSeconds);
                    if (_holdRemaining <= 0) IsLit = false;
                }
                break;
            case OverloadLatchMode.Latched:
                if (isOverloaded) IsLit = true;
                break;
        }
        return IsLit;
    }

    /// <summary>Turn the lamp off. If the host still reports overload, it relights on the next update.</summary>
    public void Reset()
    {
        IsLit = false;
        _holdRemaining = 0;
        EventCount = 0;
        _wasOverloaded = false;
    }
}
