using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PhantomVault.UI.Converters
{
    /// <summary>
    /// Returns true when the bound string matches the converter parameter
    /// (ordinal comparison). Treats null and empty as equivalent.
    /// Used to drive single-selection visual state from a single string
    /// property without needing one boolean per option.
    /// </summary>
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
            // One-way only; settings tab selection is driven from the VM.
            throw new NotSupportedException();
        }
    }
}
