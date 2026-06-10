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
        private int _selectedDisplayScale = 2;
        private string _appFontFamily = "Segoe UI";
        private int _selectedFontSizeIndex = 1;
        private string _accentColorHex = "#2B4A7A";
        private bool _reduceAnimations = false;
        private bool _reduceTransparency = false;
        private bool _useFlatButtons = false;
        private bool _useFlatButtonBorders = false;
        private bool _showCategoryColorBarOnly = false;
        private int _selectedRuntimeThemeIndex = 0;
        private readonly ThemeManagerService _themeManager;
        private readonly IRuntimeThemeService? _runtimeThemeService;

        private readonly PhantomVault.UI.Services.SettingsDraftTracker _draft;
        private readonly double[] _scales = { 0.8, 0.9, 1.0, 1.1, 1.25, 1.5 };

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

        public IReadOnlyList<string> RuntimeThemeNames
        {
            get => _runtimeThemeNames;
            private set => this.RaiseAndSetIfChanged(ref _runtimeThemeNames, value);
        }

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
                if (_isDarkTheme != value)
                {
                    this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
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

            bool stagedValue = IsDarkTheme;
            bool previousPersisted;
            try { previousPersisted = SettingsService.Load().IsDarkTheme; }
            catch { previousPersisted = stagedValue; }

            if (stagedValue == previousPersisted)
            {
                _draft.ClearKey("Theme.IsDarkTheme");
                return;
            }

            _draft.Stage(
                key: "Theme.IsDarkTheme",
                commit: () =>
                {
                    try
                    {
                        var s = SettingsService.Load();
                        s.IsDarkTheme = stagedValue;
                        SettingsService.Save(s);
                    }
                    catch {  }
                },
                discard: () =>
                {

                    _isDarkTheme = previousPersisted;
                    this.RaisePropertyChanged(nameof(IsDarkTheme));
                    var revertTheme = EnableHighContrast
                        ? AppTheme.HighContrast
                        : previousPersisted ? AppTheme.Dark : AppTheme.Light;
                    _themeManager.SetTheme(revertTheme);
                });
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

                var dialogService = new DialogService();
                var owner = GetOwnerWindow();

                bool confirmed = await dialogService.ShowConfirmationAsync(
                    "High Contrast Mode",
                    "High contrast mode requires an app restart to take full effect. Restart now?",
                    owner
                );

                if (confirmed)
                {

                    try
                    {
                        var settings = SettingsService.Load();
                        settings.EnableHighContrast = true;
                        SettingsService.Save(settings);
                    }
                    catch
                    {

                    }

                    RestartApplication();
                }
                else
                {

                    _enableHighContrast = false;
                    this.RaisePropertyChanged(nameof(EnableHighContrast));
                }
            }
            else
            {

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

                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
            }
            catch
            {

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

        public IReadOnlyList<string> FontFamilyOptions { get; } = new[]
        {
            "Segoe UI", "Arial", "Calibri", "Verdana", "Tahoma",
            "Consolas", "Cascadia Code", "Georgia", "Times New Roman"
        };

        public IReadOnlyList<string> FontSizeOptions { get; } = new[]
        {
            "Small (12)", "Default (14)", "Medium (16)", "Large (18)", "Extra Large (20)"
        };

        private static readonly double[] FontSizes = { 12.0, 14.0, 16.0, 18.0, 20.0 };

        public IReadOnlyList<string> AccentColorOptions { get; } = new[]
        {
            "#2B4A7A", "#004258", "#3A5E94", "#5A7AB0", "#6366F1", "#2E7D6B", "#8A5CB8"
        };

        public string AppFontFamily
        {
            get => _appFontFamily;
            set
            {
                var old = _appFontFamily;
                this.RaiseAndSetIfChanged(ref _appFontFamily, value);
                if (old != _appFontFamily)
                    ApplyAppFont();
            }
        }

        public int SelectedFontSizeIndex
        {
            get => _selectedFontSizeIndex;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedFontSizeIndex, value);
                this.RaisePropertyChanged(nameof(AppFontSize));
                ApplyAppFontSize();
            }
        }

        public double AppFontSize => FontSizes[Math.Clamp(_selectedFontSizeIndex, 0, FontSizes.Length - 1)];

        public string AccentColorHex
        {
            get => _accentColorHex;
            set
            {
                var old = _accentColorHex;
                this.RaiseAndSetIfChanged(ref _accentColorHex, value);
                if (old != _accentColorHex)
                    ApplyAccentColor();
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

        public bool UseFlatButtons
        {
            get => _useFlatButtons;
            set
            {
                if (_useFlatButtons != value)
                {
                    this.RaiseAndSetIfChanged(ref _useFlatButtons, value);
                    ApplyUseFlatButtons();
                }
            }
        }

        private void ApplyUseFlatButtons()
        {
            _themeManager.SetFlatButtons(_useFlatButtons);

            bool staged = _useFlatButtons;
            bool previousPersisted;
            try { previousPersisted = SettingsService.Load().UseFlatButtons; }
            catch { previousPersisted = staged; }

            if (staged == previousPersisted)
            {
                _draft.ClearKey("Theme.UseFlatButtons");
                return;
            }

            _draft.Stage(
                key: "Theme.UseFlatButtons",
                commit: () =>
                {
                    try
                    {
                        var s = SettingsService.Load();
                        s.UseFlatButtons = staged;
                        SettingsService.Save(s);
                    }
                    catch {  }
                },
                discard: () =>
                {
                    _useFlatButtons = previousPersisted;
                    this.RaisePropertyChanged(nameof(UseFlatButtons));
                    _themeManager.SetFlatButtons(previousPersisted);
                });
        }

        public bool UseFlatButtonBorders
        {
            get => _useFlatButtonBorders;
            set
            {
                if (_useFlatButtonBorders != value)
                {
                    this.RaiseAndSetIfChanged(ref _useFlatButtonBorders, value);
                    ApplyUseFlatButtonBorders();
                }
            }
        }

        private void ApplyUseFlatButtonBorders()
        {
            _themeManager.SetFlatButtonBorders(_useFlatButtonBorders);

            bool staged = _useFlatButtonBorders;
            bool previousPersisted;
            try { previousPersisted = SettingsService.Load().UseFlatButtonBorders; }
            catch { previousPersisted = staged; }

            if (staged == previousPersisted)
            {
                _draft.ClearKey("Theme.UseFlatButtonBorders");
                return;
            }

            _draft.Stage(
                key: "Theme.UseFlatButtonBorders",
                commit: () =>
                {
                    try
                    {
                        var s = SettingsService.Load();
                        s.UseFlatButtonBorders = staged;
                        SettingsService.Save(s);
                    }
                    catch {  }
                },
                discard: () =>
                {
                    _useFlatButtonBorders = previousPersisted;
                    this.RaisePropertyChanged(nameof(UseFlatButtonBorders));
                    _themeManager.SetFlatButtonBorders(previousPersisted);
                });
        }

        public bool ShowCategoryColorBarOnly
        {
            get => _showCategoryColorBarOnly;
            set
            {
                this.RaiseAndSetIfChanged(ref _showCategoryColorBarOnly, value);
                ApplyShowCategoryColorBarOnly();
            }
        }

        public ICommand ToggleThemeCommand { get; }

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

        public ThemeSettingsViewModel(ThemeManagerService? themeManager = null, IRuntimeThemeService? runtimeThemeService = null, PhantomVault.UI.Services.SettingsDraftTracker? draftTracker = null)
        {
            _themeManager = themeManager ?? ((Application.Current as App)?.Services?.GetService(typeof(ThemeManagerService)) as ThemeManagerService) ?? new ThemeManagerService();
            _runtimeThemeService = runtimeThemeService ?? ((Application.Current as App)?.Services?.GetService(typeof(IRuntimeThemeService)) as IRuntimeThemeService);

            _draft = draftTracker
                ?? ((Application.Current as App)?.Services?.GetService(typeof(PhantomVault.UI.Services.SettingsDraftTracker)) as PhantomVault.UI.Services.SettingsDraftTracker)
                ?? new PhantomVault.UI.Services.SettingsDraftTracker();
            ToggleThemeCommand = ReactiveCommand.Create(ToggleTheme);
            CreateCustomThemeCommand = ReactiveCommand.Create(OpenCustomEditor);
            DeleteCustomThemeCommand = ReactiveCommand.Create(DeleteSelectedCustomTheme);
            SaveCustomThemeCommand = ReactiveCommand.Create(SaveCustomTheme);
            CancelCustomThemeCommand = ReactiveCommand.Create(() => IsCustomEditorOpen = false);

            RefreshThemeNames();

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
            _useFlatButtons = settings.UseFlatButtons;
            _useFlatButtonBorders = settings.UseFlatButtonBorders;
            _showCategoryColorBarOnly = settings.ShowCategoryColorBarOnly;

            if (!string.IsNullOrWhiteSpace(settings.AppFontFamily))
                _appFontFamily = settings.AppFontFamily;
            var sizeIdx = Array.IndexOf(FontSizes, settings.AppFontSize);
            if (sizeIdx >= 0) _selectedFontSizeIndex = sizeIdx;
            if (!string.IsNullOrWhiteSpace(settings.AccentColorHex))
                _accentColorHex = settings.AccentColorHex;

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

            }
        }

        private void ApplyHighContrast()
        {

            ApplyTheme();

            try
            {
                var settings = SettingsService.Load();
                settings.EnableHighContrast = EnableHighContrast;
                SettingsService.Save(settings);
            }
            catch
            {

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

            }
        }

        private void ApplyAppFont()
        {
            _themeManager.SetAppFont(_appFontFamily);
            try
            {
                var s = SettingsService.Load();
                s.AppFontFamily = _appFontFamily;
                SettingsService.Save(s);
            }
            catch {  }
        }

        private void ApplyAppFontSize()
        {
            var size = FontSizes[Math.Clamp(_selectedFontSizeIndex, 0, FontSizes.Length - 1)];
            _themeManager.SetAppFontSize(size);
            try
            {
                var s = SettingsService.Load();
                s.AppFontSize = size;
                SettingsService.Save(s);
            }
            catch {  }
        }

        private void ApplyAccentColor()
        {
            _themeManager.SetAccentColor(_accentColorHex);
            try
            {
                var s = SettingsService.Load();
                s.AccentColorHex = _accentColorHex;
                SettingsService.Save(s);
            }
            catch {  }
        }

        private void ApplyAnimationSettings() => StageEffects();

        private void ApplyTransparencySettings() => StageEffects();

        private void StageEffects()
        {

            _themeManager.SetEffects(ReduceAnimations, ReduceTransparency);

            bool stagedAnim = ReduceAnimations;
            bool stagedTrans = ReduceTransparency;
            bool prevAnim, prevTrans;
            try
            {
                var s = SettingsService.Load();
                prevAnim = s.ReduceAnimations;
                prevTrans = s.ReduceTransparency;
            }
            catch { prevAnim = stagedAnim; prevTrans = stagedTrans; }

            if (stagedAnim == prevAnim && stagedTrans == prevTrans)
            {
                _draft.ClearKey("Theme.Effects");
                return;
            }

            _draft.Stage(
                key: "Theme.Effects",
                commit: () =>
                {
                    try
                    {
                        var s = SettingsService.Load();
                        s.ReduceAnimations = stagedAnim;
                        s.ReduceTransparency = stagedTrans;
                        SettingsService.Save(s);
                    }
                    catch {  }
                },
                discard: () =>
                {
                    _reduceAnimations = prevAnim;
                    _reduceTransparency = prevTrans;
                    this.RaisePropertyChanged(nameof(ReduceAnimations));
                    this.RaisePropertyChanged(nameof(ReduceTransparency));
                    _themeManager.SetEffects(prevAnim, prevTrans);
                });
        }

        private void ApplyShowCategoryColorBarOnly()
        {

            _themeManager.SetShowCategoryColorBarOnly(_showCategoryColorBarOnly);

            try
            {
                var settings = SettingsService.Load();
                settings.ShowCategoryColorBarOnly = _showCategoryColorBarOnly;
                SettingsService.Save(settings);
            }
            catch
            {

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

                string stagedId = selectedTheme.Id;
                string? previousPersistedId;
                int previousIndex = SelectedRuntimeThemeIndex;
                try { previousPersistedId = SettingsService.Load().SelectedThemeId; }
                catch { previousPersistedId = stagedId; }

                int previousIdx = 0;
                if (!string.IsNullOrEmpty(previousPersistedId))
                {
                    for (int i = 0; i < themes.Count; i++)
                    {
                        if (themes[i].Id == previousPersistedId) { previousIdx = i; break; }
                    }
                }

                if (stagedId == previousPersistedId)
                {
                    _draft.ClearKey("Theme.RuntimeTheme");
                }
                else
                {
                    _draft.Stage(
                        key: "Theme.RuntimeTheme",
                        commit: () =>
                        {
                            try
                            {
                                var s = SettingsService.Load();
                                s.SelectedThemeId = stagedId;
                                SettingsService.Save(s);
                            }
                            catch {  }
                        },
                        discard: () =>
                        {
                            if (!string.IsNullOrEmpty(previousPersistedId))
                                _runtimeThemeService.Apply(previousPersistedId);
                            _selectedRuntimeThemeIndex = previousIdx;
                            this.RaisePropertyChanged(nameof(SelectedRuntimeThemeIndex));
                            this.RaisePropertyChanged(nameof(IsCustomThemeSelected));
                        });
                }
            }

            this.RaisePropertyChanged(nameof(IsCustomThemeSelected));
        }

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

                    Success = "#22C55E",
                    Warning = "#F59E0B",
                    Error = "#EF4444"
                };

                var filePath = CustomThemeGenerator.GenerateAndSave(colors);
                _runtimeThemeService.LoadCustomThemes();
                RefreshThemeNames();

                var themes = _runtimeThemeService.GetThemes();
                var sanitized = new string(colors.Name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
                var id = "Custom_" + sanitized;
                var idx = themes.ToList().FindIndex(t => t.Id == id);
                if (idx >= 0) SelectedRuntimeThemeIndex = idx;

                IsCustomEditorOpen = false;
            }
            catch
            {

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

            SelectedRuntimeThemeIndex = 0;
        }
    }
}

