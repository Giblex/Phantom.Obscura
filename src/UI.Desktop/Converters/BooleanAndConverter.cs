using System;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace PhantomVault.UI.Converters
{

    public sealed class BooleanAndConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values == null || values.Count == 0)
                return false;

            return values.All(v => v is bool b && b);
        }
    }
}

