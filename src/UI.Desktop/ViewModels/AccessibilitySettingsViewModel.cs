using System;
using System.Collections.ObjectModel;
using System.Reactive;
using ReactiveUI;
using Avalonia.Controls;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model for accessibility settings including language, themes, view preferences, and keyboard shortcuts.
    /// </summary>
    public sealed class AccessibilitySettingsViewModel : ReactiveObject
    {
        private string _selectedLanguage = "English";
        private string _selectedThemeSkin = "Default";
        private bool _isDarkTheme = true;
        private UiScaleOption? _selectedScaleOption;
        private bool _isGridViewDefault = false;
        private string _defaultEmailUsername = string.Empty;
        private bool _useHighContrast = false;
        private bool _showShortcutHints = true;
        private bool _reduceMotion = false;
        private bool _largeTooltips = false;
        private Window? _ownerWindow;

        public AccessibilitySettingsViewModel()
        {
            ToggleThemeCommand = ReactiveCommand.Create(ToggleTheme);
            ResetToDefaultsCommand = ReactiveCommand.Create(ResetToDefaults);
            ViewShortcutsCommand = ReactiveCommand.Create(ViewShortcuts);
            
            // Initialize collections
            AvailableLanguages = new ObservableCollection<string>
            {
                "English",
                "Spanish",
                "French",
                "German",
                "Italian",
                "Portuguese",
                "Chinese (Simplified)",
                "Japanese",
                "Korean"
            };

            AvailableThemeSkins = new ObservableCollection<string>
            {
                "Default",
                "Midnight Blue",
                "Forest Green",
                "Royal Purple",
                "Sunset Orange",
                "Ocean Teal"
            };

            UiScaleOptions = new ObservableCollection<UiScaleOption>
            {
                new UiScaleOption { Label = "Smaller (80%)", Scale = 0.8 },
                new UiScaleOption { Label = "Small (90%)", Scale = 0.9 },
                new UiScaleOption { Label = "Normal (100%)", Scale = 1.0 },
                new UiScaleOption { Label = "Large (110%)", Scale = 1.1 },
                new UiScaleOption { Label = "Larger (125%)", Scale = 1.25 },
                new UiScaleOption { Label = "Extra Large (150%)", Scale = 1.5 }
            };
            
            // Set default scale
            _selectedScaleOption = UiScaleOptions[2]; // Normal (100%)
        }

        // Language Settings
        public ObservableCollection<string> AvailableLanguages { get; }

        public string SelectedLanguage
        {
            get => _selectedLanguage;
            set => this.RaiseAndSetIfChanged(ref _selectedLanguage, value);
        }

        // Theme Settings
        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set => this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
        }

        public ObservableCollection<string> AvailableThemeSkins { get; }

        public string SelectedThemeSkin
        {
            get => _selectedThemeSkin;
            set => this.RaiseAndSetIfChanged(ref _selectedThemeSkin, value);
        }

        public bool UseHighContrast
        {
            get => _useHighContrast;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _useHighContrast, value))
                {
                    // Update global accessibility service
                    PhantomVault.UI.Services.AccessibilityService.Instance.UseHighContrast = value;
                }
            }
        }

        // View Scale Settings
        public ObservableCollection<UiScaleOption> UiScaleOptions { get; }

        public UiScaleOption? SelectedScaleOption
        {
            get => _selectedScaleOption;
            set => this.RaiseAndSetIfChanged(ref _selectedScaleOption, value);
        }

        public double UiScale => _selectedScaleOption?.Scale ?? 1.0;

        // View Preferences
        public bool IsGridViewDefault
        {
            get => _isGridViewDefault;
            set => this.RaiseAndSetIfChanged(ref _isGridViewDefault, value);
        }

        public bool IsListViewDefault
        {
            get => !_isGridViewDefault;
            set => IsGridViewDefault = !value;
        }

        // Autofill Preferences
        public string DefaultEmailUsername
        {
            get => _defaultEmailUsername;
            set => this.RaiseAndSetIfChanged(ref _defaultEmailUsername, value);
        }

        // Accessibility Features
        public bool ShowShortcutHints
        {
            get => _showShortcutHints;
            set => this.RaiseAndSetIfChanged(ref _showShortcutHints, value);
        }

        public bool ReduceMotion
        {
            get => _reduceMotion;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _reduceMotion, value))
                {
                    // Update global accessibility service
                    PhantomVault.UI.Services.AccessibilityService.Instance.ReduceMotion = value;
                }
            }
        }

        public bool LargeTooltips
        {
            get => _largeTooltips;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _largeTooltips, value))
                {
                    // Update global accessibility service
                    PhantomVault.UI.Services.AccessibilityService.Instance.LargeTooltips = value;
                }
            }
        }

        // Commands
        public ReactiveCommand<Unit, Unit> ToggleThemeCommand { get; }
        public ReactiveCommand<Unit, Unit> ResetToDefaultsCommand { get; }
        public ReactiveCommand<Unit, Unit> ViewShortcutsCommand { get; }

        public void SetOwnerWindow(Window window)
        {
            _ownerWindow = window;
        }

        private void ToggleTheme()
        {
            IsDarkTheme = !IsDarkTheme;
            // Apply theme change through App
            if (Avalonia.Application.Current is App app)
            {
                app.SetTheme(IsDarkTheme ? "dark" : "light");
            }
        }

        private async void ViewShortcuts()
        {
            var window = new Views.KeyboardShortcutsWindow();
            if (_ownerWindow != null)
            {
                await window.ShowDialog(_ownerWindow);
            }
            else
            {
                window.Show();
            }
        }

        private void ResetToDefaults()
        {
            SelectedLanguage = "English";
            SelectedThemeSkin = "Default";
            IsDarkTheme = true;
            SelectedScaleOption = UiScaleOptions[2]; // 100% - Default
            IsGridViewDefault = false;
            DefaultEmailUsername = string.Empty;
            UseHighContrast = false;
            ShowShortcutHints = true;
            ReduceMotion = false;
            LargeTooltips = false;
        }
    }

    public class UiScaleOption
    {
        public string Label { get; set; } = string.Empty;
        public double Scale { get; set; }
    }
}
