namespace AudioCentric.Controls.Measurements;

/// <summary>Units of a value supplied by the host. Controls label readings with these explicitly.</summary>
public enum MeasurementUnit
{
    /// <summary>Volume units relative to a reference alignment (e.g. -18 dBFS sine = 0 VU).</summary>
    Vu,
    /// <summary>Decibels relative to digital full scale (sample peak or RMS).</summary>
    DbFs,
    /// <summary>True-peak decibels (oversampled reconstruction estimate).</summary>
    DbTp,
    /// <summary>Generic relative decibels (gain, response magnitude).</summary>
    Db,
    /// <summary>Normalized stereo phase correlation, -1 to +1.</summary>
    Correlation,
    /// <summary>Frequency in hertz.</summary>
    Hz,
    /// <summary>Unitless linear value.</summary>
    Linear,
}

public static class MeasurementUnitExtensions
{
    public static string Symbol(this MeasurementUnit unit) => unit switch
    {
        MeasurementUnit.Vu => "VU",
        MeasurementUnit.DbFs => "dBFS",
        MeasurementUnit.DbTp => "dBTP",
        MeasurementUnit.Db => "dB",
        MeasurementUnit.Correlation => "",
        MeasurementUnit.Hz => "Hz",
        _ => "",
    };
}
