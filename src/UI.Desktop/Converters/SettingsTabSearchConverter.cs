using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PhantomVault.UI.Converters
{
    /// <summary>
    /// Filters a settings tab button by the live search query. The bound value is the
    /// query text; ConverterParameter is a space/keyword string describing the tab.
    /// Returns true (visible) when the query is empty or matches any keyword.
    /// </summary>
    public sealed class SettingsTabSearchConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var query = (value as string)?.Trim();
            if (string.IsNullOrEmpty(query))
            {
                return true;
            }

            var keywords = parameter as string ?? string.Empty;
            return keywords.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
