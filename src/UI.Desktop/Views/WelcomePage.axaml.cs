using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PhantomVault.Core.Services;
using PhantomVault.UI.Services;
using PhantomVault.UI.ViewModels;
using Serilog;

namespace PhantomVault.UI.Views
{
    public partial class WelcomePage : ThemeAwareWindow
    {
        private WelcomePageViewModel? _currentViewModel;

        public WelcomePage()
        {
            // Fixed dark navy — pre-vault screens never follow user theme
            ThemeScope.SetIsThemed(this, false);

            InitializeComponent();

            // Set up navigation only when DataContext is assigned
            this.DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_currentViewModel != null)
            {
                _currentViewModel.NavigateToUsbSetup -= OnNavigateToUsbSetup;
                _currentViewModel.NavigateToSecurityCheck -= OnNavigateToSecurityCheck;
                _currentViewModel.NavigateToSetupWizard -= OnNavigateToSetupWizard;
                _currentViewModel.DeveloperBypassRequested -= OnDeveloperBypass;
            }

            if (DataContext is WelcomePageViewModel viewModel)
            {
                // Set owner window for dialogs
                viewModel.SetOwnerWindow(this);

                // Wire up navigation events
                viewModel.NavigateToUsbSetup += OnNavigateToUsbSetup;
                viewModel.NavigateToSecurityCheck += OnNavigateToSecurityCheck;
                viewModel.NavigateToSetupWizard += OnNavigateToSetupWizard;
                viewModel.DeveloperBypassRequested += OnDeveloperBypass;
                _currentViewModel = viewModel;
            }
            else
            {
                _currentViewModel = null;
            }
        }

