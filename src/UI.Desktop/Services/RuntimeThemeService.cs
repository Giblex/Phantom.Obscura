using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Serilog;

namespace PhantomVault.UI.Services
{

    public sealed class ThemeDescriptor
    {
        public string Id { get; }
        public string DisplayName { get; }
        public Uri Uri { get; }

        // Three preview swatch colours (background, accent, secondary accent) used to
        // render the website-style theme grid. Empty when not supplied.
        public IReadOnlyList<string> PreviewColors { get; }

        // Mirrors the website demo's Free/Pro split: the default palette is free, the
        // rest are Pro. Non-website themes leave this false.
        public bool IsPremium { get; }

        /// <summary>
        /// Whether this skin is a light palette.
        ///
        /// The skin and the dark/light variant used to be fully independent, so a light
        /// skin could run under the dark variant. That matters because the variant picks
        /// the app-level PhantomTheme fallback for every key a skin does not define, and
        /// it drives Fluent's own control defaults — which is how a light theme ended up
        /// with invisible checkbox outlines and dark fallback surfaces.
        /// </summary>
        public bool IsLight { get; }

        public ThemeDescriptor(string id, string displayName, Uri uri,
            IReadOnlyList<string>? previewColors = null, bool isPremium = false,
            bool isLight = false)
        {
            Id = id;
            DisplayName = displayName;
            Uri = uri;
            PreviewColors = previewColors ?? Array.Empty<string>();
            IsPremium = isPremium;
            IsLight = isLight;
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
            // Default Navy and Classic Light are the two anchors. The remainder runs from
            // light to dark. Preview values are copied from each theme's actual
            // WindowBackgroundBrush, AccentBrush and SecondaryAccentBrush resources.
            _themes = new List<ThemeDescriptor>
            {
                new("GiblexGlassNavy", "Default Navy",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.GiblexGlassNavy.axaml"),
                    new[] { "#0A0F18", "#004258", "#003248" }),
                new("GiblexWebsite", "Classic Light",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.GiblexWebsite.axaml"),
                    new[] { "#DEF2F6", "#138A9C", "#55C3CF" }, isLight: true),

                new("ClassicLight", "Modern Light",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.ClassicLight.axaml"),
                    new[] { "#FDFCF9", "#4A7BB8", "#7AA3D6" }, isLight: true),
                new("ArcticFrost", "Arctic Frost",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.ArcticFrost.axaml"),
                    new[] { "#F7FAFC", "#3182CE", "#63B3ED" }, isLight: true),
                new("GiblexWebPurple", "Blackberry White",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.GiblexWebPurple.axaml"),
                    new[] { "#F6F8FF", "#7C5CFF", "#55E6FF" }, isLight: true),
                new("SunsetEmber", "Sunset",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.SunsetEmber.axaml"),
                    new[] { "#FFF7EB", "#EF5A3C", "#F6B73C" }, isLight: true),
                new("Proton", "Photon",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.Proton.axaml"),
                    new[] { "#E8E4F3", "#6D4AFF", "#8B6EFF" }, isLight: true),
                new("CharcoalPastel", "Charcoal Pastel",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.CharcoalPastel.axaml"),
                    new[] { "#424250", "#89B4D9", "#B5D4F0" }),
                new("Natural", "Natural",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.Natural.axaml"),
                    new[] { "#493B35", "#C97664", "#8FA88A" }),
                new("MidnightNeon", "Midnight Neon",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.MidnightNeon.axaml"),
                    new[] { "#171A1D", "#A970FF", "#57E6FF" }),
                new("Cyberpunk", "Cyberpunk",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.Cyberpunk.axaml"),
                    new[] { "#141432", "#FF3CAC", "#63FF70" }),
                new("ClassicDark", "Deep Dark",
                    new Uri("avares://PhantomVault.UI/Assets/Themes/Theme.ClassicDark.axaml"),
                    new[] { "#10151C", "#556C83", "#6FA89F" }),
            };

            _currentThemeId = "GiblexGlassNavy";

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
            // Keep older settings files valid after the catalogue cleanup. Retired web
            // palettes intentionally converge on the new default instead of leaving the
            // application half-themed when their resource is no longer selectable.
            themeId = themeId switch
            {
                "WebDefaultDark" or "WebMidnightBlue" or "WebEmber" or "WebArctic"
                    or "WebPhantomViolet" or "ModernSystem" or "GiblexDark"
                    or "WebHighContrast" => "GiblexGlassNavy",
                _ => themeId
            };
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

        // Skin resources are merged into window.Resources.MergedDictionaries, but the
        // separate Dark/Light/HighContrast toggle (ThemeManagerService.SetTheme) adds
        // PhantomTheme.axaml as an Application.Styles overlay — and for any key both define,
        // that overlay silently wins over the window-level merged skin (the same issue
        // SetAccentColor's comment already documents for AccentBrush). That's why switching
        // to a light skin like Giblex Light left the header/banner/dropdown stuck on the
        // dark PhantomTheme values. Fix: stamp every key the skin defines directly onto
        // window.Resources too — a window's own Resources entries always win over anything
        // from Application.Styles, which is the same trick SetAccentColor already relies on.
        private readonly Dictionary<Window, HashSet<object>> _windowStampedKeys = new();

        private void ClearStampedKeys(Window window)
        {
            if (!_windowStampedKeys.TryGetValue(window, out var keys)) return;
            foreach (var key in keys)
            {
                try { window.Resources.Remove(key); } catch { }
            }
            keys.Clear();
        }

        private void StampResourceKeysFromDictionary(Window window, Avalonia.Controls.ResourceDictionary dict)
        {
            if (!_windowStampedKeys.TryGetValue(window, out var keys))
            {
                keys = new HashSet<object>();
                _windowStampedKeys[window] = keys;
            }

            foreach (var kvp in dict)
            {
                window.Resources[kvp.Key] = kvp.Value;
                keys.Add(kvp.Key);
            }

            Log.Debug("[RuntimeThemeService] Stamped {Count} keys directly onto window '{Title}'.", keys.Count, window.Title);
            if (window.Resources.TryGetResource("HeaderBackgroundBrush", null, out var headerBg))
            {
                Log.Debug("[RuntimeThemeService] window.Resources['HeaderBackgroundBrush'] (post-stamp, direct lookup) = {Value}", headerBg);
            }
            if (window.TryFindResource("HeaderBackgroundBrush", out var resolvedHeaderBg))
            {
                Log.Debug("[RuntimeThemeService] window.TryFindResource('HeaderBackgroundBrush') (full lookup as a control would see it) = {Value}", resolvedHeaderBg);
            }
        }

        // Theme .axaml files aren't registered as generic AvaloniaResource assets, so
        // AssetLoader.Open can't see them ("resource could not be found") even though
        // ResourceInclude itself loads the exact same URI fine (that's how the merge above
        // already works) — ResourceInclude uses Avalonia's XAML-specific resource resolution,
        // not the generic asset loader. Read the already-loaded dictionary back off the
        // ResourceInclude instead of re-loading independently.
        private void StampResourceKeysFromInclude(Window window, ResourceInclude resourceInclude)
        {
            if (resourceInclude.Loaded is Avalonia.Controls.ResourceDictionary dict)
            {
                StampResourceKeysFromDictionary(window, dict);
            }
            else
            {
                Log.Debug("[RuntimeThemeService] ResourceInclude.Loaded was not a ResourceDictionary for {Source}", resourceInclude.Source);
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
                ClearStampedKeys(window);

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
                    StampResourceKeysFromInclude(window, resourceInclude);
                }
                else
                {

                    var xamlContent = File.ReadAllText(theme.Uri.LocalPath);
                    using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xamlContent));
                    var loaded = (Avalonia.Controls.ResourceDictionary)Avalonia.Markup.Xaml.AvaloniaRuntimeXamlLoader.Load(stream);
                    window.Resources.MergedDictionaries.Add(loaded);

                    _windowCustomDicts[window] = loaded;
                    _windowThemes[window] = null!;
                    StampResourceKeysFromDictionary(window, loaded);
                }

                Log.Debug("[RuntimeThemeService] Applied {ThemeId} to window: {Title}", theme.Id, window.Title);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[RuntimeThemeService] Failed to apply theme to window: {Title}", window.Title);
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
                ClearStampedKeys(window);
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
            ClearStampedKeys(window);
            _windowStampedKeys.Remove(window);
        }
    }
}

