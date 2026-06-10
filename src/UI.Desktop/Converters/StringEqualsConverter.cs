using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PhantomVault.UI.Converters
{

    public sealed class StringEqualsConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var left = value as string ?? string.Empty;
            var right = parameter as string ?? string.Empty;
            return string.Equals(left, right, StringComparison.Ordinal);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {

            throw new NotSupportedException();
        }
    }
}

