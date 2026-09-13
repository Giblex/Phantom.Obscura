using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace PhantomVault.UI.Converters
{
    /// <summary>
    /// Makes category colours hold up on light themes. Category colours are mostly pastels
    /// (#E5CCFF, #C7E5FF...) chosen for the dark page; on a near-white card they almost vanish.
    /// In light mode they keep their hue but are pulled darker and more saturated.
    /// </summary>
    public static class CategoryColours
    {
        /// <summary>
        /// Light when the window background is actually light. Palettes such as Giblex Website
        /// are light while the app's Light/Dark switch still says Dark, so the variant alone
        /// is not reliable.
        /// </summary>
        public static bool IsLightTheme
        {
            get
            {
                // Category headers are built while filtering, which can run off the UI thread;
                // window resources may only be read on it. Off-thread, reuse the last answer.
                if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                    return _lastIsLight;

                _lastIsLight = ComputeIsLight();
                return _lastIsLight;
            }
        }

        private static volatile bool _lastIsLight;

        private static bool ComputeIsLight()
        {
            {
                var app = Application.Current;
                if (app == null) return false;

                // Palettes are applied to each window's own resources, so ask the main window
                // first; the app-level brush is the base theme's, not the palette's.
                var mainWindow = (app.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                object? res = null;
                var found = mainWindow != null
                    ? mainWindow.TryGetResource("WindowBackgroundBrush", mainWindow.ActualThemeVariant, out res)
                    : app.TryGetResource("WindowBackgroundBrush", app.ActualThemeVariant, out res);

                if (found)
                {
                    Color? c = res switch
                    {
                        ISolidColorBrush solid => solid.Color,
                        IGradientBrush { GradientStops.Count: > 0 } g => g.GradientStops[0].Color,
                        _ => null
                    };
                    if (c is { } color)
                        return (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0 > 0.5;
                }

                return app.ActualThemeVariant == ThemeVariant.Light;
            }
        }

        /// <summary>The colour as it should be drawn on the current theme's surfaces (bars, borders, accents).</summary>
        public static Color ForSurface(Color color) =>
            IsLightTheme ? WithLightness(color, maxLightness: 0.55, minSaturation: 0.55) : color;

        /// <summary>A dark shade of the hue, readable as text on a light tint of the same colour.</summary>
        public static Color TextOnLightTint(Color color) =>
            WithLightness(color, maxLightness: 0.28, minSaturation: 0.45);

        private static Color WithLightness(Color color, double maxLightness, double minSaturation)
        {
            var hsl = color.ToHsl();
            // Near-greys (the uncoloured default) stay grey rather than picking up a hue.
            var saturation = hsl.S < 0.08 ? hsl.S : Math.Max(hsl.S, minSaturation);
            var lightness = Math.Min(hsl.L, maxLightness);
            var result = HslColor.ToRgb(hsl.H, saturation, lightness, 1.0);
            return Color.FromArgb(color.A, result.R, result.G, result.B);
        }
    }
}
