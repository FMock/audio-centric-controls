namespace AudioCentric.Controls.Input;

/// <summary>Filter shape of an equalizer band.</summary>
public enum EqBandType
{
    Peaking,
    LowShelf,
    HighShelf,
    HighPass,
    LowPass,
}

/// <summary>
/// One equalizer band as a parameter set. A frequency-response graph shows bands as editable handles and publishes
/// change <em>requests</em>; the host applies them to its processor and sends back the acknowledged bands and response.
/// </summary>
public sealed record EqBand(EqBandType Type, double FrequencyHz, double GainDb = 0, double Q = 0.707, bool IsEnabled = true)
{
    /// <summary>True when the band has a gain parameter (peaking and shelving filters). Pass filters only have frequency and Q.</summary>
    public bool HasGain => Type is EqBandType.Peaking or EqBandType.LowShelf or EqBandType.HighShelf;
}

/// <summary>A requested change to band <see cref="Index"/>. The host decides what is applied.</summary>
public sealed record EqBandChangeRequest(int Index, EqBand Requested);
