using System.ComponentModel;
using System.Globalization;

namespace AudioCentric.Controls.Wpf.Controls;

/// <summary>Lets XAML set a list of strings as one comma-separated attribute, e.g. <c>ScaleLabels="LOW,MED,HIGH"</c>. Blank entries are kept as gaps.</summary>
public sealed class StringListConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string s ? s.Split(',').Select(x => x.Trim()).ToArray() : base.ConvertFrom(context, culture, value);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
        destinationType == typeof(string) && value is IEnumerable<string> list ? string.Join(",", list) : base.ConvertTo(context, culture, value, destinationType);
}
