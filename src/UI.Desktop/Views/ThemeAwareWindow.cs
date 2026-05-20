using System;
using Avalonia;
using Avalonia.Controls;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.Views
{
    /// <summary>
    /// Base window that participates in runtime theme switching.
    /// Windows inheriting from this class register themselves with the RuntimeThemeService.
    /// By default, ThemeAwareWindow has ThemeScope.IsThemed = true.
    /// Override in constructor with ThemeScope.SetIsThemed(this, false) for setup/welcome windows.
    /// </summary>
    public class ThemeAwareWindow : Window
    {
        // Avalonia style class used by accessibility-aware styles to opt out
        // of decorative motion. Selectors like
        //   <Style Selector="Window:not(.reduce-motion) Border.unlock-orb">
        // gate animations on this class. Toggled live from
        // AccessibilityService.ReduceMotion (which also picks up the OS
        // preference at startup on Windows / macOS / GNOME).
        private const string ReduceMotionClass = "reduce-motion";

        private EventHandler? _accessibilityHandler;

        public ThemeAwareWindow()
        {
            // Mark as themed by default - override in specific windows if needed
            ThemeScope.SetIsThemed(this, true);

            Opened += OnOpened;
            Closed += OnClosed;
        }

        private void OnOpened(object? sender, EventArgs e)
        {
            RegisterAndApplyTheme();
            ApplyAccessibilityClasses();

            // Live-update if the user toggles ReduceMotion in settings while
            // the window is open. The handler is captured so we can detach
            // exactly the same delegate in OnClosed.
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

        /// <summary>
        /// Adds or removes the <c>reduce-motion</c> Avalonia style class on this
        /// window based on <see cref="AccessibilityService.ReduceMotion"/>.
        /// Safe to call repeatedly — Classes.Add / Classes.Remove are idempotent.
        /// </summary>
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
                // Never let an accessibility-class update bring down a window.
            }
        }

        private void RegisterAndApplyTheme()
        {
            if (Application.Current is App app && app.Services != null)
            {
                var runtimeThemeService = app.Services.GetService(typeof(IRuntimeThemeService)) as RuntimeThemeService;
                if (runtimeThemeService != null)
                {
                    // Register window with theme service for future theme changes
                    runtimeThemeService.RegisterWindow(this);
                    
                    // Apply current theme (only if IsThemed = true)
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
