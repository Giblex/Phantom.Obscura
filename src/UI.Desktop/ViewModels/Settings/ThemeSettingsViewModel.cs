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
        private bool _isDarkTheme = false;
        private int _selectedThemeSkin = 0;
        private bool _enableHighContrast = false;
        private int _selectedDisplayScale = 2; // 100%
        private bool _reduceAnimations = false;
        private bool _reduceTransparency = false;
        private int _selectedRuntimeThemeIndex = 0;
        private readonly ThemeManagerService _themeManager;
        private readonly IRuntimeThemeService? _runtimeThemeService;
        private readonly double[] _scales = { 0.8, 0.9, 1.0, 1.1, 1.25, 1.5 };

        // Custom theme editor fields
        private bool _isCustomEditorOpen;
        private string _customThemeName = "";
        private string _customPrimaryBg = "#0D0D12";
        private string _customSecondaryBg = "#12121A";
        private string _customSurfaceBg = "#1A1A26";
        private string _customAccent = "#6366F1";
        private string _customAccentHover = "#818CF8";
        private string _customTextPrimary = "#F0F0F8";
        private string _customTextMuted = "#8888AA";
        private string _customBorder = "#2A2A3E";
        private IReadOnlyList<string> _runtimeThemeNames = Array.Empty<string>();

        /// <summary>
        /// Available runtime themes for the ComboBox.
        /// </summary>
        public IReadOnlyList<string> RuntimeThemeNames
        {
            get => _runtimeThemeNames;
            private set => this.RaiseAndSetIfChanged(ref _runtimeThemeNames, value);
        }

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
            var theme = EnableHighContrast
                ? AppTheme.HighContrast
                : IsDarkTheme ? AppTheme.Dark : AppTheme.Light;
            _themeManager.SetTheme(theme);

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

        // ── Custom theme editor properties ─────────────────────
        public bool IsCustomEditorOpen
        {
            get => _isCustomEditorOpen;
            set => this.RaiseAndSetIfChanged(ref _isCustomEditorOpen, value);
        }

        public bool IsCustomThemeSelected
        {
            get
            {
                if (_runtimeThemeService == null) return false;
                var themes = _runtimeThemeService.GetThemes();
                if (SelectedRuntimeThemeIndex >= 0 && SelectedRuntimeThemeIndex < themes.Count)
                    return themes[SelectedRuntimeThemeIndex].Id.StartsWith("Custom_");
                return false;
            }
        }

        public string CustomThemeName { get => _customThemeName; set => this.RaiseAndSetIfChanged(ref _customThemeName, value); }
        public string CustomPrimaryBg { get => _customPrimaryBg; set { this.RaiseAndSetIfChanged(ref _customPrimaryBg, value); this.RaisePropertyChanged(nameof(CustomPrimaryBgPreview)); } }
        public string CustomSecondaryBg { get => _customSecondaryBg; set { this.RaiseAndSetIfChanged(ref _customSecondaryBg, value); this.RaisePropertyChanged(nameof(CustomSecondaryBgPreview)); } }
        public string CustomSurfaceBg { get => _customSurfaceBg; set { this.RaiseAndSetIfChanged(ref _customSurfaceBg, value); this.RaisePropertyChanged(nameof(CustomSurfaceBgPreview)); } }
        public string CustomAccent { get => _customAccent; set { this.RaiseAndSetIfChanged(ref _customAccent, value); this.RaisePropertyChanged(nameof(CustomAccentPreview)); } }
        public string CustomAccentHover { get => _customAccentHover; set { this.RaiseAndSetIfChanged(ref _customAccentHover, value); this.RaisePropertyChanged(nameof(CustomAccentHoverPreview)); } }
        public string CustomTextPrimary { get => _customTextPrimary; set { this.RaiseAndSetIfChanged(ref _customTextPrimary, value); this.RaisePropertyChanged(nameof(CustomTextPrimaryPreview)); } }
        public string CustomTextMuted { get => _customTextMuted; set { this.RaiseAndSetIfChanged(ref _customTextMuted, value); this.RaisePropertyChanged(nameof(CustomTextMutedPreview)); } }
        public string CustomBorder { get => _customBorder; set { this.RaiseAndSetIfChanged(ref _customBorder, value); this.RaisePropertyChanged(nameof(CustomBorderPreview)); } }

        // Color preview brushes – return the hex value itself (Avalonia auto-converts to SolidColorBrush)
        public string CustomPrimaryBgPreview => SafeHex(_customPrimaryBg);
        public string CustomSecondaryBgPreview => SafeHex(_customSecondaryBg);
        public string CustomSurfaceBgPreview => SafeHex(_customSurfaceBg);
        public string CustomAccentPreview => SafeHex(_customAccent);
        public string CustomAccentHoverPreview => SafeHex(_customAccentHover);
        public string CustomTextPrimaryPreview => SafeHex(_customTextPrimary);
        public string CustomTextMutedPreview => SafeHex(_customTextMuted);
        public string CustomBorderPreview => SafeHex(_customBorder);

        public ICommand CreateCustomThemeCommand { get; }
        public ICommand DeleteCustomThemeCommand { get; }
        public ICommand SaveCustomThemeCommand { get; }
        public ICommand CancelCustomThemeCommand { get; }

        private static string SafeHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return "#000000";
            var h = hex.Trim();
            if (!h.StartsWith('#')) h = "#" + h;
            return h;
        }

        public ThemeSettingsViewModel(ThemeManagerService? themeManager = null, IRuntimeThemeService? runtimeThemeService = null)
        {
            _themeManager = themeManager ?? ((Application.Current as App)?.Services?.GetService(typeof(ThemeManagerService)) as ThemeManagerService) ?? new ThemeManagerService();
            _runtimeThemeService = runtimeThemeService ?? ((Application.Current as App)?.Services?.GetService(typeof(IRuntimeThemeService)) as IRuntimeThemeService);
            ToggleThemeCommand = ReactiveCommand.Create(ToggleTheme);
            CreateCustomThemeCommand = ReactiveCommand.Create(OpenCustomEditor);
            DeleteCustomThemeCommand = ReactiveCommand.Create(DeleteSelectedCustomTheme);
            SaveCustomThemeCommand = ReactiveCommand.Create(SaveCustomTheme);
            CancelCustomThemeCommand = ReactiveCommand.Create(() => IsCustomEditorOpen = false);

            // Initialize runtime theme names
            RefreshThemeNames();

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

            this.RaisePropertyChanged(nameof(IsCustomThemeSelected));
        }

        // ── Custom theme editor methods ─────────────────────

        private void RefreshThemeNames()
        {
            if (_runtimeThemeService != null)
            {
                RuntimeThemeNames = _runtimeThemeService.GetThemes().Select(t => t.DisplayName).ToList();
            }
            else
            {
                RuntimeThemeNames = new List<string> { "Classic Dark", "Giblex Glass Navy" };
            }
        }

        private void OpenCustomEditor()
        {
            CustomThemeName = "";
            CustomPrimaryBg = "#0D0D12";
            CustomSecondaryBg = "#12121A";
            CustomSurfaceBg = "#1A1A26";
            CustomAccent = "#6366F1";
            CustomAccentHover = "#818CF8";
            CustomTextPrimary = "#F0F0F8";
            CustomTextMuted = "#8888AA";
            CustomBorder = "#2A2A3E";
            IsCustomEditorOpen = true;
        }

        private void SaveCustomTheme()
        {
            if (string.IsNullOrWhiteSpace(CustomThemeName) || _runtimeThemeService == null)
                return;

            try
            {
                var colors = new CustomThemeGenerator.ThemeColors
                {
                    Name = CustomThemeName.Trim(),
                    PrimaryBackground = SafeHex(CustomPrimaryBg),
                    SecondaryBackground = SafeHex(CustomSecondaryBg),
                    SurfaceBackground = SafeHex(CustomSurfaceBg),
                    Accent = SafeHex(CustomAccent),
                    AccentHover = SafeHex(CustomAccentHover),
                    TextPrimary = SafeHex(CustomTextPrimary),
                    TextMuted = SafeHex(CustomTextMuted),
                    Border = SafeHex(CustomBorder),
                    // Auto-pick reasonable defaults for the status colours
                    Success = "#22C55E",
                    Warning = "#F59E0B",
                    Error = "#EF4444"
                };

                var filePath = CustomThemeGenerator.GenerateAndSave(colors);
                _runtimeThemeService.LoadCustomThemes();
                RefreshThemeNames();

                // Select the newly created theme
                var themes = _runtimeThemeService.GetThemes();
                var sanitized = new string(colors.Name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
                var id = "Custom_" + sanitized;
                var idx = themes.ToList().FindIndex(t => t.Id == id);
                if (idx >= 0) SelectedRuntimeThemeIndex = idx;

                IsCustomEditorOpen = false;
            }
            catch
            {
                // best-effort – generator logs internally
            }
        }

        private void DeleteSelectedCustomTheme()
        {
            if (_runtimeThemeService == null) return;

            var themes = _runtimeThemeService.GetThemes();
            if (SelectedRuntimeThemeIndex < 0 || SelectedRuntimeThemeIndex >= themes.Count) return;

            var theme = themes[SelectedRuntimeThemeIndex];
            if (!theme.Id.StartsWith("Custom_")) return;

            _runtimeThemeService.RemoveCustomTheme(theme.Id);
            RefreshThemeNames();

            // Fall back to first theme
            SelectedRuntimeThemeIndex = 0;
        }
    }
}
