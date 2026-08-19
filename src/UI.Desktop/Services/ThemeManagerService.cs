using System;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;

namespace PhantomVault.UI.Services
{

    public enum AppTheme
    {
        Light,
        Dark,
        HighContrast,
        System
    }

    public class ThemeManagerService
    {
        private const string PHANTOM_THEME_LIGHT = "avares://PhantomVault.UI/Assets/Themes/PhantomTheme.Light.axaml";
        private const string PHANTOM_THEME_DARK = "avares://PhantomVault.UI/Assets/Themes/PhantomTheme.Dark.axaml";
        private const string HIGH_CONTRAST_THEME = "avares://PhantomVault.UI/Assets/Themes/PhantomTheme.HighContrast.axaml";
        private static readonly string LogFilePath = Path.Combine(AppContext.BaseDirectory, "run_log.txt");
        private readonly string[] _skinUris =
        {
            "avares://PhantomVault.UI/Assets/Themes/Accents/Accent.Default.axaml",
            "avares://PhantomVault.UI/Assets/Themes/Accents/Accent.CharcoalBlue.axaml",
            "avares://PhantomVault.UI/Assets/Themes/Accents/Accent.Pastel.axaml"
        };

        public AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

        public event EventHandler<AppTheme>? ThemeChanged;

        public void SetTheme(AppTheme theme)
        {
            if (theme == AppTheme.System)
            {
                theme = DetectSystemTheme();
            }

            CurrentTheme = theme;
            ApplyTheme(theme);
            ThemeChanged?.Invoke(this, theme);
        }

        public void SetRenderScale(double scale)
        {

            scale = Math.Clamp(scale, 0.8, 1.5);
            if (Application.Current != null)
            {
                Application.Current.Resources["UserRenderScale"] = scale;
            }

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                foreach (var window in desktop.Windows.ToList())
                {
                    TryApplyRenderScale(window, scale);
                }
            }
        }

        private static void TryApplyRenderScale(Window window, double scale)
        {
            try
            {

                var prop = window.GetType().GetProperty("RenderScalingOverride", BindingFlags.Instance | BindingFlags.Public);
                prop ??= window.GetType().GetProperty("RenderScaling", BindingFlags.Instance | BindingFlags.Public);
                if (prop?.CanWrite == true)
                {
                    prop.SetValue(window, scale);
                    return;
                }

                var method = window.GetType().GetMethod("SetRenderScaling", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method != null)
                {
                    method.Invoke(window, new object[] { scale });
                    return;
                }

                if (window.Content is Control control)
                {
                    control.RenderTransform = new ScaleTransform(scale, scale);
                }
            }
            catch (Exception ex)
            {
                Log($"Render scale apply failed: {ex.Message}");
            }
        }

        public void SetSkin(int skinIndex)
        {
            var skinUri = _skinUris[Math.Clamp(skinIndex, 0, _skinUris.Length - 1)];
            var app = Application.Current;
            if (app == null) return;

            var toRemove = new System.Collections.Generic.List<StyleInclude>();
            for (int i = 0; i < app.Styles.Count; i++)
            {
                if (app.Styles[i] is StyleInclude si && si.Source != null && si.Source.OriginalString.Contains("Assets/Themes/Accents/Accent"))
                {
                    toRemove.Add(si);
                }
            }
            foreach (var rem in toRemove)
            {
                app.Styles.Remove(rem);
            }

            var include = new StyleInclude(new Uri("avares://PhantomVault.UI/")) { Source = new Uri(skinUri) };
            app.Styles.Add(include);
        }

