using System;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PhantomVault.Core.Services.Security;
using PhantomVault.Core.Services;
using PhantomVault.UI.ViewModels;
using PhantomVault.UI.Services;
using Serilog;

namespace PhantomVault.UI.Views
{
    public partial class SettingsWindow : ThemeAwareWindow
    {
        public SettingsWindow()
        {
            InitializeComponent();

            if (DataContext == null)
            {
                var sp = (Avalonia.Application.Current as App)?.Services;
                var defenceSettings = sp?.GetService(typeof(IDefenceSettingsService)) as IDefenceSettingsService;
                var recoveryActivationVm = sp?.GetService(typeof(PhantomVault.UI.ViewModels.Settings.RecoveryActivationSettingsViewModel))
                    as PhantomVault.UI.ViewModels.Settings.RecoveryActivationSettingsViewModel;
                var updateService = sp?.GetService(typeof(PhantomVault.Core.Services.Update.IUpdateService))
                    as PhantomVault.Core.Services.Update.IUpdateService;
                var updateSettings = sp?.GetService(typeof(PhantomVault.Core.Services.Update.UpdateChannelSettings))
                    as PhantomVault.Core.Services.Update.UpdateChannelSettings;
                DataContext = new SettingsViewModel(defenceSettings, recoveryActivationVm, updateService, updateSettings);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private async void OpenWindowsHelloSettings_Click(object? sender, RoutedEventArgs e)
        {
            await HandleEventAsync(async () =>
            {

                var viewModel = new WindowsHelloSettingsViewModel();
                var window = new WindowsHelloSettingsWindow
                {
                    DataContext = viewModel
                };

                await window.ShowDialog(this);
            });
        }

        private async void OpenPasskeySettings_Click(object? sender, RoutedEventArgs e)
        {
            await HandleEventAsync(async () =>
            {

                var viewModel = new PasskeySettingsViewModel();
                var window = new PasskeySettingsWindow
                {
                    DataContext = viewModel
                };

                await window.ShowDialog(this);
            });
        }

        private async void OpenTotpSettings_Click(object? sender, RoutedEventArgs e)
        {
            await HandleEventAsync(async () =>
            {

                var totpService = new PhantomVault.Core.Services.TotpService();
                var viewModel = new TotpSettingsViewModel(totpService);
                var window = new TotpSettingsWindow
                {
                    DataContext = viewModel
                };

                await window.ShowDialog(this);
            });
        }

        private async void OpenAccessibilitySettings_Click(object? sender, RoutedEventArgs e)
        {
            await HandleEventAsync(async () =>
            {

                var viewModel = new AccessibilitySettingsViewModel();
                var window = new AccessibilitySettingsWindow
                {
                    DataContext = viewModel
                };
                viewModel.SetOwnerWindow(window);

                await window.ShowDialog(this);
            });
        }

        private async void OpenThemeSettings_Click(object? sender, RoutedEventArgs e)
        {
            await HandleEventAsync(async () =>
            {
                var app = (App)Avalonia.Application.Current!;
                var themeManager = app.Services?.GetService(typeof(ThemeManagerService)) as ThemeManagerService;
                var runtimeThemeService = app.Services?.GetService(typeof(IRuntimeThemeService)) as IRuntimeThemeService;

                var window = new ThemeSettingsWindow
                {
                    DataContext = new PhantomVault.UI.ViewModels.Settings.ThemeSettingsViewModel(themeManager, runtimeThemeService)
                };

                await window.ShowDialog(this);
            });
        }

        private async Task HandleEventAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in async event handler");
                var dialogService = new DialogService();
                try
                {
                    Log.Error(ex, "[SettingsWindow] Command failed unexpectedly.");
                    await dialogService.ShowErrorAsync("Error", "The settings operation could not be completed. Try again.", this);
                }
                catch
                {
                    Log.Fatal(ex, "Failed to show error dialog");
                }
            }
        }
    }
}

