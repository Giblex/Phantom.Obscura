using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;

namespace PhantomVault.UI.Services
{

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

    public interface IRuntimeThemeService
    {

        IReadOnlyList<ThemeDescriptor> GetThemes();

        string CurrentThemeId { get; }

        void Apply(string themeId);

        void ApplyToWindow(Window window);

        void SuspendForLightMode();

        void ResumeForDarkMode();

        bool IsSuspended { get; }

        event EventHandler<string>? ThemeChanged;

        void LoadCustomThemes();

        bool RemoveCustomTheme(string themeId);
    }

    public sealed class RuntimeThemeService : IRuntimeThemeService
    {
        private readonly List<ThemeDescriptor> _themes;
        private readonly Dictionary<Window, ResourceInclude> _windowThemes = new();
        private readonly Dictionary<Window, Avalonia.Controls.ResourceDictionary> _windowCustomDicts = new();
        private string _currentThemeId;
        private bool _isSuspended;

        public event EventHandler<string>? ThemeChanged;

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

            LoadCustomThemes();
        }

        public void LoadCustomThemes()
        {

            _themes.RemoveAll(t => t.Id.StartsWith("Custom_"));

            foreach (var (id, displayName, filePath) in CustomThemeGenerator.DiscoverCustomThemes())
            {
                _themes.Add(new ThemeDescriptor(id, $"\u2728 {displayName}", new Uri(filePath)));
            }
        }

        public void RegisterCustomTheme(string filePath, bool apply = true)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var id = fileName.Replace("Theme.", "");
            var displayName = id.Replace("Custom_", "").Replace("_", " ");

            _themes.RemoveAll(t => t.Id == id);
            _themes.Add(new ThemeDescriptor(id, $"\u2728 {displayName}", new Uri(filePath)));

            if (apply)
            {
                Apply(id);
            }
        }

        public bool RemoveCustomTheme(string themeId)
        {
            var theme = _themes.FirstOrDefault(t => t.Id == themeId);
            if (theme == null || !themeId.StartsWith("Custom_")) return false;

            _themes.Remove(theme);
            CustomThemeGenerator.DeleteCustomTheme(theme.Uri.LocalPath);

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

            ApplyToAllThemedWindows(theme);

            ThemeChanged?.Invoke(this, themeId);
            System.Diagnostics.Debug.WriteLine($"[RuntimeThemeService] Applied theme: {themeId}");
        }

        private void ApplyToAllThemedWindows(ThemeDescriptor theme)
        {

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

                if (_windowThemes.TryGetValue(window, out var oldTheme) && oldTheme != null)
                {
                    window.Resources.MergedDictionaries.Remove(oldTheme);
                }
                if (_windowCustomDicts.TryGetValue(window, out var oldCustom))
                {
                    window.Resources.MergedDictionaries.Remove(oldCustom);
                    _windowCustomDicts.Remove(window);
                }

                if (_isSuspended)
                {
                    _windowThemes[window] = null!;
                    System.Diagnostics.Debug.WriteLine($"[RuntimeThemeService] Suspended — skipping runtime theme for window: {window.Title}");
                    return;
                }

                if (theme.Uri.Scheme == "avares")
                {

                    var resourceInclude = new ResourceInclude(new Uri("avares://PhantomVault.UI"))
                    {
                        Source = theme.Uri
                    };
                    window.Resources.MergedDictionaries.Add(resourceInclude);
                    _windowThemes[window] = resourceInclude;
                }
                else
                {

                    var xamlContent = File.ReadAllText(theme.Uri.LocalPath);
                    using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xamlContent));
                    var loaded = (Avalonia.Controls.ResourceDictionary)Avalonia.Markup.Xaml.AvaloniaRuntimeXamlLoader.Load(stream);
                    window.Resources.MergedDictionaries.Add(loaded);

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

        public void SuspendForLightMode()
        {
            if (_isSuspended) return;
            _isSuspended = true;

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
                    catch {  }
                }
                if (_windowCustomDicts.TryGetValue(window, out var customDict))
                {
                    try { window.Resources.MergedDictionaries.Remove(customDict); } catch { }
                    _windowCustomDicts.Remove(window);
                }
                _windowThemes[window] = null!;
            }
        }

        public void ResumeForDarkMode()
        {
            if (!_isSuspended) return;
            _isSuspended = false;

            var theme = _themes.FirstOrDefault(t => t.Id == _currentThemeId);
            if (theme != null)
            {
                ApplyToAllThemedWindows(theme);
            }
        }

        public void ApplyToWindow(Window window)
        {

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

        public void RegisterWindow(Window window)
        {
            if (!_windowThemes.ContainsKey(window))
            {
                _windowThemes[window] = null!;
                window.Closed += (_, _) => UnregisterWindow(window);
            }
        }

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

