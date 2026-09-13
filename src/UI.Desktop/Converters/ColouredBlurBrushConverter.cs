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
    /// When coloured blur is OFF the tiles take Obscura's navy instead of a washed-out
    /// neutral white, so they read as deliberate glass in the app's own palette.
    /// </summary>
    public class ColouredBlurBrushConverter : IMultiValueConverter
    {
        // Navy glass used when coloured-blur is disabled. The base is Color.Navy400 (#24384F)
        // lifted slightly so tiles still separate from the #0A0F18-#162235 page behind them;
        // the inset surfaces use Color.Navy600 (#162235), the header navy. The previous
        // #5C9EDC was a bright sky blue that sat outside the Glass Navy palette.
        private static readonly Color NeutralBlue = Color.FromRgb(0x2A, 0x42, 0x62);
        private static readonly Color NeutralBlueDark = Color.FromRgb(0x16, 0x22, 0x35);

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
                        // Tiles are near-solid: at 0x52 the dot grid and page showed through
                        // enough to make rows look washed out. The "darker" surfaces (name box,
                        // close button) rise with them so they still read as inset fields.
                        "icon" => Solid(c, 0x6E),
                        "darker" => Solid(Darken(c, 0.62), 0xC8),
                        _ => Solid(c, 0xB8),
                    };
                }
                catch
                {
                }
            }

            return variant switch
            {
                "icon" => Solid(NeutralBlue, 0x3C),
                "darker" => Solid(NeutralBlueDark, 0xB0),
                _ => Solid(NeutralBlue, 0x9C),
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
