namespace AudioCentric.Controls.Plotting;

public enum TriggerSlope
{
    Rising,
    Falling,
}

public enum TriggerMode
{
    /// <summary>No triggering: always show the newest samples (the trace drifts).</summary>
    Free,
    /// <summary>Trigger when possible; free-run when no trigger is found, so the screen is never blank.</summary>
    Auto,
    /// <summary>Update only on a trigger; otherwise keep the last trace.</summary>
    Normal,
    /// <summary>Capture one triggered trace, then stop until re-armed.</summary>
    Single,
}

public enum ScopeStatus
{
    /// <summary>Showing a triggered trace.</summary>
    Triggered,
    /// <summary>Free-running (Free mode, or Auto without a trigger).</summary>
    FreeRunning,
    /// <summary>Normal or Single mode is waiting for a trigger.</summary>
    Waiting,
    /// <summary>Single capture done, or the display is frozen.</summary>
    Stopped,
}

/// <summary>
/// Oscilloscope triggering over a window of recent history. It is UI-independent and deterministic.
/// The host supplies recent samples with an absolute start offset. This class picks which visible window to show:
/// the newest trigger crossing (with hysteresis) whose window fits, honouring trigger position and holdoff.
/// </summary>
public sealed class ScopeTrigger
{
    private long _lastTriggerAbsolute = long.MinValue;
    private bool _singleDone;

    public TriggerMode Mode { get; set; } = TriggerMode.Auto;
    public TriggerSlope Slope { get; set; } = TriggerSlope.Rising;
    public float Level { get; set; }

    /// <summary>Hysteresis band below (rising) or above (falling) the level that must be crossed first. Rejects noise.</summary>
    public float Hysteresis { get; set; } = 0.02f;

    /// <summary>Horizontal trigger position as a fraction of the screen (0 = left edge). Default 0.5 (centre).</summary>
    public double TriggerPosition { get; set; } = 0.5;

    /// <summary>Minimum number of samples between accepted triggers.</summary>
    public long HoldoffSamples { get; set; }

    public ScopeStatus Status { get; private set; } = ScopeStatus.Waiting;

    /// <summary>Re-arm Single mode (and clear holdoff history).</summary>
    public void Arm()
    {
        _singleDone = false;
        _lastTriggerAbsolute = long.MinValue;
        Status = ScopeStatus.Waiting;
    }

    /// <summary>
    /// Chooses the start index (into <paramref name="history"/>) of a window of <paramref name="visible"/> samples,
    /// or returns -1 to keep showing the previous trace.
    /// <paramref name="historyStart"/> is the absolute sample index of history[0].
    /// </summary>
    public int Select(ReadOnlySpan<float> history, long historyStart, int visible)
    {
        if (visible <= 0 || history.Length < visible) { Status = ScopeStatus.Waiting; return -1; }
        int newest = history.Length - visible;

        if (Mode == TriggerMode.Single && _singleDone) { Status = ScopeStatus.Stopped; return -1; }
        if (Mode == TriggerMode.Free) { Status = ScopeStatus.FreeRunning; return newest; }

        int pre = (int)Math.Round(Math.Clamp(TriggerPosition, 0, 1) * visible);
        int from = Math.Max(1, pre), to = history.Length - (visible - pre); // trigger index range [from, to]
        int trig = FindLast(history, Level, Slope, from, to, Hysteresis);
        if (trig >= 0 && HoldoffSamples > 0 && _lastTriggerAbsolute != long.MinValue && historyStart + trig < _lastTriggerAbsolute + HoldoffSamples)
            trig = -1;

        if (trig >= 0)
        {
            _lastTriggerAbsolute = historyStart + trig;
            Status = ScopeStatus.Triggered;
            if (Mode == TriggerMode.Single) _singleDone = true;
            return trig - pre;
        }

        if (Mode == TriggerMode.Auto) { Status = ScopeStatus.FreeRunning; return newest; }
        Status = ScopeStatus.Waiting;
        return -1;
    }

    /// <summary>Index of the last sample (in [from, to]) completing a crossing of <paramref name="level"/>, or -1.</summary>
    public static int FindLast(ReadOnlySpan<float> s, float level, TriggerSlope slope, int from, int to, float hysteresis)
    {
        to = Math.Min(to, s.Length - 1);
        if (to < from || s.Length < 2) return -1;
        float armLevel = slope == TriggerSlope.Rising ? level - hysteresis : level + hysteresis;
        // One forward pass: arm when the signal is beyond the hysteresis band, fire on the level crossing.
        // Scanning from 0 establishes the arm state before the window; only crossings inside [from, to] count.
        bool armed = false;
        int last = -1;
        for (int i = 0; i <= to; i++)
        {
            float x = s[i];
            bool beyond = slope == TriggerSlope.Rising ? x <= armLevel : x >= armLevel;
            bool crossed = slope == TriggerSlope.Rising ? x >= level : x <= level;
            if (beyond) armed = true;
            else if (armed && crossed)
            {
                if (i >= from) last = i;
                armed = false;
            }
        }
        return last;
    }
}
