using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PhantomVault.Core;
using PhantomVault.Core.Services;
using PhantomVault.UI.Models;
using PhantomVault.UI.Services;
using PhantomVault.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace PhantomVault.UI.Views
{
    public partial class SecurityCheckScreen : Window
    {
        private SecurityCheckScreenViewModel? _currentViewModel;

        public SecurityCheckScreen()
        {
            InitializeComponent();
            // Exclude from runtime theme switching - setup workflow remains unchanged
            ThemeScope.SetIsThemed(this, false);

            // Set up navigation when DataContext is assigned
            this.DataContextChanged += OnDataContextChanged;
        }

        public SecurityCheckScreen(SecurityCheckService securityCheckService, DetectedVaultLaunchRequest launchRequest) : this()
        {
            var viewModel = new SecurityCheckScreenViewModel(securityCheckService, launchRequest);
            viewModel.SetOwnerWindow(this);

            DataContext = viewModel;
            // Navigation will be wired up in OnDataContextChanged
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            // Unsubscribe from previous view model
            if (_currentViewModel != null)
            {
                _currentViewModel.NavigateToVault -= OnNavigateToVault;
            }

            // Subscribe to new view model
            if (DataContext is SecurityCheckScreenViewModel viewModel)
            {
                viewModel.SetOwnerWindow(this);
                viewModel.NavigateToVault += OnNavigateToVault;
                _currentViewModel = viewModel;
            }
        }

        private void OnNavigateToVault(object? sender, DetectedVaultLaunchRequest launchRequest)
        {
            // Ensure we're on the UI thread
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    var usbPath = launchRequest?.UsbPath ?? string.Empty;

                    // Defensive: validate inputs early
                    if (usbPath is null)
                        throw new ArgumentNullException(nameof(usbPath), "USB path was null when navigating to vault.");

                    // If empty path, user cancelled - just close and return to welcome
                    if (string.IsNullOrEmpty(usbPath))
                    {
                        this.Close();

                        // Show WelcomePage again
                        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                        {
                            foreach (var window in desktop.Windows)
                            {
                                if (window is WelcomePage welcomePage)
                                {
                                    welcomePage.Show();
                                    break;
                                }
                            }
                        }
                        return;
                    }

                    // Get required services from DI container
                    var app = (App)Application.Current!;
                    var services = app.Services ?? throw new InvalidOperationException("Service provider not initialized.");
                    var dialogService = services.GetRequiredService<DialogService>();

                    // SECURITY: Enforce USB policy at vault-access boundary
                    // This was deferred from startup so the front page is always reachable.
                    try
                    {
                        if (Program.PolicyService.IsUsbRequired())
                        {
                            var usbResult = Program.PolicyService.EnforceUsbPolicy();
                            System.Diagnostics.Debug.WriteLine(
                                $"[SecurityCheck] USB policy enforced. Drive={usbResult.drive?.Name ?? "none"} Serial={usbResult.volumeSerial ?? "n/a"}");
                        }
                    }
                    catch (PolicyViolationException pvEx)
                    {
                        await dialogService.ShowErrorAsync(
                            "Policy Violation",
                            $"USB policy enforcement failed: {pvEx.Message}",
                            this);
                        return;
                    }
                    catch (System.Security.SecurityException secEx)
                    {
                        await dialogService.ShowErrorAsync(
                            "Security Policy Error",
                            $"Security policy check failed: {secEx.Message}",
                            this);
                        return;
                    }

                    var vaultLockDurationService = services.GetRequiredService<VaultLockDurationService>();
                    Serilog.Log.Information("[Nav] resolved VaultLockDurationService");
                    var secureTrashService = services.GetRequiredService<SecureTrashService>();
                    Serilog.Log.Information("[Nav] resolved SecureTrashService");
                    var encryptionService = services.GetRequiredService<EncryptionService>();
                    Serilog.Log.Information("[Nav] resolved EncryptionService");
                    var iconManager = services.GetRequiredService<IconManager>();
                    Serilog.Log.Information("[Nav] resolved IconManager");
                    var usbDetector = services.GetRequiredService<UsbDetector>();
                    Serilog.Log.Information("[Nav] resolved UsbDetector — constructing VaultUnlockViewModel");

                    // Create VaultUnlockViewModel 
                    var unlockViewModel = new ViewModels.VaultUnlockViewModel(
                        usbPath,
                        dialogService,
                        vaultLockDurationService,
                        secureTrashService,
                        encryptionService,
                        iconManager,
                        usbDetector,
                        launchRequest?.VaultPath);
                    Serilog.Log.Information("[Nav] VaultUnlockViewModel constructed — creating VaultUnlockWindow");

                    // Show unlock window (it will auto-unlock if keyfile is present)
                    var unlockWindow = new VaultUnlockWindow(unlockViewModel);
                    Serilog.Log.Information("[Nav] VaultUnlockWindow constructed — calling Show()");
                    unlockWindow.Show();
                    Serilog.Log.Information("[Nav] Show() returned");

                    // Transfer MainWindow to unlock window so closing SecurityCheckScreen
                    // doesn't trigger app shutdown (Avalonia default: OnMainWindowClose)
                    if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime dt)
                        dt.MainWindow = unlockWindow;
                    Serilog.Log.Information("[Nav] MainWindow transferred — closing SecurityCheckScreen");

                    // Close security check screen
                    this.Close();
                    Serilog.Log.Information("[Nav] SecurityCheckScreen.Close() returned");
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "[Nav] OnNavigateToVault threw");
                    var errorDialogService = new DialogService();
                    await errorDialogService.ShowErrorAsync(
                        "Navigation Error",
                        $"Failed to open vault:\n\n{ex.Message}\n\n{ex.GetType().Name}\n{ex.StackTrace}",
                        this);
                }
            });
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}


