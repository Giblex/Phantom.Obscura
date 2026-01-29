using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;

namespace PhantomVault.UI.Converters
{
    public class IconPathExistsConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool exists = false;

            if (value is string s && !string.IsNullOrWhiteSpace(s))
            {
                exists = File.Exists(s);
            }

            // Check if we should invert the result
            if (parameter is string param && param.Equals("invert", StringComparison.OrdinalIgnoreCase))
            {
                return !exists;
            }

            return exists;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
