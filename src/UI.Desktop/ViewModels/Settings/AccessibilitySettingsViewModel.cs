using System;
using System.Reactive;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using ReactiveUI;
using PhantomVault.UI.Services;
using PhantomVault.UI.Views;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.ViewModels.Settings
{
    public class AccessibilitySettingsViewModel : ReactiveObject
    {
        private int _selectedLanguage = 0;
        private int _selectedDefaultView = 0; // List view
        private bool _showEntryIcons = true;
        private bool _showCategoryColors = true;
        private int _selectedFontSize = 1; // Medium (13px)
        private int _selectedFontFamily = 0;
        private bool _enableKeyboardShortcuts = true;
        private bool _focusSearchOnOpen = true;
        private bool _enableScreenReader = false;

        // Maps the language dropdown index to a culture code. Only en-US and
        // es-ES ship with translated string resources today; the rest persist
        // the choice and set the culture (formatting) but fall back to English
        // strings until their resource files exist.
        private static readonly string[] LanguageCultures =
        {
            "en-US", "es-ES", "fr-FR", "de-DE", "it-IT", "pt-PT", "zh-CN", "ja-JP", "ko-KR"
        };

        public int SelectedLanguage
        {
            get => _selectedLanguage;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedLanguage, value);
                ApplyLanguage();
            }
        }

        public int SelectedDefaultView
        {
            get => _selectedDefaultView;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedDefaultView, value);
                Persist(s => s.PreferGridView = value == 1);
            }
        }

        public bool ShowEntryIcons
        {
            get => _showEntryIcons;
            set
            {
                this.RaiseAndSetIfChanged(ref _showEntryIcons, value);
                Persist(s => s.ShowEntryIcons = value);
            }
        }

        public bool ShowCategoryColors
        {
            get => _showCategoryColors;
            set
            {
                this.RaiseAndSetIfChanged(ref _showCategoryColors, value);
                Persist(s => s.ShowCategoryColors = value);
            }
        }

        public int SelectedFontSize
        {
            get => _selectedFontSize;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedFontSize, value);
                Persist(s => s.AccessibilityFontSize = value);
                ApplyFontSettings();
            }
        }

        public int SelectedFontFamily
        {
            get => _selectedFontFamily;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedFontFamily, value);
                Persist(s => s.AccessibilityFontFamily = value);
                ApplyFontSettings();
            }
        }

        public bool EnableKeyboardShortcuts
        {
            get => _enableKeyboardShortcuts;
            set
            {
                this.RaiseAndSetIfChanged(ref _enableKeyboardShortcuts, value);
                Persist(s => s.EnableKeyboardShortcuts = value);
            }
        }

        public bool FocusSearchOnOpen
        {
            get => _focusSearchOnOpen;
            set
            {
                this.RaiseAndSetIfChanged(ref _focusSearchOnOpen, value);
                Persist(s => s.FocusSearchOnOpen = value);
            }
        }

        public bool EnableScreenReader
        {
            get => _enableScreenReader;
            set
            {
                this.RaiseAndSetIfChanged(ref _enableScreenReader, value);
                Persist(s => s.EnableScreenReader = value);
            }
        }

        public ICommand ConfigureShortcutsCommand { get; }

        public AccessibilitySettingsViewModel()
        {
            ConfigureShortcutsCommand = ReactiveCommand.Create(ConfigureShortcuts);

            // Load persisted accessibility preferences directly into the backing
            // fields (avoid the public setters so we don't re-persist on load).
            try
            {
                var s = SettingsService.Load();
                _selectedLanguage = s.LanguageIndex;
                _selectedDefaultView = s.PreferGridView ? 1 : 0;
                _showEntryIcons = s.ShowEntryIcons;
                _showCategoryColors = s.ShowCategoryColors;
                _selectedFontSize = s.AccessibilityFontSize;
                _selectedFontFamily = s.AccessibilityFontFamily;
                _enableKeyboardShortcuts = s.EnableKeyboardShortcuts;
                _focusSearchOnOpen = s.FocusSearchOnOpen;
                _enableScreenReader = s.EnableScreenReader;
            }
            catch { /* defaults already set on the fields */ }

            // Apply font preferences live on open.
            ApplyFontSettings();
        }

        private static void Persist(Action<UserSettings> update)
        {
            try { SettingsService.Update(update); }
            catch { /* best-effort persistence */ }
        }

        private void ApplyLanguage()
        {
            // Persist the choice.
            try
            {
                var s = SettingsService.Load();
                s.LanguageIndex = _selectedLanguage;
                SettingsService.Save(s);
            }
            catch { /* best-effort persistence */ }

            // Apply the culture for formatting now; full UI string localization
            // takes effect on next launch (only languages with shipped resource
            // dictionaries change visible strings).
            try
            {
                var code = LanguageCultures[Math.Clamp(_selectedLanguage, 0, LanguageCultures.Length - 1)];
                var culture = new System.Globalization.CultureInfo(code);
                System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
                System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
            }
            catch { /* unsupported culture — keep current */ }
        }

        private void ApplyFontSettings()
        {
            // Font sizes: Small=11px, Medium=13px, Large=15px, Extra Large=17px
            double[] sizes = { 11, 13, 15, 17 };
            string[] families = { "Segoe UI", "Aptos", "Times New Roman", "Calibri" };

            var size = sizes[Math.Clamp(SelectedFontSize, 0, sizes.Length - 1)];
            var familyName = families[Math.Clamp(SelectedFontFamily, 0, families.Length - 1)];

            if (Application.Current != null)
            {
                // Apply font size globally to application resources
                Application.Current.Resources["GlobalFontSize"] = size;
                Application.Current.Resources["GlobalFontFamily"] = new FontFamily(familyName);
            }
        }

        private void ConfigureShortcuts()
        {
            var win = new ShortcutsWindow
            {
                DataContext = new ShortcutsViewModel()
            };
            win.Show();
        }
    }
}