        private void OnNavigateToSetupWizard(object? sender, EventArgs e)
        {
            try
            {
                var setupWizard = new SetupWizardWindow();
                var wizardVm = setupWizard.DataContext as SetupWizardViewModel;

                // When the ViewModel signals vault is ready to create, open the loading window
                if (wizardVm != null)
                {
                    wizardVm.VaultReadyForCreation += async (s, _) =>
                    {
                        try
                        {
                            // Already on UI thread (fired from button click) — no Dispatcher wrapping needed
                            var loadingWindow = new VaultCreationLoadingWindow();
                            loadingWindow.Show();
                            setupWizard.Hide();

                            loadingWindow.CreationCompleted += (lw, _) =>
                            {
                                try
                                {
                                    loadingWindow.Close();
                                    setupWizard.Close();
                                    OpenVaultWindowWithPreferencePrompt();
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex, "Failed to transition after vault creation");
                                    this.Show();
                                }
                            };

                            await loadingWindow.RunCreationAsync(wizardVm);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Vault creation loading window failed");
                            setupWizard.Show();
                        }
                    };
                }

                // Track whether vault creation was initiated so we know if wizard
                // was closed by user (X button) vs closed after successful creation.
                bool vaultCreationStarted = false;

                if (wizardVm != null)
                    wizardVm.VaultReadyForCreation += (_, __) => vaultCreationStarted = true;

                // When wizard closes without vault creation, show welcome page again
                setupWizard.Closed += (s, args) =>
                {
                    if (!vaultCreationStarted)
                    {
                        this.Show();
                    }
                    // Otherwise, vault creation flow will handle window transitions
                };

                setupWizard.Show();
                this.Hide();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open setup wizard");
                var dialogService = new DialogService();
                _ = dialogService.ShowErrorAsync(
                    "Setup Wizard Error",
                    $"Unable to open the setup wizard: {ex.Message}",
                    this);
            }
        }

        private async void OpenVaultWindowWithPreferencePrompt()
        {
            try
            {
                if (Application.Current is not App app || app.Services == null)
                    return;

                var services = app.Services;

                // Open VaultWindow (same pattern as developer bypass)
                var vaultViewModel = services.GetRequiredService<VaultViewModel>();
                var vaultWindow = new VaultWindow { DataContext = vaultViewModel };

                var vaultLockDurationService = services.GetRequiredService<VaultLockDurationService>();
                vaultLockDurationService.SetActiveSession(null);

                vaultViewModel.SetOwnerWindow(vaultWindow);
                vaultWindow.Show();

                // Transfer MainWindow before hiding so app doesn't shut down
                if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime dt)
                    dt.MainWindow = vaultWindow;

                this.Hide();

                // Check if this is first time (no unlock preference set yet)
                var settings = SettingsService.Load();

                if (string.IsNullOrEmpty(settings.VaultUnlockPreference))
                {
                    // Short delay so the vault window is fully rendered
                    await Task.Delay(600);

                    var preferenceDialog = new VaultUnlockPreferenceDialog();
                    var result = await preferenceDialog.ShowDialog<string?>(vaultWindow);

                    if (!string.IsNullOrEmpty(result))
                    {
                        settings.VaultUnlockPreference = result;

                        // Also apply the preference to related settings
                        switch (result)
                        {
                            case "Pin":
                                settings.EnablePinLock = true;
                                settings.DefaultUsePasskey = false;
                                break;
                            case "WindowsHello":
                                settings.DefaultUsePasskey = true;
                                settings.EnablePinLock = false;
                                break;
                            case "Automatic":
                                settings.DefaultUsePasskey = false;
                                settings.EnablePinLock = false;
                                break;
                        }

                        SettingsService.Save(settings);
                    }
                }

                // Close welcome page – vault is now the main window
                this.Close();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open vault window after creation");
                this.Show();
            }
        }

        private void OnNavigateToUsbSetup(object? sender, EventArgs e)
        {
            // Create USB detector and show USB setup window
            var usbDetector = new UsbDetector();
            var usbSetupViewModel = new UsbSetupViewModel(usbDetector);

            // Wire up navigation from USB setup
            usbSetupViewModel.NavigateBack += (s, args) =>
            {
                // Re-show the original WelcomePage instead of creating a new one
                this.Show();
                if (s is UsbSetupViewModel vm && Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    foreach (var window in desktop.Windows)
                    {
                        if (window is UsbSetupWindow)
                        {
                            window.Close();
                            break;
                        }
                    }
                }
            };

            usbSetupViewModel.NavigateToContinue += (s, usbPath) =>
            {
                // SKIP old installer flow - Go directly to ProvisionWindow with pre-selected USB

                var app = (App)Application.Current!;
                var provisionViewModel = ActivatorUtilities.CreateInstance<ProvisionViewModel>(app.Services!, usbPath);

                // Wire up navigation to vault window after vault creation
                provisionViewModel.NavigateToVault += (s, usbDrivePath) =>
                {
                    // Ensure we're on the UI thread
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        // Navigate to security check which will open the vault
                        OnNavigateToSecurityCheck(s, usbDrivePath);

                        // Close provision window
                        foreach (var window in Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop2 ? desktop2.Windows : Array.Empty<Window>())
                        {
                            if (window is ProvisionWindow)
                            {
                                window.Close();
                                break;
                            }
                        }
                    });
                };

                var provisionWindow = new ProvisionWindow
                {
                    DataContext = provisionViewModel
                };

                provisionViewModel.SetOwnerWindow(provisionWindow);
                provisionWindow.Show();

                // Close USB setup window
                if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    foreach (var window in desktop.Windows)
                    {
                        if (window is UsbSetupWindow)
                        {
                            window.Close();
                            break;
                        }
                    }
                }
            };

            var usbSetupWindow = new UsbSetupWindow
            {
                DataContext = usbSetupViewModel
            };

            // Set owner window for dialogs (Browse button)
            usbSetupViewModel.SetOwnerWindow(usbSetupWindow);

            usbSetupWindow.Show();
            this.Hide(); // Hide (not Close) — WelcomePage is MainWindow; closing it triggers app shutdown
        }

        private void OnNavigateToSecurityCheck(object? sender, string usbPath)
        {
            // Create services for security check
            var encryptionService = new EncryptionService();
            var containerService = new PhantomContainerService(encryptionService);
            var manifestService = new ManifestService(encryptionService, containerService);
            var securityCheckService = new SecurityCheckService(manifestService);

            var securityCheckScreen = new SecurityCheckScreen(securityCheckService, usbPath);
            securityCheckScreen.Show();
            this.Hide(); // Hide (not Close) — WelcomePage is MainWindow; closing it triggers app shutdown
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnDeveloperBypass(object? sender, EventArgs e)
        {
#if DEBUG
            try
            {
                if (Application.Current is not App app || app.Services == null)
                {
                    return;
                }

                var services = app.Services;
                var vaultViewModel = services.GetRequiredService<VaultViewModel>();
                var vaultWindow = new VaultWindow
                {
                    DataContext = vaultViewModel
                };

                var vaultLockDurationService = services.GetRequiredService<VaultLockDurationService>();
                vaultLockDurationService.SetActiveSession(null);

                vaultViewModel.SetOwnerWindow(vaultWindow);
                vaultViewModel.SetDeveloperBypassMode(true);
                vaultViewModel.VaultName = "Developer Vault";
                vaultViewModel.StatusMessage = "Developer bypass active (no vault mounted).";

                vaultWindow.Closed += (_, _) => this.Show();

                vaultWindow.Show();
                this.Hide();
            }
            catch (Exception ex)
            {
                try
                {
                    var dumpPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "PhantomVault_DevException.txt");
                    System.IO.File.WriteAllText(dumpPath, ex.ToString());
                }
                catch (Exception dumpEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[WelcomePage] Failed to write exception dump: {dumpEx.Message}");
                }

                var dialogService = new DialogService();
                _ = dialogService.ShowErrorAsync(
                    "Developer Bypass Failed",
                    $"Unable to open the vault window for development testing:\n{ex}\n\nFull exception written to your Desktop as 'PhantomVault_DevException.txt' (if possible).",
                    this);
            }
#endif
        }

        private async void About_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await HandleEventAsync(async () =>
            {
                var about = new AboutWindow();
                await about.ShowDialog(this);
            });
        }

        private void Settings_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var window = new VaultSettingsWindow
            {
                DataContext = new VaultSettingsViewModel()
            };
            if (window.DataContext is VaultSettingsViewModel vm) vm.SetOwnerWindow(window);
            window.Show();
        }

        private async void Import_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await HandleEventAsync(async () =>
            {
                var import = new ImportWindow();
                await import.ShowDialog(this);
            });
        }

        private async void Export_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await HandleEventAsync(async () =>
            {
                var export = new ExportWindow();
                await export.ShowDialog(this);
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

