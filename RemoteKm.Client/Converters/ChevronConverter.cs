using System.Globalization;

namespace RemoteKm.Client.Converters;

/// <summary>Maps an expanded/collapsed bool to an up/down chevron glyph.</summary>
public sealed class ChevronConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "▲" : "▼";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
