using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PhantomVault.UI.Converters
{
    /// <summary>
    /// Converts a boolean value into one of two doubles specified via converter parameter ("true|false").
    /// </summary>
    public sealed class BoolToDoubleConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var token = value is bool b && b;
            var parameterText = parameter as string ?? string.Empty;
            var parts = parameterText.Split('|', StringSplitOptions.RemoveEmptyEntries);
            double trueValue = parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var t)
                ? t
                : 0d;
            double falseValue = parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
                ? f
                : trueValue;
            return token ? trueValue : falseValue;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return Avalonia.AvaloniaProperty.UnsetValue;
        }
    }
}
