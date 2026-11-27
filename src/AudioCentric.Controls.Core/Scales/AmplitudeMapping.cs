using AudioCentric.Controls.Measurements;

namespace AudioCentric.Controls.Scales;

/// <summary>
/// Needle deflection proportional to linear amplitude, as on a classic VU meter:
/// u = 10^(v/20) / 10^(max/20). Zero amplitude (silence, -infinity) rests at u = 0.
/// With max = +3 VU, 0 VU lands at ~70.8% of full deflection ("100%" on the percentage scale).
/// </summary>
public sealed class AmplitudeMapping : IValueMapping
{
    private readonly double _maxAmplitude;

    public AmplitudeMapping(double maximumDb)
    {
        MaximumDb = maximumDb;
        _maxAmplitude = Decibels.ToAmplitude(maximumDb);
    }

    public double MaximumDb { get; }

    public double ToNormalized(double value) =>
        double.IsNaN(value) ? 0 : MappingMath.Clamp01(Decibels.ToAmplitude(value) / _maxAmplitude);

    public double FromNormalized(double normalized) =>
        Decibels.FromAmplitude(MappingMath.Clamp01(normalized) * _maxAmplitude);
}
