using System;
using Avalonia.Data.Converters;
using System.Globalization;

namespace PhantomVault.UI.Converters
{
    /// <summary>
    /// Converter that maps a tile width to sensible button sizes for responsive UI.
    /// Parameter can be "copy" (returns width for the primary copy button) or "icon" (returns size for icon/action buttons).
    /// </summary>
    public class WidthToButtonSizeConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                if (value == null) return 64.0;
                double width = 0.0;
                if (value is double d) width = d;
                else if (!double.TryParse(value.ToString(), out width)) width = 260.0;

                var param = (parameter ?? string.Empty).ToString()?.ToLowerInvariant() ?? string.Empty;

                // Small tile sizes
                if (width <= 200)
                {
                    return param == "copy" ? 48.0 : 26.0;
                }

                // Medium
                if (width <= 300)
                {
                    return param == "copy" ? 64.0 : 30.0;
                }

                // Large
                return param == "copy" ? 86.0 : 34.0;
            }
            catch
            {
                return 64.0;
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
