using System;
using System.Collections.Generic;
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
        /// Event raised when theme changes.
        /// </summary>
        event EventHandler<string>? ThemeChanged;
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
        private string _currentThemeId;

        public event EventHandler<string>? ThemeChanged;

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
            };

            _currentThemeId = "ClassicDark";
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
                if (_windowThemes.TryGetValue(window, out var oldTheme))
                {
                    window.Resources.MergedDictionaries.Remove(oldTheme);
                }

                // Create new ResourceInclude for the theme
                var resourceInclude = new ResourceInclude(new Uri("avares://PhantomVault.UI"))
                {
                    Source = theme.Uri
                };

                // Add to window's MergedDictionaries (window resources take precedence over app resources)
                window.Resources.MergedDictionaries.Add(resourceInclude);
                _windowThemes[window] = resourceInclude;

                System.Diagnostics.Debug.WriteLine($"[RuntimeThemeService] Applied {theme.Id} to window: {window.Title}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RuntimeThemeService] Failed to apply theme to window: {ex.Message}");
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
        }
    }
}
