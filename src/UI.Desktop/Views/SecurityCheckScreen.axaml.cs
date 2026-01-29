using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PhantomVault.Core.Services;
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

        public SecurityCheckScreen(SecurityCheckService securityCheckService, string usbPath) : this()
        {
            var viewModel = new SecurityCheckScreenViewModel(securityCheckService, usbPath);
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

        private void OnNavigateToVault(object? sender, string usbPath)
        {
            // Ensure we're on the UI thread
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try
                {
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
                var vaultLockDurationService = services.GetRequiredService<VaultLockDurationService>();
                var secureTrashService = services.GetRequiredService<SecureTrashService>();
                var veraCryptService = services.GetService<IVeraCryptService>() ?? new VeraCryptService();
                var iconManager = services.GetRequiredService<IconManager>();
                var usbDetector = services.GetRequiredService<UsbDetector>();

                // Create VaultUnlockViewModel and window
                var unlockViewModel = new ViewModels.VaultUnlockViewModel(
                    usbPath,
                    dialogService,
                    vaultLockDurationService,
                    secureTrashService,
                    veraCryptService,
                    iconManager,
                    usbDetector);
                    var unlockWindow = new VaultUnlockWindow(unlockViewModel);
                    unlockWindow.Show();

                    // Close security check screen
                    this.Close();
                }
                catch (Exception ex)
                {
                    // Show detailed error with stack trace to diagnose source quickly
                    var errorDialogService = new DialogService();
                    // Use GetAwaiter().GetResult() instead of .Wait() to avoid potential deadlocks
                    errorDialogService.ShowErrorAsync(
                        "Navigation Error",
                        $"Failed to open vault:\n\n{ex.Message}\n\n{ex.GetType().Name}\n{ex.StackTrace}",
                        this).GetAwaiter().GetResult();
                }
            });
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}


