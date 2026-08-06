using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PhantomVault.UI.Converters
{
    /// <summary>
    /// Produces the frosted-glass fill for category tiles. Inputs: [0] tile colour hex,
    /// [1] bool UseColouredTileBlur. ConverterParameter selects the surface variant:
    ///   (none)/"tile" → the tile background, "icon" → the icon circle, "darker" → the
    ///   name-input/close-button surface (a slightly darker shade).
    /// When coloured blur is OFF the tiles get a subtle blue colour-correction instead of a
    /// washed-out neutral white, so they read as deliberate glass rather than grey haze.
    /// </summary>
    public class ColouredBlurBrushConverter : IMultiValueConverter
    {
        // Blue-tinted neutral glass used when coloured-blur is disabled.
        private static readonly Color NeutralBlue = Color.FromRgb(0x5C, 0x9E, 0xDC);
        private static readonly Color NeutralBlueDark = Color.FromRgb(0x2C, 0x4E, 0x76);

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var variant = (parameter as string ?? "tile").Trim().ToLowerInvariant();
            var useColoured = values.Count > 1 && values[1] is bool b && b;

            if (useColoured && values.Count > 0 && values[0] is string hex && !string.IsNullOrWhiteSpace(hex))
            {
                try
                {
                    var c = Color.Parse(hex.Trim());
                    return variant switch
                    {
                        // Stronger tint than before so coloured tiles read as frostier glass.
                        "icon" => Solid(c, 0x6E),
                        "darker" => Solid(Darken(c, 0.62), 0x8A),
                        _ => Solid(c, 0x52),
                    };
                }
                catch
                {
                }
            }

            return variant switch
            {
                "icon" => Solid(NeutralBlue, 0x3C),
                "darker" => Solid(NeutralBlueDark, 0x4C),
                _ => Solid(NeutralBlue, 0x2A),
            };
        }

        private static SolidColorBrush Solid(Color c, byte alpha)
            => new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));

        private static Color Darken(Color c, double factor)
            => Color.FromRgb(
                (byte)Math.Clamp(c.R * factor, 0, 255),
                (byte)Math.Clamp(c.G * factor, 0, 255),
                (byte)Math.Clamp(c.B * factor, 0, 255));
    }
}
