namespace AudioCentric.Controls.Input;

/// <summary>Whether a switch stays in its new state or springs back on release.</summary>
public enum ToggleMode
{
    Latching,
    Momentary,
}

/// <summary>Host-reported status of a switch's action.</summary>
public enum ToggleStatus
{
    /// <summary>The actual state equals the requested state.</summary>
    Ready,
    /// <summary>A request was sent and has not been acknowledged yet.</summary>
    Pending,
    /// <summary>The host failed to apply the request.</summary>
    Fault,
}

/// <summary>
/// Separates what the user asked for (<see cref="Requested"/>) from what the host reports
/// (<see cref="Actual"/>). The illumination shows the actual state; pending and fault states stay visible.
/// </summary>
public sealed class ToggleStateModel
{
    public bool Requested { get; private set; }
    public bool Actual { get; private set; }
    public ToggleStatus Status { get; private set; } = ToggleStatus.Ready;

    /// <summary>When true, a request is considered applied immediately (no host acknowledgement).</summary>
    public bool AutoAcknowledge { get; set; } = true;

    public void Request(bool state)
    {
        Requested = state;
        if (AutoAcknowledge)
        {
            Actual = state;
            Status = ToggleStatus.Ready;
        }
        else if (Actual != state)
        {
            Status = ToggleStatus.Pending;
        }
    }

    /// <summary>The host reports the state it actually applied.</summary>
    public void Acknowledge(bool actual)
    {
        Actual = actual;
        Status = actual == Requested ? ToggleStatus.Ready : ToggleStatus.Fault;
    }

    public void Fail()
    {
        Status = ToggleStatus.Fault;
    }
}
