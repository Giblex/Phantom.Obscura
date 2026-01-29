using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace PhantomVault.UI.Converters
{
    /// <summary>
    /// Converts a percentage (0-100) to a proportional width value.
    /// Used for progress bars that need to scale based on percentage.
    /// </summary>
    public class PercentageToWidthConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double percentage)
            {
                // Maximum width is 40 (as specified in TOTP progress bar)
                return (percentage / 100.0) * 40.0;
            }
            return 0.0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
