using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PhantomVault.UI.Services.TrayBackground;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class AutoFillSettingsWindow : ThemeAwareWindow
    {
        public AutoFillSettingsWindow()
        {
            InitializeComponent();

            if (DataContext == null)
            {
                var app = Avalonia.Application.Current as App;
                var trayService = app?.Services?.GetService(typeof(ITrayBackgroundService)) as ITrayBackgroundService;
                DataContext = new AutoFillSettingsViewModel(trayService);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
