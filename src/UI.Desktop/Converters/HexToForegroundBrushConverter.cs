using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PhantomVault.UI.Converters
{
    public class HexToForegroundBrushConverter : IValueConverter
    {
        public static readonly HexToForegroundBrushConverter Instance = new HexToForegroundBrushConverter();
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                if (value is string hex && !string.IsNullOrWhiteSpace(hex))
                {
                    var color = Color.Parse(hex);

                    double r = color.R / 255.0;
                    double g = color.G / 255.0;
                    double b = color.B / 255.0;

                    double luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;

                    return luminance < 0.55 ? Brushes.White : Brushes.Black;
                }
            }
            catch { }

            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}

