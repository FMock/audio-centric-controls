using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AudioControlsGallery;

/// <summary>Binds a RadioButton's IsChecked to an enum property: ConverterParameter is the enum member name.</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true && parameter is string name ? Enum.Parse(targetType, name) : Binding.DoNothing;
}

/// <summary>True when the value equals ConverterParameter (by reference or Equals); used for face colour pickers.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Equals(value, parameter);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? parameter : Binding.DoNothing;
}

/// <summary>Formats a dB value with the unit in ConverterParameter ("VU", "dBFS", "dB", "Hz"...).</summary>
public sealed class LevelTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string unit = parameter as string ?? "";
        if (value is not double v || double.IsNaN(v)) return "---";
        if (double.IsNegativeInfinity(v)) return $"−∞ {unit}".Trim();
        if (unit == "Hz")
            return v >= 1000 ? (v / 1000).ToString("0.00", CultureInfo.InvariantCulture) + " kHz" : v.ToString("0", CultureInfo.InvariantCulture) + " Hz";
        if (unit == "%") return (v * 100).ToString("0", CultureInfo.InvariantCulture) + " %";
        if (unit == "pan")
            return Math.Abs(v) < 0.005 ? "C" : (v < 0 ? "L" : "R") + Math.Round(Math.Abs(v) * 100).ToString(CultureInfo.InvariantCulture);
        string sign = unit is "dB" && v > 0.04 ? "+" : "";
        return sign + v.ToString("0.0;−0.0;0.0", CultureInfo.InvariantCulture) + (unit.Length > 0 ? " " + unit : "");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ^ (parameter as string == "invert") ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
