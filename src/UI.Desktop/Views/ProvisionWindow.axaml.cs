using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PhantomVault.UI.ViewModels;
using PhantomVault.UI.Services;
using Serilog;

namespace PhantomVault.UI.Views
{
    public partial class ProvisionWindow : Window
    {
        public ProvisionWindow()
        {
            InitializeComponent();

            // Set the owner window for the ViewModel
            this.Opened += (s, e) =>
            {
                if (DataContext is ProvisionViewModel viewModel)
                {
                    viewModel.SetOwnerWindow(this);
                }
            };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void GoBack_Click(object? sender, RoutedEventArgs e)
        {
            // Return to WelcomePage
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow?.Show();
                desktop.MainWindow?.Activate();
            }
            Close();
        }

        private async void ConfigureWindowsHello_Click(object? sender, RoutedEventArgs e)
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

        private async void ConfigurePasskeys_Click(object? sender, RoutedEventArgs e)
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

        private async void ConfigureTotp_Click(object? sender, RoutedEventArgs e)
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