using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PhantomVault.UI.Converters
{
    public sealed class IsWidthAtLeastConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            double width = 0;
            switch (value)
            {
                case double d:
                    width = d;
                    break;
                case float f:
                    width = f;
                    break;
                case int i:
                    width = i;
                    break;
                default:
                    break;
            }

            double threshold = 1200;
            if (parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var t))
            {
                threshold = t;
            }
            return width >= threshold;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return Avalonia.AvaloniaProperty.UnsetValue;
        }
    }
}
