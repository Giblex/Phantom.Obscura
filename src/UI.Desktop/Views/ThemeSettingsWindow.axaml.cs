using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.Views
{
    public partial class ThemeSettingsWindow : ThemeAwareWindow
    {
        public ThemeSettingsWindow()
        {
            InitializeComponent();

            if (DataContext == null)
            {
                var app = Avalonia.Application.Current as App;
                var themeManager = app?.Services?.GetService(typeof(ThemeManagerService)) as ThemeManagerService;
                var runtimeThemeService = app?.Services?.GetService(typeof(IRuntimeThemeService)) as IRuntimeThemeService;
                DataContext = new ViewModels.Settings.ThemeSettingsViewModel(themeManager, runtimeThemeService);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public void Close_Click(object? sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
