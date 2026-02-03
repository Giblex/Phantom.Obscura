using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using PhantomVault.Core.Services;
using PhantomVault.UI.Services;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the welcome/landing page shown to first-time users.
    /// </summary>
    public class WelcomePageViewModel : ReactiveObject
    {
        private bool _isCheckingForVault;
        private bool _hasExistingVault;
        private string _statusMessage = string.Empty;
        private string? _detectedUsbPath;
        private readonly DialogService _dialogService;
        private Window? _ownerWindow;

        public event EventHandler<string>? NavigateToSecurityCheck;
        public event EventHandler? NavigateToSetupWizard;
#pragma warning disable CS0067 // events may only be raised in debug or legacy flows; suppress unused-event warning
        public event EventHandler? NavigateToUsbSetup;
        public event EventHandler? DeveloperBypassRequested;
        public event EventHandler? OpenInstallerRequested;
#pragma warning restore CS0067

        public WelcomePageViewModel()
        {
            _dialogService = new DialogService();

#if DEBUG
            IsDeveloperBypassVisible = true;
            DeveloperBypassCommand = ReactiveCommand.Create(ExecuteDeveloperBypass);
            OpenInstallerCommand = ReactiveCommand.Create(ExecuteOpenInstaller);
#else
            IsDeveloperBypassVisible = false;
            DeveloperBypassCommand = ReactiveCommand.Create(() => { });
            OpenInstallerCommand = ReactiveCommand.Create(() => { });
#endif

            GetStartedCommand = ReactiveCommand.CreateFromTask(GetStartedAsync);
            OpenExistingVaultCommand = ReactiveCommand.CreateFromTask(OpenExistingVaultAsync);

            // Check for existing vault on load
            Task.Run(CheckForExistingVaultAsync);
        }

        /// <summary>
        /// Sets the owner window for dialog display.
        /// </summary>
        public void SetOwnerWindow(Window window)
        {
            _ownerWindow = window;
        }

        public string AppName => "Phantom Vault";
        public string AppVersion => "5.0.0";
        public string WelcomeMessage => "Secure Password Manager with Post-Quantum Encryption";

        public string FeatureList =>
            "• Post-quantum cryptography (ML-KEM-768)\n" +
            "• USB-bound vault storage\n" +
            "• Hardware token support (YubiKey)\n" +
            "• VeraCrypt container integration\n" +
            "• KeePass import capability\n" +
            "• TOTP 2FA generator\n" +
            "• Password health monitoring\n" +
            "• Zero-knowledge architecture";

        public bool IsCheckingForVault
        {
            get => _isCheckingForVault;
            set => this.RaiseAndSetIfChanged(ref _isCheckingForVault, value);
        }

        public bool HasExistingVault
        {
            get => _hasExistingVault;
            set => this.RaiseAndSetIfChanged(ref _hasExistingVault, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public ReactiveCommand<Unit, Unit> GetStartedCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenExistingVaultCommand { get; }
        public ReactiveCommand<Unit, Unit> DeveloperBypassCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenInstallerCommand { get; }
        public bool IsDeveloperBypassVisible { get; }

#if DEBUG
        private void ExecuteDeveloperBypass()
        {
            StatusMessage = "Developer bypass active (debug build).";
            DeveloperBypassRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ExecuteOpenInstaller()
        {
            StatusMessage = "Opening installer (debug build).";
            OpenInstallerRequested?.Invoke(this, EventArgs.Empty);
        }
#endif

        private async Task CheckForExistingVaultAsync()
        {
            IsCheckingForVault = true;
            StatusMessage = "Checking for existing vaults...";

            try
            {
                // Check all available drives for vault manifest in hidden .phantom folder
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && d.DriveType == DriveType.Removable)
                    .ToList();

                foreach (var drive in drives)
                {
                    // Check for .phantom folder structure
                    var phantomPath = Path.Combine(drive.RootDirectory.FullName, ".phantom", "manifests");
                    if (Directory.Exists(phantomPath))
                    {
                        // Check if any manifest files exist
                        var manifestFiles = Directory.GetFiles(phantomPath, "*.manifest");
                        if (manifestFiles.Length > 0)
                        {
                            HasExistingVault = true;
                            _detectedUsbPath = drive.RootDirectory.FullName;
                            StatusMessage = $"Found existing vault on {drive.Name}";
                            await Task.Delay(1000); // Show message briefly
                            return;
                        }
                    }
                }

                StatusMessage = string.Empty;
                HasExistingVault = false;
                _detectedUsbPath = null;
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    "Vault Detection Error",
                    $"An error occurred while checking for existing vaults:\n\n{ex.Message}",
                    _ownerWindow);
                StatusMessage = $"Error checking for vaults: {ex.Message}";
                HasExistingVault = false;
            }
            finally
            {
                IsCheckingForVault = false;
            }
        }

        private async Task GetStartedAsync()
        {
            // Navigate to setup wizard for first-time users
            StatusMessage = "Starting setup wizard...";
            await Task.Delay(300);

            NavigateToSetupWizard?.Invoke(this, EventArgs.Empty);
        }

        private async Task OpenExistingVaultAsync()
        {
            if (string.IsNullOrEmpty(_detectedUsbPath))
            {
                await _dialogService.ShowWarningAsync(
                    "No Vault Found",
                    "No existing vault was detected. Please ensure your USB drive with the vault is connected.",
                    _ownerWindow);
                return;
            }

            // Navigate to security check screen for existing vault
            StatusMessage = "Opening existing vault...";
            await Task.Delay(500);

            NavigateToSecurityCheck?.Invoke(this, _detectedUsbPath);
        }
    }
}