        public void SetEffects(bool reduceAnimations, bool reduceTransparency)
        {
            if (Application.Current == null) return;
            Application.Current.Resources["ReduceAnimations"] = reduceAnimations;
            Application.Current.Resources["ReduceTransparency"] = reduceTransparency;

            // Bridge the persisted "Reduce Animations" setting into the runtime
            // AccessibilityService, which is the single source the animation behaviors
            // (Hover/Scale/LiquidGlass/SmoothScroll/LoadingSpinner) actually read. Without
            // this, the toggle only flipped a resource flag and animations kept running.
            AccessibilityService.Instance.ReduceMotion = reduceAnimations;

            // Same bridge for "Reduce Transparency". Setting the resource above is not
            // enough on its own — nothing binds it. AccessibilityService raises
            // SettingsChanged, which makes every ThemeAwareWindow re-apply its
            // "reduce-transparency" class, and the App.axaml styles under that class
            // swap the translucent glass fills for opaque ones.
            AccessibilityService.Instance.ReduceTransparency = reduceTransparency;
        }

        public void SetAppFont(string family)
        {
            if (Application.Current == null || string.IsNullOrWhiteSpace(family)) return;
            try
            {
                var ff = new FontFamily(family);
                Application.Current.Resources["AppFontFamily"] = ff;
                if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    foreach (var window in desktop.Windows.ToList())
                        window.FontFamily = ff;
                }
            }
            catch (Exception ex) { Log($"SetAppFont failed: {ex.Message}"); }
        }

        public void SetAppFontSize(double size)
        {
            if (Application.Current == null) return;
            size = Math.Clamp(size, 10.0, 22.0);
            Application.Current.Resources["AppFontSize"] = size;
            if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                foreach (var window in desktop.Windows.ToList())
                    window.FontSize = size;
            }
        }

        /// <summary>Accent used when the persisted value is missing or unparseable.</summary>
        private const string FallbackAccentHex = "#5A7AB0";

        /// <summary>
        /// Last accent applied by <see cref="SetAccentColor"/>.
        ///
        /// Windows opened after startup are not covered by the per-window stamping below,
        /// and UserAccentBrush is deliberately absent from every theme file, so a window
        /// created later had to rely on Application.Resources resolving — which did not
        /// hold in practice for controls inside a themed window, leaving the brush null.
        /// ThemeAwareWindow reads this on open and stamps its own Resources instead.
        /// </summary>
        public static (Color Color, IBrush Brush)? CurrentAccent { get; private set; }

        public void SetAccentColor(string hex)
        {
            if (Application.Current == null) return;
            try
            {
                // UserAccentBrush/UserAccentColor are deliberately defined in no theme
                // file (see below), which means this method is their ONLY source. Bailing
                // out on a blank or unparseable value therefore left them undefined, and
                // every consumer resolved to a null brush — invisible rather than wrong,
                // which is how a checked checkbox rendered as a bare tick with no box.
                // Fall back to a known-good accent instead of returning early.
                Color color;
                if (string.IsNullOrWhiteSpace(hex) || !Color.TryParse(hex, out color))
                {
                    Log($"SetAccentColor: '{hex}' unusable, falling back to {FallbackAccentHex}");
                    color = Color.Parse(FallbackAccentHex);
                }
                var brush = new SolidColorBrush(color);
                CurrentAccent = (color, brush);
                Application.Current.Resources["AccentColor"] = color;
                Application.Current.Resources["AccentBrush"] = brush;
                Application.Current.Resources["Brush.Accent"] = brush;

                // Dedicated user-accent resources for surfaces that MUST track the user's
                // picked colour even when a runtime theme overlay defines its own AccentBrush
                // (theme overlays' Style.Resources override Application.Resources for
                // AccentBrush, which silently undoes the user's choice — these keys aren't
                // defined by any theme file so they always reflect SetAccentColor's input).
                Application.Current.Resources["UserAccentColor"] = color;
                Application.Current.Resources["UserAccentBrush"] = brush;

                // Also stamp the brush onto every open Window's Resources so any control
                // binding to AccentBrush via DynamicResource still resolves to the user's
                // choice (Window.Resources beats Style.Resources from app-level themes).
                if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    foreach (var window in desktop.Windows.ToList())
                    {
                        window.Resources["AccentBrush"] = brush;
                        window.Resources["AccentColor"] = color;
                        window.Resources["UserAccentBrush"] = brush;
                        window.Resources["UserAccentColor"] = color;
                    }
                }
            }
            catch (Exception ex) { Log($"SetAccentColor failed: {ex.Message}"); }
        }

        public void SetShowCategoryColorBarOnly(bool value)
        {
            if (Application.Current == null) return;
            Application.Current.Resources["ShowCategoryColorBarOnly"] = value;
        }

        public void SetFlatButtons(bool enabled)
        {
            if (Application.Current == null) return;
            Application.Current.Resources["UseFlatButtons"] = enabled;
            if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                foreach (var window in desktop.Windows.ToList())
                {
                    ApplyWindowClass(window, "flat-buttons", enabled);
                }
            }
        }

        public void SetFlatButtonBorders(bool enabled)
        {
            if (Application.Current == null) return;
            Application.Current.Resources["UseFlatButtonBorders"] = enabled;
            if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                foreach (var window in desktop.Windows.ToList())
                {
                    ApplyWindowClass(window, "flat-button-borders", enabled);
                }
            }
        }

        private static void ApplyWindowClass(Window window, string cls, bool enabled)
        {
            if (enabled)
            {
                if (!window.Classes.Contains(cls)) window.Classes.Add(cls);
            }
            else
            {
                window.Classes.Remove(cls);
            }
        }

        private void ApplyTheme(AppTheme theme)
        {
            var app = Application.Current;
            if (app == null)
            {
                Log("ApplyTheme invoked but Application.Current is null");
                return;
            }

            Log($"ApplyTheme({theme}) - Current style count: {app.Styles.Count}");
            DumpStyles(app.Styles, "before theme change");

            try
            {

                var toRemove = app.Styles
                    .OfType<StyleInclude>()
                    .Where(s => s.Source?.OriginalString?.Contains("PhantomTheme") == true ||
                                s.Source?.OriginalString?.Contains("HighContrast") == true)
                    .ToList();

                foreach (var style in toRemove)
                {
                    Log($"Removing style: {style.Source?.OriginalString}");
                    app.Styles.Remove(style);
                }

                Log($"Removed {toRemove.Count} theme overlay(s)");

                IRuntimeThemeService? runtimeThemeService = null;
                if (app is App phantomApp && phantomApp.Services != null)
                {
                    runtimeThemeService = phantomApp.Services.GetService(typeof(IRuntimeThemeService)) as IRuntimeThemeService;
                }

                string? phantomThemeUri = null;

                switch (theme)
                {
                    case AppTheme.Light:
                        app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;

                        phantomThemeUri = PHANTOM_THEME_LIGHT;
                        Log($"Set RequestedThemeVariant to Light — runtime theme stays active");
                        break;

                    case AppTheme.Dark:
                        app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
                        phantomThemeUri = PHANTOM_THEME_DARK;
                        Log($"Set RequestedThemeVariant to Dark — runtime theme stays active");
                        break;

                    case AppTheme.HighContrast:

                        app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
                        phantomThemeUri = PHANTOM_THEME_DARK;
                        Log($"Set RequestedThemeVariant to Dark/HighContrast — runtime theme stays active");
                        break;

                    default:
                        Log($"Unsupported theme: {theme}, defaulting to Dark");
                        app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
                        phantomThemeUri = PHANTOM_THEME_DARK;
                        break;
                }

                if (phantomThemeUri != null)
                {
                    try
                    {
                        Log($"Adding PhantomTheme from {phantomThemeUri}");
                        var phantomThemeStyle = new StyleInclude(new Uri("resm:Styles?assembly=PhantomVault.UI"))
                        {
                            Source = new Uri(phantomThemeUri)
                        };
                        app.Styles.Add(phantomThemeStyle);
                        Log($"PhantomTheme overlay added successfully. Current style count: {app.Styles.Count}");
                        DumpStyles(app.Styles, "after adding PhantomTheme");
                    }
                    catch (Exception ex)
                    {
                        Log($"Failed to load PhantomTheme: {ex.Message}");
                        if (ex.InnerException != null)
                        {
                            Log($"Inner exception: {ex.InnerException.Message}");
                            Log($"Inner exception type: {ex.InnerException.GetType().Name}");
                        }
                        Log($"Stack trace: {ex.StackTrace}");
                    }
                }

                if (theme == AppTheme.HighContrast)
                {
                    try
                    {
                        Log($"Adding HighContrast overlay from {HIGH_CONTRAST_THEME}");
                        var highContrastStyle = new StyleInclude(new Uri("resm:Styles?assembly=PhantomVault.UI"))
                        {
                            Source = new Uri(HIGH_CONTRAST_THEME)
                        };
                        app.Styles.Add(highContrastStyle);
                        Log("HighContrast overlay added");
                    }
                    catch (Exception ex)
                    {
                        Log($"Failed to load high contrast theme: {ex.Message}");
                    }
                }

                if (app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    foreach (var window in desktop.Windows)
                    {
                        window.InvalidateVisual();
                    }
                    Log("Forced UI refresh on all windows");
                }

                Log($"ActualThemeVariant: {app.ActualThemeVariant}");
                DumpActiveBrushes();
            }
            catch (Exception ex)
            {
                Log($"Error applying theme: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Log($"Inner exception: {ex.InnerException.Message}");
                    Log($"Inner exception type: {ex.InnerException.GetType().Name}");
                }
                Log($"Stack trace: {ex.StackTrace}");
            }
        }

        private AppTheme DetectSystemTheme()
        {
            if (OperatingSystem.IsWindows())
            {
                return DetectWindowsTheme();
            }
            else if (OperatingSystem.IsMacOS())
            {
                return DetectMacOSTheme();
            }
            else if (OperatingSystem.IsLinux())
            {
                return DetectLinuxTheme();
            }

            return AppTheme.Dark;
        }

        private AppTheme DetectWindowsTheme()
        {
            try
            {
#if WINDOWS
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

                var value = key?.GetValue("AppsUseLightTheme");
                if (value is int intValue)
                {
                    return intValue == 1 ? AppTheme.Light : AppTheme.Dark;
                }
#endif
            }
            catch
            {

            }

            return AppTheme.Dark;
        }

        private AppTheme DetectMacOSTheme()
        {

            return AppTheme.Dark;
        }

        private AppTheme DetectLinuxTheme()
        {

            return AppTheme.Dark;
        }

        public bool IsSystemHighContrastEnabled()
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
#if WINDOWS
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Themes");

                    var value = key?.GetValue("HighContrast");
                    return value?.ToString()?.Contains("High Contrast") == true;
