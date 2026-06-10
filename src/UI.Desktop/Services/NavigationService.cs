using System;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using PhantomVault.Core.Services;
using PhantomVault.UI.Models;
using PhantomVault.UI.Views;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Services
{

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

        public void ShowWelcomePage()
        {
            var window = new WelcomePage();
            ShowWindow(window);
        }

        public void ShowUsbSetup()
        {
            var viewModel = new UsbSetupViewModel(_usbDetector);

            viewModel.GoBackCommand.Subscribe(_ => ShowWelcomePage());

            var window = new UsbSetupWindow
            {
                DataContext = viewModel
            };

            ShowWindow(window);
        }

        public void ShowSecurityCheck(string usbPath)
        {
            var launchRequest = new DetectedVaultLaunchRequest
            {
                UsbPath = usbPath,
                UsbDisplayName = usbPath,
                VaultPath = usbPath,
                DisplayName = "Detected vault",
                AutoOpenEligible = false
            };

            var window = new SecurityCheckScreen(_securityCheckService, launchRequest);

            if (window.DataContext is SecurityCheckScreenViewModel viewModel)
            {
                viewModel.ContinueCommand.Subscribe(_ => ShowVault(usbPath));
                viewModel.CancelCommand.Subscribe(_ => ShowWelcomePage());
            }

            ShowWindow(window);
        }

        public void ShowProvision(string usbPath)
        {
            var viewModel = _serviceProvider.GetRequiredService<ProvisionViewModel>();

            var window = new ProvisionWindow
            {
                DataContext = viewModel
            };

            ShowWindow(window);
        }

        public void ShowVault(string usbPath)
        {
            var viewModel = _serviceProvider.GetRequiredService<VaultViewModel>();

            var window = new VaultWindow
            {
                DataContext = viewModel
            };

            viewModel.SetOwnerWindow(window);

            ShowWindow(window);
        }

        private void ShowWindow(Window window)
        {
            var oldWindow = _currentWindow;
            _currentWindow = window;

            window.Show();

            oldWindow?.Close();
        }

        public Window? CurrentWindow => _currentWindow;
    }
}

