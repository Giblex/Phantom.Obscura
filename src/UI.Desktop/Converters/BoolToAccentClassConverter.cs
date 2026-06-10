using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace PhantomVault.UI.Converters
{

    public class BoolToAccentClassConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue && boolValue)
            {
                return "accent";
            }
            return "secondary";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return AvaloniaProperty.UnsetValue;
        }
    }
}

