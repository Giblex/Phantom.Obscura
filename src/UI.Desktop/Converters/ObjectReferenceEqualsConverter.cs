using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PhantomVault.UI.Converters
{
    /// <summary>
    /// Returns true when two bound objects reference the same instance.
    /// </summary>
    public sealed class ObjectReferenceEqualsConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values == null || values.Count < 2)
            {
                return false;
            }

            var left = values[0];
            var right = values[1];
            return ReferenceEquals(left, right);
        }
    }
}
