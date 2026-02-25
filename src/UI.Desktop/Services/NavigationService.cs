using System;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using PhantomVault.Core.Services;
using PhantomVault.UI.Views;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Service for managing navigation between windows in the application.
    /// Handles window creation, display, and transitions.
    /// </summary>
    public class NavigationService
    {
        private Window? _currentWindow;
        private readonly UsbDetector _usbDetector;
        private readonly ManifestService _manifestService;
        private readonly SecurityCheckService _securityCheckService;
        private readonly IServiceProvider _serviceProvider;

        public NavigationService(
            UsbDetector usbDetector,
            ManifestService manifestService,
            SecurityCheckService securityCheckService,
            IServiceProvider serviceProvider)
        {
            _usbDetector = usbDetector ?? throw new ArgumentNullException(nameof(usbDetector));
            _manifestService = manifestService ?? throw new ArgumentNullException(nameof(manifestService));
            _securityCheckService = securityCheckService ?? throw new ArgumentNullException(nameof(securityCheckService));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        /// <summary>
        /// Navigate to the welcome page (first-time user experience).
        /// </summary>
        public void ShowWelcomePage()
        {
            var window = new WelcomePage();
            ShowWindow(window);
        }

        /// <summary>
        /// Navigate to USB setup window.
        /// </summary>
        public void ShowUsbSetup()
        {
            var viewModel = new UsbSetupViewModel(_usbDetector);

            // Wire up navigation commands
            viewModel.GoBackCommand.Subscribe(_ => ShowWelcomePage());
            // ContinueCommand navigation will be wired in Phase 4

            var window = new UsbSetupWindow
            {
                DataContext = viewModel
            };

            ShowWindow(window);
        }

        /// <summary>
        /// Navigate to security check screen.
        /// </summary>
        /// <param name="usbPath">Path to USB drive to validate</param>
        public void ShowSecurityCheck(string usbPath)
        {
            var window = new SecurityCheckScreen(_securityCheckService, usbPath);

            if (window.DataContext is SecurityCheckScreenViewModel viewModel)
            {
                viewModel.ContinueCommand.Subscribe(_ => ShowVault(usbPath));
                viewModel.CancelCommand.Subscribe(_ => ShowWelcomePage());
            }

            ShowWindow(window);
        }

        /// <summary>
        /// Navigate to provision window (set master password).
        /// </summary>
        /// <param name="usbPath">Path to USB drive for vault</param>
        public void ShowProvision(string usbPath)
        {
            var viewModel = _serviceProvider.GetRequiredService<ProvisionViewModel>();
            
            var window = new ProvisionWindow
            {
                DataContext = viewModel
            };
            
            ShowWindow(window);
        }

        /// <summary>
        /// Navigate to main vault window.
        /// </summary>
        /// <param name="usbPath">Path to USB drive with vault</param>
        public void ShowVault(string usbPath)
        {
            var viewModel = _serviceProvider.GetRequiredService<VaultViewModel>();
            
            var window = new VaultWindow
            {
                DataContext = viewModel
            };
            
            // Set the owner window reference
            viewModel.SetOwnerWindow(window);
            
            ShowWindow(window);
        }

        /// <summary>
        /// Show a window and close the current one.
        /// </summary>
        private void ShowWindow(Window window)
        {
            var oldWindow = _currentWindow;
            _currentWindow = window;

            window.Show();

            // Close old window after new one is shown
            oldWindow?.Close();
        }

        /// <summary>
        /// Get the currently active window.
        /// </summary>
        public Window? CurrentWindow => _currentWindow;
    }
}
