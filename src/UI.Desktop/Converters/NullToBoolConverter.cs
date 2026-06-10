using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PhantomVault.UI.Converters
{

    public sealed class NullToBoolConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var notNull = value is not null;
            return Invert ? !notNull : notNull;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}

