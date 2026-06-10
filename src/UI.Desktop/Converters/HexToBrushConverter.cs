using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PhantomVault.UI.Converters
{

    public class HexToBrushConverter : IValueConverter
    {
        public static readonly HexToBrushConverter Instance = new HexToBrushConverter();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string s && !string.IsNullOrWhiteSpace(s))
            {
                try
                {
                    var color = Color.Parse(s.Trim());
                    return new SolidColorBrush(color);
                }
                catch
                {

                }
            }
            return Brushes.Transparent;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ISolidColorBrush b)
            {
                var c = b.Color;
                return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }
            return null;
        }
    }
}

