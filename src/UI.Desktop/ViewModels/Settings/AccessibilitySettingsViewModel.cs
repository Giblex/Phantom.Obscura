using System;
using System.Reactive;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using ReactiveUI;
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

        public int SelectedLanguage
        {
            get => _selectedLanguage;
            set => this.RaiseAndSetIfChanged(ref _selectedLanguage, value);
        }

        public int SelectedDefaultView
        {
            get => _selectedDefaultView;
            set => this.RaiseAndSetIfChanged(ref _selectedDefaultView, value);
        }

        public bool ShowEntryIcons
        {
            get => _showEntryIcons;
            set => this.RaiseAndSetIfChanged(ref _showEntryIcons, value);
        }

        public bool ShowCategoryColors
        {
            get => _showCategoryColors;
            set => this.RaiseAndSetIfChanged(ref _showCategoryColors, value);
        }

        public int SelectedFontSize
        {
            get => _selectedFontSize;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedFontSize, value);
                ApplyFontSettings();
            }
        }

        public int SelectedFontFamily
        {
            get => _selectedFontFamily;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedFontFamily, value);
                ApplyFontSettings();
            }
        }

        public bool EnableKeyboardShortcuts
        {
            get => _enableKeyboardShortcuts;
            set => this.RaiseAndSetIfChanged(ref _enableKeyboardShortcuts, value);
        }

        public bool FocusSearchOnOpen
        {
            get => _focusSearchOnOpen;
            set => this.RaiseAndSetIfChanged(ref _focusSearchOnOpen, value);
        }

        public bool EnableScreenReader
        {
            get => _enableScreenReader;
            set => this.RaiseAndSetIfChanged(ref _enableScreenReader, value);
        }

        public ICommand ConfigureShortcutsCommand { get; }

        public AccessibilitySettingsViewModel()
        {
            ConfigureShortcutsCommand = ReactiveCommand.Create(ConfigureShortcuts);
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
