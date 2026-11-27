namespace AudioCentric.Controls.Measurements;

/// <summary>Decibel / linear-amplitude conversions (amplitude reference 1.0).</summary>
public static class Decibels
{
    /// <summary>20 log10(a). Returns -infinity for 0.</summary>
    public static double FromAmplitude(double amplitude) =>
        amplitude <= 0 ? double.NegativeInfinity : 20.0 * Math.Log10(amplitude);

    /// <summary>10^(dB/20). Returns 0 for -infinity.</summary>
    public static double ToAmplitude(double db) =>
        double.IsNegativeInfinity(db) ? 0.0 : Math.Pow(10.0, db / 20.0);
}
