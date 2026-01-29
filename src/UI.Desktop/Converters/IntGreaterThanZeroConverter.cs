using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PhantomVault.UI.Converters
{
    /// <summary>
    /// Converts an integer to a boolean indicating if it's greater than zero.
    /// Useful for showing/hiding elements based on count.
    /// </summary>
    public class IntGreaterThanZeroConverter : IValueConverter
    {
        public static readonly IntGreaterThanZeroConverter Instance = new IntGreaterThanZeroConverter();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int intValue)
            {
                return intValue > 0;
            }

            if (value is double doubleValue)
            {
                return doubleValue > 0;
            }

            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
