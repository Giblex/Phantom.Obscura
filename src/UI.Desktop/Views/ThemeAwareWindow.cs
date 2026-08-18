using System;
using Avalonia;
using Avalonia.Controls;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.Views
{

    public class ThemeAwareWindow : Window
    {

        private const string ReduceMotionClass = "reduce-motion";
        private const string ReduceTransparencyClass = "reduce-transparency";

        private EventHandler? _accessibilityHandler;

        public ThemeAwareWindow()
        {

            ThemeScope.SetIsThemed(this, true);

            Opened += OnOpened;
            Closed += OnClosed;
        }

        private void OnOpened(object? sender, EventArgs e)
        {
            RegisterAndApplyTheme();
            ApplyAccentResources();
            ApplyAccessibilityClasses();

            _accessibilityHandler = (_, __) => ApplyAccessibilityClasses();
            AccessibilityService.Instance.SettingsChanged += _accessibilityHandler;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            if (_accessibilityHandler is not null)
            {
                AccessibilityService.Instance.SettingsChanged -= _accessibilityHandler;
                _accessibilityHandler = null;
            }

            UnregisterFromThemeService();
            Opened -= OnOpened;
            Closed -= OnClosed;
        }

        private void ApplyAccessibilityClasses()
        {
            try
            {
                ToggleClass(ReduceMotionClass, AccessibilityService.Instance.ReduceMotion);
                ToggleClass(ReduceTransparencyClass, AccessibilityService.Instance.ReduceTransparency);
            }
            catch
            {

            }
        }

        /// <summary>
        /// Stamps the live accent into this window's own Resources.
        ///
        /// UserAccentBrush is intentionally defined in no theme file — SetAccentColor is
        /// its only source — but ThemeManagerService only stamps windows that were already
        /// open when it ran. A window created later was left resolving the key through
        /// Application.Resources, which did not reliably reach controls inside a themed
        /// window: the brush came back null, and a null brush renders as nothing. That is
        /// what made a checked checkbox show its tick with no box behind it.
        ///
        /// Window.Resources sits above the app-level theme styles in the lookup, so
        /// stamping here resolves it for every window without reintroducing a theme-level
        /// definition that would shadow the user's chosen accent.
        /// </summary>
        private void ApplyAccentResources()
        {
            try
            {
                if (ThemeManagerService.CurrentAccent is not { } accent) return;

                Resources["UserAccentBrush"] = accent.Brush;
                Resources["UserAccentColor"] = accent.Color;
            }
            catch
            {
                // Never let accent stamping stop a window from opening.
            }
        }

        private void ToggleClass(string className, bool enabled)
        {
            if (enabled)
            {
                if (!Classes.Contains(className))
                {
                    Classes.Add(className);
                }
            }
            else
            {
                Classes.Remove(className);
            }
        }

        private void RegisterAndApplyTheme()
        {
            if (Application.Current is App app && app.Services != null)
            {
                var runtimeThemeService = app.Services.GetService(typeof(IRuntimeThemeService)) as RuntimeThemeService;
                if (runtimeThemeService != null)
                {

                    runtimeThemeService.RegisterWindow(this);

                    runtimeThemeService.ApplyToWindow(this);
                }
            }
        }

        private void UnregisterFromThemeService()
        {
            if (Application.Current is App app && app.Services != null)
            {
                var runtimeThemeService = app.Services.GetService(typeof(IRuntimeThemeService)) as RuntimeThemeService;
                runtimeThemeService?.UnregisterWindow(this);
            }
        }
    }
}

