using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PhantomVault.UI.Converters
{
    public class InvertBoolConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return value == null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return value == null;
        }
    }
}

