using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using PhantomVault.UI.Services;
using PhantomVault.UI.ViewModels.Settings;

namespace PhantomVault.UI.Views.Settings
{
    public partial class ThemeSettingsView : UserControl
    {
        public ThemeSettingsView()
        {
            InitializeComponent();
            var viewModel = new ThemeSettingsViewModel();
            DataContext = viewModel;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            ApplyPreviewTheme(viewModel);
            DetachedFromVisualTree += (_, _) => viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not ThemeSettingsViewModel viewModel)
                return;

            if (e.PropertyName is nameof(ThemeSettingsViewModel.SelectedRuntimeThemeIndex)
                or nameof(ThemeSettingsViewModel.AccentColorHex))
            {
                ApplyPreviewTheme(viewModel);
            }
        }

        private void ApplyPreviewTheme(ThemeSettingsViewModel viewModel)
        {
            if (Application.Current is not App app ||
                app.Services?.GetService(typeof(IRuntimeThemeService)) is not IRuntimeThemeService themesService)
                return;

            var themes = themesService.GetThemes();
            if (viewModel.SelectedRuntimeThemeIndex < 0 || viewModel.SelectedRuntimeThemeIndex >= themes.Count)
                return;

            var theme = themes[viewModel.SelectedRuntimeThemeIndex];
            var resources = ThemePreviewSurface.Resources;
            resources.Clear();
            resources.MergedDictionaries.Clear();

            if (theme.Uri.Scheme == "avares")
            {
                resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://PhantomVault.UI"))
                {
                    Source = theme.Uri
                });
            }
            else
            {
                var xaml = File.ReadAllText(theme.Uri.LocalPath);
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xaml));
                if (AvaloniaRuntimeXamlLoader.Load(stream) is ResourceDictionary customTheme)
                    resources.MergedDictionaries.Add(customTheme);
            }

            // Accent selection is staged independently of the theme. Stamp all aliases
            // used by built-in and custom palettes into this local preview scope so the
            // sample controls show the exact pending combination before Save.
            var accent = Color.Parse(viewModel.AccentColorHex);
            var accentBrush = new SolidColorBrush(accent);
            resources["Color.Accent"] = accent;
            resources["AccentColor"] = accent;
            resources["UserAccentColor"] = accent;
            resources["AccentBrush"] = accentBrush;
            resources["Brush.Accent"] = accentBrush;
            resources["UserAccentBrush"] = accentBrush;
        }
    }
}

