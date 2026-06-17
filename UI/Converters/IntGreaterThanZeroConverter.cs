using System.Globalization;

namespace QuantumZ.UI.Converters;

public sealed class IntGreaterThanZeroConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var integer = value switch
        {
            int i => i,
            long l => l,
            short s => s,
            uint u => u,
            ulong ul => (long)ul,
            _ => 0
        };

        return integer > 0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
