using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Serilog;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views.Settings
{
    public partial class SecuritySettingsView : UserControl
    {
        public SecuritySettingsView()
        {
            InitializeComponent();
        }

        private async void OpenTotpSettings_Click(object? sender, RoutedEventArgs e)
        {
            await HandleEventAsync(async () =>
            {
                var parentWindow = this.FindAncestorOfType<Window>();
                if (parentWindow != null)
                {
                    // Explicitly create with TotpService so verification works
                    var totpService = new PhantomVault.Core.Services.TotpService();
                    var viewModel = new TotpSettingsViewModel(totpService);

                    var totpWindow = new TotpSettingsWindow
                    {
                        DataContext = viewModel
                    };
                    await totpWindow.ShowDialog(parentWindow);
                }
            });
        }

        private async void OpenPasskeySettings_Click(object? sender, RoutedEventArgs e)
        {
            await HandleEventAsync(async () =>
            {
                var parentWindow = this.FindAncestorOfType<Window>();
                if (parentWindow != null)
                {
                    var app = (App)Avalonia.Application.Current!;
                    var viewModel = app.Services?.GetService(typeof(PasskeySettingsViewModel)) as PasskeySettingsViewModel;

                    var passkeyWindow = new PasskeySettingsWindow
                    {
                        DataContext = viewModel
                    };
                    await passkeyWindow.ShowDialog(parentWindow);
                }
            });
        }

        private async void OpenWindowsHelloSettings_Click(object? sender, RoutedEventArgs e)
        {
            await HandleEventAsync(async () =>
            {
                var parentWindow = this.FindAncestorOfType<Window>();
                if (parentWindow != null)
                {
                    var app = (App)Avalonia.Application.Current!;
                    var viewModel = app.Services?.GetService(typeof(WindowsHelloSettingsViewModel)) as WindowsHelloSettingsViewModel;

                    var windowsHelloWindow = new WindowsHelloSettingsWindow
                    {
                        DataContext = viewModel
                    };
                    await windowsHelloWindow.ShowDialog(parentWindow);
                }
            });
        }

        private async void OpenBackupCodes_Click(object? sender, RoutedEventArgs e)
        {
            await HandleEventAsync(async () =>
            {
                var parentWindow = this.FindAncestorOfType<Window>();
                if (parentWindow != null)
                {
                    var dialogService = new PhantomVault.UI.Services.DialogService();
                    await dialogService.ShowInfoAsync(
                        "Backup Codes",
                        "Emergency backup codes allow you to access your vault if you lose access to your other authentication methods.\n\nFeature coming soon.",
                        parentWindow);
                }
            });
        }

        /// <summary>
        /// Wrapper for async void event handlers to ensure exceptions are logged and displayed.
        /// </summary>
        private async Task HandleEventAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in async event handler");
                var parentWindow = this.FindAncestorOfType<Window>();
                var dialogService = new PhantomVault.UI.Services.DialogService();
                try
                {
                    await dialogService.ShowErrorAsync("Error", $"An error occurred: {ex.Message}", parentWindow);
                }
                catch
                {
                    Log.Fatal(ex, "Failed to show error dialog");
                }
            }
        }
    }
}
