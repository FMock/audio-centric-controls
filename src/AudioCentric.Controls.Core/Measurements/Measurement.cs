namespace AudioCentric.Controls.Measurements;

/// <summary>
/// A single host-supplied reading. Silence is represented by <see cref="double.NegativeInfinity"/>
/// with <see cref="IsValid"/> = true; an unavailable or undefined reading has <see cref="IsValid"/> = false.
/// </summary>
public readonly record struct Measurement(double Value, MeasurementUnit Unit, bool IsValid = true)
{
    public static Measurement Invalid(MeasurementUnit unit) => new(double.NaN, unit, false);

    public bool IsSilence => IsValid && double.IsNegativeInfinity(Value);

    public string Format(string numberFormat = "0.0")
    {
        if (!IsValid) return "---";
        if (double.IsNegativeInfinity(Value)) return $"-∞ {Unit.Symbol()}".TrimEnd();
        return $"{Value.ToString(numberFormat, System.Globalization.CultureInfo.InvariantCulture)} {Unit.Symbol()}".TrimEnd();
    }
}