#endif
                }
                catch
                {

                }
            }

            return false;
        }

        private static void Log(string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [ThemeManager] {message}";
            Debug.WriteLine(line);
            Console.WriteLine(line);
            try
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
            catch
            {

            }
        }

        private static void DumpStyles(Styles styles, string context)
        {
            if (styles == null) return;
            Log($"Styles snapshot ({context}): {styles.Count} entries");
            for (int i = 0; i < styles.Count; i++)
            {
                Log($"  [{i}] {DescribeStyle(styles[i])}");
            }
        }

        private static string DescribeStyle(IStyle? style)
        {
            if (style is StyleInclude include && include.Source != null)
            {
                return $"StyleInclude -> {include.Source}";
            }

            return style?.GetType().Name ?? "<null>";
        }

        private static void DumpActiveBrushes()
        {
            var app = Application.Current;
            if (app == null)
            {
                Log("DumpActiveBrushes skipped: Application.Current is null");
                return;
            }

            var tv = app.ActualThemeVariant;
            Log("Dumping active brush resources...");
            DumpBrush("TextSelectionBrush", tv);
            DumpBrush("TextCaretBrush", tv);
            DumpBrush("QuickFilterGlassHoverBrush", tv);
            DumpBrush("QuickFilterGlassActiveBrush", tv);
            DumpBrush("QuickFilterGlassActiveHoverBrush", tv);
            DumpBrush("LiquidGlassButtonForeground", tv);
            DumpBrush("InputForegroundBrush", tv);

            foreach (var include in app.Styles.OfType<StyleInclude>())
            {
                var src = include.Source?.OriginalString ?? "<null>";
                if (src.Contains("PhantomTheme") || src.Contains("/Themes/Theme."))
                {
                    Log($"Inspecting resources within StyleInclude: {src}");
                    var loaded = include.Loaded;
                    if (loaded is Styles styles)
                    {
                        for (int i = 0; i < styles.Count; i++)
                        {
                            if (styles[i] is Style style)
                            {
                                DumpStyleResources(style, tv);
                            }
                        }
                    }
                    else if (loaded is Style style)
                    {
                        DumpStyleResources(style, tv);
                    }
                }
            }
        }

        private static void DumpStyleResources(Style style, ThemeVariant? tv)
        {
            if (style?.Resources == null) return;
            Log("  Style.Resources snapshot:");
            TryDumpResource(style.Resources, "TextSelectionBrush", tv);
            TryDumpResource(style.Resources, "TextCaretBrush", tv);
            TryDumpResource(style.Resources, "QuickFilterGlassHoverBrush", tv);
            TryDumpResource(style.Resources, "QuickFilterGlassActiveBrush", tv);
            TryDumpResource(style.Resources, "QuickFilterGlassActiveHoverBrush", tv);
            TryDumpResource(style.Resources, "LiquidGlassButtonForeground", tv);
            TryDumpResource(style.Resources, "InputForegroundBrush", tv);
        }

        private static void TryDumpResource(IResourceDictionary dict, string key, ThemeVariant? tv)
        {
            if (dict.TryGetResource(key, tv, out var value))
            {
                switch (value)
                {
                    case SolidColorBrush scb:
                        var c = scb.Color;
                        Log($"    {key}: SolidColorBrush ARGB=#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}");
                        break;
                    case LinearGradientBrush lgb:
                        var stops = string.Join(", ", lgb.GradientStops.Select(s => $"#{s.Color.A:X2}{s.Color.R:X2}{s.Color.G:X2}{s.Color.B:X2}@{s.Offset:0.00}"));
                        Log($"    {key}: LinearGradientBrush Stops=[{stops}] Start={lgb.StartPoint} End={lgb.EndPoint}");
                        break;
                    case Brush b:
                        Log($"    {key}: Brush type={b.GetType().Name}");
                        break;
                    default:
                        Log($"    {key}: {value?.GetType().Name ?? "<null>"}");
                        break;
                }
            }
            else
            {
                Log($"    {key}: <missing>");
            }
        }

        private static void DumpBrush(string key, ThemeVariant? tv)
        {
            var app = Application.Current;
            if (app == null) return;

            if (app.Resources.TryGetResource(key, tv, out var value))
            {
                switch (value)
                {
                    case SolidColorBrush scb:
                        var c = scb.Color;
                        Log($"{key}: SolidColorBrush ARGB=#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}");
                        break;
                    case LinearGradientBrush lgb:
                        var stops = string.Join(", ", lgb.GradientStops.Select(s => $"#{s.Color.A:X2}{s.Color.R:X2}{s.Color.G:X2}{s.Color.B:X2}@{s.Offset:0.00}"));
                        Log($"{key}: LinearGradientBrush Stops=[{stops}] Start={lgb.StartPoint} End={lgb.EndPoint}");
                        break;
                    case GradientBrush gb:
                        Log($"{key}: GradientBrush type={gb.GetType().Name}");
                        break;
                    case Brush b:
                        Log($"{key}: Brush type={b.GetType().Name}");
                        break;
                    default:
                        Log($"{key}: {value?.GetType().Name ?? "<null>"}");
                        break;
                }
            }
            else
            {
                Log($"{key}: <missing>");
            }
        }
    }
}

