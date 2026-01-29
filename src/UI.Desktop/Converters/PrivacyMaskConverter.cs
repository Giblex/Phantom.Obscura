using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PhantomVault.UI.Converters
{
    public sealed class PrivacyMaskConverter : IMultiValueConverter
    {
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var original = values.Count > 0 ? values[0] : null;
            var privacyFlag = values.Count > 1 && values[1] is bool enabled && enabled;

            if (original is string str)
            {
                return str;
            }

            return original?.ToString() ?? string.Empty;
        }

        public object ConvertBack(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
