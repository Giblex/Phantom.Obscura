using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PhantomVault.UI.Converters
{
    /// <summary>
    /// Converts a hex color string to a slightly darker SolidColorBrush.
    /// If input is null/invalid, returns Transparent.
    /// DarkenFactor can be provided via parameter as a double (0..1), default 0.85.
    /// </summary>
    public sealed class HexToDarkerBrushConverter : IValueConverter
    {
        public static readonly HexToDarkerBrushConverter Instance = new HexToDarkerBrushConverter();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string s && !string.IsNullOrWhiteSpace(s))
            {
                try
                {
                    var color = Color.Parse(s.Trim());
                    double factor = 0.85; // default
                    if (parameter is string ps && double.TryParse(ps, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                        factor = Math.Clamp(f, 0.0, 1.0);
                    byte dr = (byte)Math.Round(color.R * factor);
                    byte dg = (byte)Math.Round(color.G * factor);
                    byte db = (byte)Math.Round(color.B * factor);
                    var darker = Color.FromArgb(color.A, dr, dg, db);
                    return new SolidColorBrush(darker);
                }
                catch { }
            }
            return Brushes.Transparent;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => null;
    }
}

