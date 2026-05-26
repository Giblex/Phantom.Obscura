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
    /// <summary>
    /// Manages application theme switching and system theme detection
    /// </summary>
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

        /// <summary>
        /// Set the application theme
        /// </summary>
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
            // Clamp to sane bounds then try to apply to every open window.
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
                // Prefer explicit render scaling override if the platform exposes it.
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

                // Fallback: scale window content to give the user visible feedback even if platform scaling is unavailable.
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
        }

        /// <summary>
        /// Propagate the "show only colour bar on categories" preference into
        /// the app-wide resource dictionary so XAML can DynamicResource-bind to
        /// it. The sidebar consumes this via VaultViewModel.ShowCategoryColorBarOnly.
        /// </summary>
        public void SetShowCategoryColorBarOnly(bool value)
        {
            if (Application.Current == null) return;
            Application.Current.Resources["ShowCategoryColorBarOnly"] = value;
        }

        /// <summary>
        /// Apply the specified theme to the application
        /// </summary>
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
                // Remove existing PhantomTheme and HighContrast overlays
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

                // Get the RuntimeThemeService to coordinate window-level theme dictionaries
                IRuntimeThemeService? runtimeThemeService = null;
                if (app is App phantomApp && phantomApp.Services != null)
                {
                    runtimeThemeService = phantomApp.Services.GetService(typeof(IRuntimeThemeService)) as IRuntimeThemeService;
                }

                // Avalonia 11.0+ uses RequestedThemeVariant property
                string? phantomThemeUri = null;

                switch (theme)
                {
                    case AppTheme.Light:
                        app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
                        // Load the light theme for base Style selectors.
                        // Runtime theme (per-window) provides the actual color tokens.
                        phantomThemeUri = PHANTOM_THEME_LIGHT;
                        Log($"Set RequestedThemeVariant to Light — runtime theme stays active");
                        break;

                    case AppTheme.Dark:
                        app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
                        phantomThemeUri = PHANTOM_THEME_DARK;
                        Log($"Set RequestedThemeVariant to Dark — runtime theme stays active");
                        break;

                    case AppTheme.HighContrast:
                        // Use Dark as base with both dark PhantomTheme and high contrast overlay
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

                // Add the appropriate PhantomTheme overlay
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

                // Add HighContrast overlay if needed
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

                // Force UI refresh by invalidating all windows
                if (app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    foreach (var window in desktop.Windows)
                    {
                        window.InvalidateVisual();
                    }
                    Log("Forced UI refresh on all windows");
                }

                // Log active theme variant and key brushes for debugging
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

        /// <summary>
        /// Detect the system's preferred theme
        /// </summary>
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

            return AppTheme.Dark; // Default fallback
        }

        /// <summary>
        /// Detect Windows theme preference from registry
        /// </summary>
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
                // Ignore errors, fall back to default
            }

            return AppTheme.Dark;
        }

        /// <summary>
        /// Detect macOS theme preference (simplified)
        /// </summary>
        private AppTheme DetectMacOSTheme()
        {
            // In a full implementation, this would use NSUserDefaults or AppleScript
            // For now, default to dark theme
            return AppTheme.Dark;
        }

        /// <summary>
        /// Detect Linux theme preference (simplified)
        /// </summary>
        private AppTheme DetectLinuxTheme()
        {
            // In a full implementation, this would check GTK settings or desktop environment
            // For now, default to dark theme
            return AppTheme.Dark;
        }

        /// <summary>
        /// Check if high contrast mode is enabled in the OS
        /// </summary>
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
                    // Ignore errors
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
                // ignore file I/O errors for logging
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

            // Also try locating these keys inside loaded StyleIncludes (e.g., PhantomTheme.* files)
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
