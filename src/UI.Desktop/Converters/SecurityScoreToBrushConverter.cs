using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PhantomVault.UI.Converters
{
    /// <summary>
    /// Converts a security score (0-100) into a gradient SolidColorBrush.
    /// 0-40: Error/Red (#F48771)
    /// 41-70: Warning/Yellow (#FFC107)
    /// 71-100: Success/Green (#B8E6C8)
    /// </summary>
    public class SecurityScoreToBrushConverter : IValueConverter
    {
        public static readonly SecurityScoreToBrushConverter Instance = new SecurityScoreToBrushConverter();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int score || (value is double d && (score = (int)d) >= 0))
            {
                if (score <= 40)
                {
                    // Error/Red
                    return new SolidColorBrush(Color.Parse("#F48771"));
                }
                else if (score <= 70)
                {
                    // Warning/Yellow
                    return new SolidColorBrush(Color.Parse("#FFC107"));
                }
                else
                {
                    // Success/Green
                    return new SolidColorBrush(Color.Parse("#B8E6C8"));
                }
            }

            // Default to accent color
            return new SolidColorBrush(Color.Parse("#6B8CAE"));
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
