using System;
using Avalonia;
using Avalonia.Controls;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.Views
{

    public class ThemeAwareWindow : Window
    {

        private const string ReduceMotionClass = "reduce-motion";

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
                var reduce = AccessibilityService.Instance.ReduceMotion;
                if (reduce)
                {
                    if (!Classes.Contains(ReduceMotionClass))
                    {
                        Classes.Add(ReduceMotionClass);
                    }
                }
                else
                {
                    Classes.Remove(ReduceMotionClass);
                }
            }
            catch
            {

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

