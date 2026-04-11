using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Describes an available theme.
    /// </summary>
    public sealed class ThemeDescriptor
    {
        public string Id { get; }
        public string DisplayName { get; }
        public Uri Uri { get; }

        public ThemeDescriptor(string id, string displayName, Uri uri)
        {
            Id = id;
            DisplayName = displayName;
            Uri = uri;
        }
    }

    /// <summary>
    /// Interface for runtime theme service.
    /// </summary>
    public interface IRuntimeThemeService
    {
        /// <summary>
        /// Gets all available themes.
        /// </summary>
        IReadOnlyList<ThemeDescriptor> GetThemes();

        /// <summary>
        /// Gets the current theme ID.
        /// </summary>
        string CurrentThemeId { get; }

        /// <summary>
        /// Applies a theme by ID to all themed windows.
        /// </summary>
        void Apply(string themeId);

        /// <summary>
        /// Applies the current theme to a specific window (called when window opens).
        /// Only applies to windows with ThemeScope.IsThemed = true.
        /// </summary>
        void ApplyToWindow(Window window);

        /// <summary>
        /// Suspends window-level runtime theme dictionaries so that the app-level
        /// light theme can take effect. Called when switching to Light mode.
        /// </summary>
        void SuspendForLightMode();

        /// <summary>
        /// Re-applies window-level runtime theme dictionaries.
        /// Called when switching back to Dark mode.
        /// </summary>
        void ResumeForDarkMode();

        /// <summary>
        /// Whether the runtime theme is currently suspended (light mode active).
        /// </summary>
        bool IsSuspended { get; }

        /// <summary>
        /// Event raised when theme changes.
        /// </summary>
        event EventHandler<string>? ThemeChanged;

        /// <summary>
        /// Reloads custom themes from the user's custom themes directory.
        /// </summary>
        void LoadCustomThemes();

        /// <summary>
        /// Removes a custom theme by ID and optionally deletes the file.
        /// </summary>
        bool RemoveCustomTheme(string themeId);
    }

    /// <summary>
    /// Runtime theme service that applies themes per-window via Window.Resources.MergedDictionaries.
    /// Only windows with ThemeScope.IsThemed=true receive theme updates.
    /// Setup/Welcome windows with IsThemed=false use the base app theme and are unaffected.
    /// </summary>
    public sealed class RuntimeThemeService : IRuntimeThemeService
    {
        private readonly List<ThemeDescriptor> _themes;
        private readonly Dictionary<Window, ResourceInclude> _windowThemes = new();
        private readonly Dictionary<Window, Avalonia.Controls.ResourceDictionary> _windowCustomDicts = new();
        private string _currentThemeId;
        private bool _isSuspended;

        public event EventHandler<string>? ThemeChanged;

        /// <summary>
        /// Whether the runtime theme is currently suspended (light mode active).
        /// When suspended, window-level dark theme dictionaries are removed so the
        /// app-level PhantomTheme.Light tokens take effect.
        /// </summary>
        public bool IsSuspended => _isSuspended;

        public RuntimeThemeService()
        {
            _themes = new List<ThemeDescriptor>
            {
                new ThemeDescriptor(
                    "ClassicDark",
                    "Classic Dark",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.ClassicDark.axaml")),
                new ThemeDescriptor(
                    "GiblexGlassNavy",
                    "Giblex Glass Navy",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.GiblexGlassNavy.axaml")),
                new ThemeDescriptor(
                    "GiblexDark",
                    "Giblex Dark",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.GiblexDark.axaml")),
                new ThemeDescriptor(
                    "GiblexWebsite",
                    "Giblex Light",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.GiblexWebsite.axaml")),
                new ThemeDescriptor(
                    "ClassicLight",
                    "Classic Light",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.ClassicLight.axaml")),
                new ThemeDescriptor(
                    "CharcoalPastel",
                    "Charcoal Pastel",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.CharcoalPastel.axaml")),
                new ThemeDescriptor(
                    "Natural",
                    "Natural",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.Natural.axaml")),
                new ThemeDescriptor(
                    "ModernSystem",
                    "Modern System",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.ModernSystem.axaml")),
                new ThemeDescriptor(
                    "Proton",
                    "Proton",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.Proton.axaml")),
                new ThemeDescriptor(
                    "MidnightNeon",
                    "Midnight Neon",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.MidnightNeon.axaml")),
                new ThemeDescriptor(
                    "ArcticFrost",
                    "Arctic Frost",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.ArcticFrost.axaml")),
                new ThemeDescriptor(
                    "SunsetEmber",
                    "Sunset Ember",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.SunsetEmber.axaml")),
                new ThemeDescriptor(
                    "Cyberpunk",
                    "Cyberpunk",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.Cyberpunk.axaml")),
            };

            _currentThemeId = "GiblexWebsite";

            // Discover and register custom themes from disk
            LoadCustomThemes();
        }

        /// <summary>
        /// Loads custom themes from the user's custom themes directory.
        /// </summary>
        public void LoadCustomThemes()
        {
            // Remove any previously loaded custom themes
            _themes.RemoveAll(t => t.Id.StartsWith("Custom_"));

            foreach (var (id, displayName, filePath) in CustomThemeGenerator.DiscoverCustomThemes())
            {
                _themes.Add(new ThemeDescriptor(id, $"\u2728 {displayName}", new Uri(filePath)));
            }
        }

        /// <summary>
        /// Registers a new custom theme and optionally applies it.
        /// </summary>
        public void RegisterCustomTheme(string filePath, bool apply = true)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var id = fileName.Replace("Theme.", "");
            var displayName = id.Replace("Custom_", "").Replace("_", " ");

            // Remove existing entry with same ID
            _themes.RemoveAll(t => t.Id == id);
            _themes.Add(new ThemeDescriptor(id, $"\u2728 {displayName}", new Uri(filePath)));

            if (apply)
            {
                Apply(id);
            }
        }

        /// <summary>
        /// Removes a custom theme from the list and deletes its file.
        /// </summary>
        public bool RemoveCustomTheme(string themeId)
        {
            var theme = _themes.FirstOrDefault(t => t.Id == themeId);
            if (theme == null || !themeId.StartsWith("Custom_")) return false;

            _themes.Remove(theme);
            CustomThemeGenerator.DeleteCustomTheme(theme.Uri.LocalPath);

            // If the removed theme was active, switch to Giblex Website
            if (_currentThemeId == themeId)
            {
                Apply("GiblexWebsite");
            }

            return true;
        }

        public IReadOnlyList<ThemeDescriptor> GetThemes() => _themes;

        public string CurrentThemeId => _currentThemeId;

        public void Apply(string themeId)
        {
            var theme = _themes.FirstOrDefault(t => t.Id == themeId);
            if (theme == null)
            {
                System.Diagnostics.Debug.WriteLine($"[RuntimeThemeService] Theme not found: {themeId}");
                return;
            }

            _currentThemeId = themeId;

            // Apply theme to all registered themed windows
            ApplyToAllThemedWindows(theme);

            ThemeChanged?.Invoke(this, themeId);
            System.Diagnostics.Debug.WriteLine($"[RuntimeThemeService] Applied theme: {themeId}");
        }

        private void ApplyToAllThemedWindows(ThemeDescriptor theme)
        {
            // Get all open windows that are themed
            var themedWindows = _windowThemes.Keys.ToList();
            foreach (var window in themedWindows)
            {
                if (ThemeScope.GetIsThemed(window))
                {
                    ApplyThemeToWindow(window, theme);
                }
            }
        }

        private void ApplyThemeToWindow(Window window, ThemeDescriptor theme)
        {
            try
            {
                // Remove previous theme from this window if exists
                if (_windowThemes.TryGetValue(window, out var oldTheme) && oldTheme != null)
                {
                    window.Resources.MergedDictionaries.Remove(oldTheme);
                }
                if (_windowCustomDicts.TryGetValue(window, out var oldCustom))
                {
                    window.Resources.MergedDictionaries.Remove(oldCustom);
                    _windowCustomDicts.Remove(window);
                }

                // When suspended (light mode), don't apply the dark runtime theme.
                if (_isSuspended)
                {
                    _windowThemes[window] = null!;
                    System.Diagnostics.Debug.WriteLine($"[RuntimeThemeService] Suspended — skipping runtime theme for window: {window.Title}");
                    return;
                }

                if (theme.Uri.Scheme == "avares")
                {
                    // Built-in theme — use ResourceInclude
                    var resourceInclude = new ResourceInclude(new Uri("avares://PhantomVault.UI"))
                    {
                        Source = theme.Uri
                    };
                    window.Resources.MergedDictionaries.Add(resourceInclude);
                    _windowThemes[window] = resourceInclude;
                }
                else
                {
                    // Custom theme from filesystem — load XAML directly
                    var xamlContent = File.ReadAllText(theme.Uri.LocalPath);
                    using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xamlContent));
                    var loaded = (Avalonia.Controls.ResourceDictionary)Avalonia.Markup.Xaml.AvaloniaRuntimeXamlLoader.Load(stream);
                    window.Resources.MergedDictionaries.Add(loaded);
                    // Store as null and track via separate dict — ResourceInclude won't work for raw dicts
                    _windowCustomDicts[window] = loaded;
                    _windowThemes[window] = null!;
                }

                System.Diagnostics.Debug.WriteLine($"[RuntimeThemeService] Applied {theme.Id} to window: {window.Title}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RuntimeThemeService] Failed to apply theme to window: {ex.Message}");
            }
        }

        /// <summary>
        /// Suspends window-level runtime theme dictionaries for light mode.
        /// Removes dark theme ResourceDictionaries from all windows so the app-level
        /// PhantomTheme.Light.axaml tokens take effect.
        /// </summary>
        public void SuspendForLightMode()
        {
            if (_isSuspended) return;
            _isSuspended = true;

            // Remove dark theme from all registered windows
            foreach (var kvp in _windowThemes.ToList())
            {
                var window = kvp.Key;
                var theme = kvp.Value;
                if (theme != null)
                {
                    try
                    {
                        window.Resources.MergedDictionaries.Remove(theme);
                        System.Diagnostics.Debug.WriteLine($"[RuntimeThemeService] Suspended dark theme from window: {window.Title}");
                    }
                    catch { /* window may be closing */ }
                }
                if (_windowCustomDicts.TryGetValue(window, out var customDict))
                {
                    try { window.Resources.MergedDictionaries.Remove(customDict); } catch { }
                    _windowCustomDicts.Remove(window);
                }
                _windowThemes[window] = null!;
            }
        }

        /// <summary>
        /// Re-applies window-level runtime theme dictionaries for dark mode.
        /// </summary>
        public void ResumeForDarkMode()
        {
            if (!_isSuspended) return;
            _isSuspended = false;

            // Re-apply the current theme to all registered themed windows
            var theme = _themes.FirstOrDefault(t => t.Id == _currentThemeId);
            if (theme != null)
            {
                ApplyToAllThemedWindows(theme);
            }
        }

        public void ApplyToWindow(Window window)
        {
            // Only apply to windows with IsThemed = true
            if (!ThemeScope.GetIsThemed(window))
            {
                System.Diagnostics.Debug.WriteLine($"[RuntimeThemeService] Skipping unthemed window: {window.Title}");
                return;
            }

            var theme = _themes.FirstOrDefault(t => t.Id == _currentThemeId);
            if (theme != null)
            {
                ApplyThemeToWindow(window, theme);
            }
        }

        /// <summary>
        /// Registers a window for theme tracking. Called by ThemeAwareWindow.
        /// </summary>
        public void RegisterWindow(Window window)
        {
            if (!_windowThemes.ContainsKey(window))
            {
                _windowThemes[window] = null!;
                window.Closed += (_, _) => UnregisterWindow(window);
            }
        }

        /// <summary>
        /// Unregisters a window from theme tracking.
        /// </summary>
        public void UnregisterWindow(Window window)
        {
            if (_windowThemes.TryGetValue(window, out var theme))
            {
                if (theme != null)
                {
                    window.Resources.MergedDictionaries.Remove(theme);
                }
                _windowThemes.Remove(window);
            }
            if (_windowCustomDicts.TryGetValue(window, out var customDict))
            {
                window.Resources.MergedDictionaries.Remove(customDict);
                _windowCustomDicts.Remove(window);
            }
        }
    }
}
