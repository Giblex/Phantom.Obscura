using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PhantomVault.UI.Converters
{
    /// <summary>
    /// Converts a boolean IsFavorite value to a star brush color.
    /// True -> yellow (filled star), False -> muted text color (outline/star outline color).
    /// This keeps the UI simple and avoids adding extra theme resources.
    /// </summary>
    public class BoolToStarBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b && b)
            {
                // Gold / amber (matches typical "favorite" color)
                return new SolidColorBrush(Color.Parse("#FFC107"));
            }

            // Muted text color used across the app
            return new SolidColorBrush(Color.Parse("#B0B8C0"));
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return AvaloniaProperty.UnsetValue;
        }
    }
}
