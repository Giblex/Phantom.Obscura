using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace PhantomVault.UI.Converters
{
    public sealed class BoolToGridLengthConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool on = value is bool b && b;
            var param = (parameter as string) ?? "1*|0";
            var parts = param.Split('|');
            var whenTrue = parts.Length > 0 ? parts[0] : "1*";
            var whenFalse = parts.Length > 1 ? parts[1] : "0";
            var token = on ? whenTrue : whenFalse;
            return ParseGridLength(token);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return AvaloniaProperty.UnsetValue;
        }

        private static GridLength ParseGridLength(string s)
        {
            s = s.Trim();
            if (s.Equals("Auto", StringComparison.OrdinalIgnoreCase))
                return GridLength.Auto;

            if (s.EndsWith("*", StringComparison.Ordinal))
            {
                var num = s.Substring(0, s.Length - 1);
                if (double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var star))
                {
                    return new GridLength(star, GridUnitType.Star);
                }
                return new GridLength(1, GridUnitType.Star);
            }
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var px))
            {
                return new GridLength(px, GridUnitType.Pixel);
            }
            return GridLength.Auto;
        }
    }
}
