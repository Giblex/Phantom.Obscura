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

        private async void ChangeMasterPassword_Click(object? sender, RoutedEventArgs e)
        {
            await HandleEventAsync(async () =>
            {
                var parentWindow = this.FindAncestorOfType<Window>();
                var dialogService = new PhantomVault.UI.Services.DialogService();
                // The current RekeyService rotates the keyfile but does NOT
                // change the passphrase to a new value, so there is no safe
                // backend to change the master password yet. Be honest rather
                // than silently leaving the password unchanged.
                await dialogService.ShowInfoAsync(
                    "Change Master Password",
                    "Master password change isn't available yet.\n\n" +
                    "The vault's key-rotation service currently rotates the keyfile " +
                    "but does not re-derive the vault key from a new passphrase, so " +
                    "changing the master password here would not actually take effect.\n\n" +
                    "This is tracked as a backend feature. For now, your existing " +
                    "master password remains in place.",
                    parentWindow);
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
                        "Recovery Codes",
                        "Emergency recovery codes are part of the vault's TOTP and recovery workflow, not a separate in-panel generator yet.\n\nUse TOTP setup for accounts that support recovery codes, or open the recovery flow if you need to regain access to a protected vault.",
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
