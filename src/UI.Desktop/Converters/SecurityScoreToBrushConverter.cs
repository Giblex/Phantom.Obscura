using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PhantomVault.UI.Converters
{

    public class SecurityScoreToBrushConverter : IValueConverter
    {
        public static readonly SecurityScoreToBrushConverter Instance = new SecurityScoreToBrushConverter();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int score || (value is double d && (score = (int)d) >= 0))
            {
                if (score <= 40)
                {

                    return new SolidColorBrush(Color.Parse("#F48771"));
                }
                else if (score <= 70)
                {

                    return new SolidColorBrush(Color.Parse("#FFC107"));
                }
                else
                {

                    return new SolidColorBrush(Color.Parse("#B8E6C8"));
                }
            }

            return new SolidColorBrush(Color.Parse("#6B8CAE"));
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return AvaloniaProperty.UnsetValue;
        }
    }
}

