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
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            UnregisterFromThemeService();
            Opened -= OnOpened;
            Closed -= OnClosed;
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
