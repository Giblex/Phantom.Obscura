using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.Services;
using PhantomVault.UI.ViewModels;
using PhantomVault.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace PhantomVault.UI.Views
{
    public partial class MainWindow : ThemeAwareWindow
    {
        public MainWindow()
        {
            InitializeComponent();

            var settings = SettingsService.Load();
            WindowStateManager.RestoreMainWindowState(this, settings);

            this.Opened += OnOpened;

            WindowStateManager.AttachStateChangeHandlers(this, SaveWindowState);

            var toastContainer = this.FindControl<Panel>("ToastContainer");
            if (toastContainer != null)
            {
                PhantomVault.UI.Desktop.Services.ToastNotificationManager.Instance.Initialize(toastContainer);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void SaveWindowState()
        {
            SettingsService.Update(settings =>
            {
                WindowStateManager.SaveMainWindowState(this, settings);
                return 0;
            });
        }

        private void OnOpened(object? sender, EventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.OnRequestProvision += () =>
                {
                    var provisionWindow = new ProvisionWindow
                    {
                        DataContext = ((App)Application.Current!).Services!.GetRequiredService<ProvisionViewModel>()
                    };
                    provisionWindow.Show();
                };

                vm.OnRequestOpenVault += (_, args) =>
                {
                    _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        var app = (App)Application.Current!;
                        var serviceProvider = app.Services ?? throw new InvalidOperationException("Service provider not initialized.");
                        var vaultWindow = new VaultWindow
                        {
                            DataContext = serviceProvider.GetRequiredService<VaultViewModel>()
                        };

                        if (vaultWindow.DataContext is VaultViewModel vaultViewModel)
                        {
                            vaultViewModel.SetOwnerWindow(vaultWindow);
                            vaultViewModel.SetReauthContext(args.UsbRootPath, args.ManifestPath, args.ContainerAbsPath, args.KeyfilePath);
                            try
                            {
                                await vaultViewModel.LoadAsync(args.MountPath, args.Password, args.KeyfilePath);
                            }
                            catch (Exception ex)
                            {
                                var dialogService = serviceProvider.GetRequiredService<DialogService>();
                                await dialogService.ShowErrorAsync(
                                    "Vault Open Failed",
                                    $"Unable to load the vault contents:\n{ex.Message}",
                                    vaultWindow);
                                vaultWindow.Close();
                                return;
                            }
                        }

                        vaultWindow.Show();
                    });
                };
            }
        }

        public void OpenFrontPage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var app = (App)Application.Current!;
            var window = new FrontPageWindow
            {
                DataContext = app.Services!.GetRequiredService<FrontPageViewModel>()
            };
            window.Show();
        }

        public async void OpenProvisionWindow_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var app = (App)Application.Current!;

            // Creating an additional vault while one is already unlocked on this
            // device is the premium "multiple vaults per device" feature.
            if (DataContext is ViewModels.VaultViewModel vm && !vm.CanUseMultiVault)
            {
                var dialog = new Services.DialogService();
                await dialog.ShowInfoAsync(
                    "Premium feature",
                    "Running multiple vaults on one device is a Premium feature. " +
                    "Upgrade from Settings → Subscription to add another vault.",
                    this);
                if (vm.ShowSubscriptionSettingsCommand is System.Windows.Input.ICommand cmd && cmd.CanExecute(null))
                    cmd.Execute(null);
                return;
            }

            var window = new ProvisionWindow
            {
                DataContext = app.Services!.GetRequiredService<ProvisionViewModel>()
            };

            this.Show();
            window.Show();
        }

        public void OpenVaultUnlockWindow_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var window = new VaultUnlockWindow();
            window.Show();
        }

        public void OpenThemeSettings_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var app = (App)Application.Current!;
            var themeManager = app.Services!.GetRequiredService<ThemeManagerService>();
            var runtimeThemeService = app.Services!.GetRequiredService<IRuntimeThemeService>();
            var window = new ThemeSettingsWindow
            {
                DataContext = new ViewModels.Settings.ThemeSettingsViewModel(themeManager, runtimeThemeService)
            };
            window.Show();
        }

        public void OpenSecuritySettings_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var app = (App)Application.Current!;
            var window = new SecuritySettingsWindow
            {
                DataContext = app.Services!.GetRequiredService<SecuritySettingsViewModel>()
            };
            window.Show();
        }

        public async void OpenAutoFillSettings_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                var app = (App)Application.Current!;
                var window = new AutoFillSettingsWindow
                {
                    DataContext = app.Services!.GetRequiredService<AutoFillSettingsViewModel>()
                };
                window.Show();
            }
            catch (InvalidOperationException)
            {

                var dialog = new Window
                {
                    Title = "Not Available",
                    Width = 360,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new Avalonia.Controls.TextBlock
                    {
                        Text = "AutoFill settings are not available yet. This feature is still under development.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Margin = new Avalonia.Thickness(24),
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    }
                };
                await dialog.ShowDialog(this);
            }
        }

        public void OpenAddPasswordWindow_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var app = (App)Application.Current!;
            var window = new AddPasswordWindow
            {
                DataContext = app.Services!.GetRequiredService<AddPasswordViewModel>()
            };
            window.Show();
        }

        public void OpenUsbSetupWindow_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var app = (App)Application.Current!;
            var window = new UsbSetupWindow
            {
                DataContext = app.Services!.GetRequiredService<UsbSetupViewModel>()
            };
            window.Show();
        }

        public void OpenShareWindow_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var app = (App)Application.Current!;
            var window = new ShareWindow
            {
                DataContext = app.Services!.GetRequiredService<ShareViewModel>()
            };
            window.Show();
        }

        public void OpenPasswordHealthWindow_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var app = (App)Application.Current!;
            var window = new PasswordHealthWindow
            {
                DataContext = app.Services!.GetRequiredService<PasswordHealthViewModel>()
            };
            window.Show();
        }

        public void Exit_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {

            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
    }
}

