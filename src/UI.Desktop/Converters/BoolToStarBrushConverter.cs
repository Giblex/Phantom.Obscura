using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PhantomVault.UI.Converters
{

    public class BoolToStarBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b && b)
            {

                return new SolidColorBrush(Color.Parse("#FFC107"));
            }

            return new SolidColorBrush(Color.Parse("#B0B8C0"));
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return AvaloniaProperty.UnsetValue;
        }
    }
}

