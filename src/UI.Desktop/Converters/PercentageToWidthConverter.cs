using Avalonia.Data.Converters;
using Avalonia;
using System;
using System.Globalization;

namespace PhantomVault.UI.Converters
{

    public class PercentageToWidthConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double percentage)
            {

                return (percentage / 100.0) * 40.0;
            }
            return 0.0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return AvaloniaProperty.UnsetValue;
        }
    }
}

