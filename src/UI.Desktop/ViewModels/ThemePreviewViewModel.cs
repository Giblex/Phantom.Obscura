using System;
using System.Linq;
using System.Reactive;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ReactiveUI;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.ViewModels
{

    public sealed class ThemePreviewViewModel : ReactiveObject
    {
        private readonly ThemeManagerService _themeManager;
        private readonly IRuntimeThemeService? _runtimeThemeService;
        private string _currentThemeName = "Unknown";

        public ThemePreviewViewModel(
            ThemeManagerService? themeManager = null,
            IRuntimeThemeService? runtimeThemeService = null)
        {
            _themeManager = themeManager ?? GetServiceOrDefault<ThemeManagerService>() ?? new ThemeManagerService();
            _runtimeThemeService = runtimeThemeService ?? GetServiceOrNull<IRuntimeThemeService>();

            LoadCurrentThemeName();

            ApplyThemeCommand = ReactiveCommand.Create<string>(ApplyTheme);
            CloseCommand = ReactiveCommand.Create(CloseWindow);
        }

        public string CurrentThemeName
        {
            get => _currentThemeName;
            private set => this.RaiseAndSetIfChanged(ref _currentThemeName, value);
        }

        public ReactiveCommand<string, Unit> ApplyThemeCommand { get; }
        public ReactiveCommand<Unit, Unit> CloseCommand { get; }

        private void LoadCurrentThemeName()
        {
            try
            {
                var settings = SettingsService.Load();

                if (!string.IsNullOrEmpty(settings.SelectedThemeId) && _runtimeThemeService != null)
                {
                    var theme = _runtimeThemeService.GetThemes()
                        .FirstOrDefault(t => t.Id == settings.SelectedThemeId);

                    if (theme != null)
                    {
                        CurrentThemeName = theme.DisplayName;
                        return;
                    }
                }

                CurrentThemeName = "Phantom Light";
            }
            catch
            {
                CurrentThemeName = "Unknown";
            }
        }

        private void ApplyTheme(string themeId)
        {
            if (string.IsNullOrEmpty(themeId))
                return;

            try
            {

                switch (themeId.ToLowerInvariant())
                {
                    case "phantom-dark":
                        _themeManager.SetTheme(AppTheme.Dark);
                        SaveThemeSelection(null, true);
                        CurrentThemeName = "Phantom Dark";
                        break;

                    case "phantom-light":
                        _themeManager.SetTheme(AppTheme.Light);
                        SaveThemeSelection(null, false);
                        CurrentThemeName = "Phantom Light";
                        break;

                    case "giblex-glass-navy":
                        if (_runtimeThemeService != null)
                        {
                            _runtimeThemeService.Apply("giblex-glass-navy");
                            SaveThemeSelection("giblex-glass-navy", true);
                            CurrentThemeName = "Giblex Glass Navy";
                        }
                        break;

                    case "classic-dark":
                        if (_runtimeThemeService != null)
                        {
                            _runtimeThemeService.Apply("classic-dark");
                            SaveThemeSelection("classic-dark", true);
                            CurrentThemeName = "Classic Dark";
                        }
                        break;

                    case "classic-light":
                        if (_runtimeThemeService != null)
                        {
                            _runtimeThemeService.Apply("classic-light");
                            SaveThemeSelection("classic-light", false);
                            CurrentThemeName = "Classic Light";
                        }
                        break;

                    default:

                        if (_runtimeThemeService != null)
                        {
                            var theme = _runtimeThemeService.GetThemes()
                                .FirstOrDefault(t => t.Id.Equals(themeId, StringComparison.OrdinalIgnoreCase));

                            if (theme != null)
                            {
                                _runtimeThemeService.Apply(theme.Id);
                                SaveThemeSelection(theme.Id, true);
                                CurrentThemeName = theme.DisplayName;
                            }
                        }
                        break;
                }
            }
            catch (Exception ex)
            {

                System.Diagnostics.Debug.WriteLine($"Failed to apply theme '{themeId}': {ex.Message}");
            }
        }

        private void SaveThemeSelection(string? themeId, bool isDark)
        {
            try
            {
                var settings = SettingsService.Load();
                settings.SelectedThemeId = themeId ?? string.Empty;
                settings.IsDarkTheme = false;
                SettingsService.Save(settings);
            }
            catch
            {

            }
        }

        private void CloseWindow()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var window = desktop.Windows.FirstOrDefault(w => w is Views.ThemePreviewWindow);
                window?.Close();
            }
        }

        private T? GetServiceOrDefault<T>() where T : class, new()
        {
            try
            {
                if (Application.Current is App app && app.Services != null)
                {
                    var service = app.Services.GetService(typeof(T)) as T;
                    if (service != null)
                        return service;
                }
            }
            catch
            {

            }

            return new T();
        }

        private T? GetServiceOrNull<T>() where T : class
        {
            try
            {
                if (Application.Current is App app && app.Services != null)
                {
                    return app.Services.GetService(typeof(T)) as T;
                }
            }
            catch
            {

            }

            return null;
        }
    }
}

