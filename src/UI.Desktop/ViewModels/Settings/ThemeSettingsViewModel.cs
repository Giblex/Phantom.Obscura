using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ReactiveUI;
using PhantomVault.UI.Services;
using PhantomVault.UI.Views;

namespace PhantomVault.UI.ViewModels.Settings
{
    public class ThemeSettingsViewModel : ReactiveObject
    {
        private bool _isDarkTheme = true;
        private int _selectedThemeSkin = 0;
        private bool _enableHighContrast = false;
        private int _selectedDisplayScale = 2; // 100%
        private bool _reduceAnimations = false;
        private bool _reduceTransparency = false;
        private int _selectedRuntimeThemeIndex = 0;
        private readonly ThemeManagerService _themeManager;
        private readonly IRuntimeThemeService? _runtimeThemeService;
        private readonly double[] _scales = { 0.8, 0.9, 1.0, 1.1, 1.25, 1.5 };

        /// <summary>
        /// Available runtime themes for the ComboBox.
        /// </summary>
        public IReadOnlyList<string> RuntimeThemeNames { get; }

        /// <summary>
        /// Selected runtime theme index.
        /// </summary>
        public int SelectedRuntimeThemeIndex
        {
            get => _selectedRuntimeThemeIndex;
            set
            {
                var oldValue = _selectedRuntimeThemeIndex;
                this.RaiseAndSetIfChanged(ref _selectedRuntimeThemeIndex, value);
                if (oldValue != _selectedRuntimeThemeIndex)
                {
                    ApplyRuntimeTheme();
                }
            }
        }

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _isDarkTheme, value))
                {
                    ApplyTheme();
                }
            }
        }
        
        private void ApplyTheme()
        {
            _themeManager.SetTheme(EnableHighContrast ? AppTheme.HighContrast : (IsDarkTheme ? AppTheme.Dark : AppTheme.Light));
            
            try
            {
                var settings = SettingsService.Load();
                settings.IsDarkTheme = IsDarkTheme;
                SettingsService.Save(settings);
            }
            catch
            {
                // best-effort persistence
            }
        }

        public int SelectedThemeSkin
        {
            get => _selectedThemeSkin;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedThemeSkin, value);
                ApplyThemeSkin();
            }
        }

        public bool EnableHighContrast
        {
            get => _enableHighContrast;
            set
            {
                if (_enableHighContrast != value)
                {
                    _ = SetHighContrastAsync(value);
                }
            }
        }

        private async Task SetHighContrastAsync(bool enable)
        {
            if (enable)
            {
                // High contrast mode requires app restart for full effect
                var dialogService = new DialogService();
                var owner = GetOwnerWindow();
                
                bool confirmed = await dialogService.ShowConfirmationAsync(
                    "High Contrast Mode",
                    "High contrast mode requires an app restart to take full effect. Restart now?",
                    owner
                );

                if (confirmed)
                {
                    // Save the setting
                    try
                    {
                        var settings = SettingsService.Load();
                        settings.EnableHighContrast = true;
                        SettingsService.Save(settings);
                    }
                    catch
                    {
                        // best-effort persistence
                    }

                    // Restart the application
                    RestartApplication();
                }
                else
                {
                    // User canceled - don't change the value
                    // Reset the checkbox back to false without triggering this method again
                    _enableHighContrast = false;
                    this.RaisePropertyChanged(nameof(EnableHighContrast));
                }
            }
            else
            {
                // Disabling high contrast - can be done immediately
                this.RaiseAndSetIfChanged(ref _enableHighContrast, false);
                ApplyHighContrast();
            }
        }

        private Window? GetOwnerWindow()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow;
            }
            return null;
        }

        private void RestartApplication()
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = true,
                        WorkingDirectory = Environment.CurrentDirectory
                    });
                }

                // Close current instance
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
            }
            catch
            {
                // If restart fails, just apply the theme change
                this.RaiseAndSetIfChanged(ref _enableHighContrast, true);
                ApplyHighContrast();
            }
        }

        public int SelectedDisplayScale
        {
            get => _selectedDisplayScale;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedDisplayScale, value);
                ApplyDisplayScale();
            }
        }

        public bool ReduceAnimations
        {
            get => _reduceAnimations;
            set
            {
                this.RaiseAndSetIfChanged(ref _reduceAnimations, value);
                ApplyAnimationSettings();
            }
        }

        public bool ReduceTransparency
        {
            get => _reduceTransparency;
            set
            {
                this.RaiseAndSetIfChanged(ref _reduceTransparency, value);
                ApplyTransparencySettings();
            }
        }

        public ICommand ToggleThemeCommand { get; }

        public ThemeSettingsViewModel(ThemeManagerService? themeManager = null, IRuntimeThemeService? runtimeThemeService = null)
        {
            _themeManager = themeManager ?? ((Application.Current as App)?.Services?.GetService(typeof(ThemeManagerService)) as ThemeManagerService) ?? new ThemeManagerService();
            _runtimeThemeService = runtimeThemeService ?? ((Application.Current as App)?.Services?.GetService(typeof(IRuntimeThemeService)) as IRuntimeThemeService);
            ToggleThemeCommand = ReactiveCommand.Create(ToggleTheme);

            // Initialize runtime theme names
            if (_runtimeThemeService != null)
            {
                RuntimeThemeNames = _runtimeThemeService.GetThemes().Select(t => t.DisplayName).ToList();
            }
            else
            {
                RuntimeThemeNames = new List<string> { "Classic Dark", "Giblex Glass Navy" };
            }

            // Load persisted settings
            var settings = SettingsService.Load();
            var idx = Array.IndexOf(_scales, settings.RenderScale);
            if (idx >= 0)
            {
                _selectedDisplayScale = idx;
            }
            _isDarkTheme = settings.IsDarkTheme;
            _selectedThemeSkin = settings.ThemeSkin;
            _enableHighContrast = settings.EnableHighContrast;
            _reduceAnimations = settings.ReduceAnimations;
            _reduceTransparency = settings.ReduceTransparency;
            
            // Initialize runtime theme selection
            if (_runtimeThemeService != null && !string.IsNullOrEmpty(settings.SelectedThemeId))
            {
                var themes = _runtimeThemeService.GetThemes();
                var themeIdx = themes.ToList().FindIndex(t => t.Id == settings.SelectedThemeId);
                if (themeIdx >= 0)
                {
                    _selectedRuntimeThemeIndex = themeIdx;
                }
            }
        }

        private void ToggleTheme()
        {
            // Simply toggle the property - the property setter will handle applying the theme
            IsDarkTheme = !IsDarkTheme;
        }

        private void ApplyThemeSkin()
        {
            var skinNames = new[] { "Default", "MidnightBlue", "ForestGreen", "RoyalPurple", "SunsetOrange", "OceanTeal" };
            _themeManager.SetSkin(SelectedThemeSkin);
            
            try
            {
                var settings = SettingsService.Load();
                settings.ThemeSkin = SelectedThemeSkin;
                SettingsService.Save(settings);
            }
            catch
            {
                // best-effort persistence
            }
        }

        private void ApplyHighContrast()
        {
            // ApplyTheme() already handles the high contrast check
            ApplyTheme();
            
            try
            {
                var settings = SettingsService.Load();
                settings.EnableHighContrast = EnableHighContrast;
                SettingsService.Save(settings);
            }
            catch
            {
                // best-effort persistence
            }
        }

        private void ApplyDisplayScale()
        {
            var scale = _scales[Math.Clamp(SelectedDisplayScale, 0, _scales.Length - 1)];
            _themeManager.SetRenderScale(scale);

            try
            {
                var settings = SettingsService.Load();
                settings.RenderScale = scale;
                SettingsService.Save(settings);
            }
            catch
            {
                // best-effort persistence
            }
        }

        private void ApplyAnimationSettings()
        {
            _themeManager.SetEffects(ReduceAnimations, ReduceTransparency);
            
            try
            {
                var settings = SettingsService.Load();
                settings.ReduceAnimations = ReduceAnimations;
                settings.ReduceTransparency = ReduceTransparency;
                SettingsService.Save(settings);
            }
            catch
            {
                // best-effort persistence
            }
        }

        private void ApplyTransparencySettings()
        {
            _themeManager.SetEffects(ReduceAnimations, ReduceTransparency);
            
            try
            {
                var settings = SettingsService.Load();
                settings.ReduceAnimations = ReduceAnimations;
                settings.ReduceTransparency = ReduceTransparency;
                SettingsService.Save(settings);
            }
            catch
            {
                // best-effort persistence
            }
        }

        private void ApplyRuntimeTheme()
        {
            if (_runtimeThemeService == null) return;

            var themes = _runtimeThemeService.GetThemes();
            if (SelectedRuntimeThemeIndex >= 0 && SelectedRuntimeThemeIndex < themes.Count)
            {
                var selectedTheme = themes[SelectedRuntimeThemeIndex];
                _runtimeThemeService.Apply(selectedTheme.Id);

                try
                {
                    var settings = SettingsService.Load();
                    settings.SelectedThemeId = selectedTheme.Id;
                    SettingsService.Save(settings);
                }
                catch
                {
                    // best-effort persistence
                }
            }
        }
    }
}
