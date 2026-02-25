using System;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
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
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private async void OpenWindowsHelloSettings_Click(object? sender, RoutedEventArgs e)
        {
            await HandleEventAsync(async () =>
            {
                // Create Windows Hello settings window
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
                // Create Passkey settings window
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
                // Create TOTP settings window with service for code verification
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
                // Create Accessibility settings window
                var viewModel = new AccessibilitySettingsViewModel();
                var window = new AccessibilitySettingsWindow
                {
                    DataContext = viewModel
                };
                viewModel.SetOwnerWindow(window);

                await window.ShowDialog(this);
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
                var dialogService = new DialogService();
                try
                {
                    await dialogService.ShowErrorAsync("Error", $"An error occurred: {ex.Message}", this);
                }
                catch
                {
                    Log.Fatal(ex, "Failed to show error dialog");
                }
            }
        }
    }
}