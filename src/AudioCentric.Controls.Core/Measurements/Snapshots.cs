namespace AudioCentric.Controls.Measurements;

/// <summary>Read-only block of samples for one channel. <see cref="StartSample"/> is an absolute offset.</summary>
public sealed record SampleSnapshot(int SampleRate, int Channel, long StartSample, float[] Samples);

/// <summary>
/// Recent history of one channel for an oscilloscope, newest sample last. <see cref="StartSample"/> is the absolute
/// index of Samples[0] (used for trigger holdoff). The history should be at least two screens long so a trigger
/// with pre-trigger samples can be found.
/// </summary>
public sealed record TraceSnapshot(int SampleRate, long StartSample, float[] Samples);

/// <summary>
/// One analysed spectrum frame from the host's analysis provider: levels in dB (−∞ for silence) at ascending bin
/// centre frequencies in Hz. A bin at 0 Hz is DC; logarithmic displays show it with a separate indicator.
/// The metadata labels the display and lets band aggregation stay calibrated.
/// </summary>
public sealed record SpectrumSnapshot(int SampleRate, int Channel, double[] Frequencies, double[] LevelsDb, MeasurementUnit Unit = MeasurementUnit.DbFs)
{
    /// <summary>FFT length N (bin spacing = SampleRate / N). 0 when not FFT based or unknown.</summary>
    public int FftSize { get; init; }

    /// <summary>Window name for labelling, e.g. "Hann".</summary>
    public string Window { get; init; } = "";

    /// <summary>
    /// Equivalent noise bandwidth of the window in bins (Hann = 1.5, rectangular = 1). With peak-tone calibrated levels,
    /// summing bin powers over a band and dividing by this gives the band's level, so a tone reads the same in any band width.
    /// </summary>
    public double NoiseBandwidthBins { get; init; } = 1.0;

    /// <summary>Absolute index of the first sample in the analysed frame (timestamp).</summary>
    public long StartSample { get; init; }

    /// <summary>Bin spacing in Hz, or 0 when unknown.</summary>
    public double BinSpacing => FftSize > 0 ? (double)SampleRate / FftSize : 0;
}

/// <summary>
/// A frequency response: relative gain in dB (not dBFS) and optional phase in degrees at ascending frequencies in Hz.
/// A NaN gain marks an unreliable point (e.g. a masked weak bin in a measurement); the curve is broken there.
/// </summary>
public sealed record ResponseSnapshot(double[] Frequencies, double[] GainDb, double[]? PhaseDegrees = null, bool IsValid = true)
{
    /// <summary>True for a response measured from excitation and capture; false for one computed from coefficients.</summary>
    public bool IsMeasured { get; init; }

    /// <summary>Sample rate of the processor (the response ends at Nyquist). 0 when unknown.</summary>
    public int SampleRate { get; init; }
}
