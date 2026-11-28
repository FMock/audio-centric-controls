namespace AudioCentric.Controls.Measurements;

/// <summary>Read-only block of samples for one channel. <see cref="StartSample"/> is an absolute offset.</summary>
public sealed record SampleSnapshot(int SampleRate, int Channel, long StartSample, float[] Samples);

/// <summary>
/// Recent history of one channel for an oscilloscope, newest sample last. <see cref="StartSample"/> is the absolute
/// index of Samples[0] (used for trigger holdoff). The history should be at least two screens long so a trigger
/// with pre-trigger samples can be found.
/// </summary>
public sealed record TraceSnapshot(int SampleRate, long StartSample, float[] Samples);

/// <summary>Spectrum magnitudes (dB) at given center frequencies (Hz).</summary>
public sealed record SpectrumSnapshot(int SampleRate, int Channel, double[] Frequencies, double[] LevelsDb, MeasurementUnit Unit = MeasurementUnit.DbFs);

/// <summary>Frequency response: gain in dB and optional phase in degrees per frequency point.</summary>
public sealed record ResponseSnapshot(double[] Frequencies, double[] GainDb, double[]? PhaseDegrees = null, bool IsValid = true);
