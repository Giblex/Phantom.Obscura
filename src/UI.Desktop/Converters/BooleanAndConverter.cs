using System;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace PhantomVault.UI.Converters
{
    /// <summary>
    /// Multi-value converter that returns true only if all input boolean values are true.
    /// </summary>
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
