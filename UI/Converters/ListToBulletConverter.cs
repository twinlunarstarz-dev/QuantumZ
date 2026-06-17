using System.Globalization;

namespace QuantumZ.UI.Converters;

public sealed class ListToBulletConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is IEnumerable<string> items)
        {
            var lines = items.Where(item => !string.IsNullOrWhiteSpace(item))
                             .Select(item => $"• {item.Trim()}");
            return string.Join(Environment.NewLine, lines);
        }

        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
