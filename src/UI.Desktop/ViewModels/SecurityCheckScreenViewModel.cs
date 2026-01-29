using System;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using PhantomVault.Core.Services;
using PhantomVault.UI.Services;
using ReactiveUI;
using CoreSecurityCheckResult = PhantomVault.Core.Services.SecurityCheckResult;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the security check screen that validates vault integrity.
    /// </summary>
    public class SecurityCheckScreenViewModel : ReactiveObject
    {
        private readonly SecurityCheckService _securityCheckService;
        private string _usbPath = string.Empty;

        private bool _isRunningChecks;
        private string _currentCheckName = "Initializing...";
        private int _percentComplete;
        private string _statusMessage = "Starting security checks...";
        private bool _checksComplete;
        private bool _checksSuccessful;
        private CoreSecurityCheckResult? _checkResult;
        private readonly DialogService _dialogService;
        private Window? _ownerWindow;

        public event EventHandler<string>? NavigateToVault;

        public SecurityCheckScreenViewModel(SecurityCheckService securityCheckService, string usbPath)
        {
            _securityCheckService = securityCheckService ?? throw new ArgumentNullException(nameof(securityCheckService));
            _usbPath = usbPath;
            _dialogService = new DialogService();

            var canContinue = this.WhenAnyValue(
                x => x.ChecksComplete,
                x => x.ChecksSuccessful,
                (complete, successful) => complete && successful);

            ContinueCommand = ReactiveCommand.Create(OnContinue, canContinue);

            RetryCommand = ReactiveCommand.CreateFromTask(RunSecurityChecksAsync,
                this.WhenAnyValue(x => x.ChecksComplete));

            CancelCommand = ReactiveCommand.Create(OnCancel);

            // Start checks automatically - but ensure property updates happen on UI thread
            _ = RunSecurityChecksAsync();
        }

        public bool IsRunningChecks
        {
            get => _isRunningChecks;
            set => this.RaiseAndSetIfChanged(ref _isRunningChecks, value);
        }

        public string CurrentCheckName
        {
            get => _currentCheckName;
            set => this.RaiseAndSetIfChanged(ref _currentCheckName, value);
        }

        public int PercentComplete
        {
            get => _percentComplete;
            set => this.RaiseAndSetIfChanged(ref _percentComplete, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public bool ChecksComplete
        {
            get => _checksComplete;
            set => this.RaiseAndSetIfChanged(ref _checksComplete, value);
        }

        public bool ChecksSuccessful
        {
            get => _checksSuccessful;
            set => this.RaiseAndSetIfChanged(ref _checksSuccessful, value);
        }

        public CoreSecurityCheckResult? CheckResult
        {
            get => _checkResult;
            set
            {
                this.RaiseAndSetIfChanged(ref _checkResult, value);
                this.RaisePropertyChanged(nameof(ChecksPassedText));
                this.RaisePropertyChanged(nameof(DetailedCheckResults));
                this.RaisePropertyChanged(nameof(ErrorsText));
                this.RaisePropertyChanged(nameof(WarningsText));
                // Also notify computed helper properties used by XAML bindings
                this.RaisePropertyChanged(nameof(HasErrors));
                this.RaisePropertyChanged(nameof(HasWarnings));
                this.RaisePropertyChanged(nameof(HasUsbDriveInfo));
                this.RaisePropertyChanged(nameof(UsbDriveFormat));
                this.RaisePropertyChanged(nameof(UsbDriveTotalSizeFormatted));
                this.RaisePropertyChanged(nameof(UsbDriveFreeSpaceFormatted));
            }
        }

        // Helper properties for safe XAML bindings
        public bool HasErrors => CheckResult?.Errors?.Count > 0;

        public bool HasWarnings => CheckResult?.Warnings?.Count > 0;

        public bool HasUsbDriveInfo => CheckResult?.UsbDriveInfo != null;

        public string UsbDriveFormat => CheckResult?.UsbDriveInfo?.DriveFormat ?? string.Empty;

        public string UsbDriveTotalSizeFormatted => CheckResult?.UsbDriveInfo?.TotalSizeFormatted ?? string.Empty;

        public string UsbDriveFreeSpaceFormatted => CheckResult?.UsbDriveInfo?.FreeSpaceFormatted ?? string.Empty;

        public string ChecksPassedText => CheckResult != null
            ? $"{CheckResult.ChecksPassed} of 5 checks passed"
            : string.Empty;

        public string DetailedCheckResults => CheckResult != null
            ? GetDetailedCheckResults()
            : string.Empty;

        public string ErrorsText => CheckResult?.Errors.Count > 0
            ? string.Join("\n", CheckResult.Errors)
            : string.Empty;

        public string WarningsText => CheckResult?.Warnings.Count > 0
            ? string.Join("\n", CheckResult.Warnings)
            : string.Empty;

        private string GetDetailedCheckResults()
        {
            if (CheckResult == null) return string.Empty;

            var results = new System.Text.StringBuilder();

            // Manifest Integrity (CRITICAL)
            results.AppendLine(CheckResult.ManifestValid
                ? "✓ Manifest Integrity: PASSED"
                : "✗ Manifest Integrity: FAILED");
            if (!CheckResult.ManifestValid)
                results.AppendLine("  → Fix: Ensure vault files exist in the .phantom folder, or create a new vault.");

            // USB Health (CRITICAL)
            results.AppendLine(CheckResult.UsbHealthy
                ? "✓ USB Health: PASSED"
                : "✗ USB Health: FAILED");
            if (!CheckResult.UsbHealthy)
                results.AppendLine("  → Fix: Check USB connection, ensure drive is writable, and has at least 10MB free space.");

            // Hardware Tokens (OPTIONAL - always shows as info)
            if (CheckResult.YubiKeyDetected)
            {
                results.AppendLine("✓ Hardware Token: YubiKey detected and ready");
            }
            else
            {
                results.AppendLine("ℹ Hardware Token: None detected (optional feature)");
                results.AppendLine("  → Info: YubiKey support is available but not required. Vault works without it.");
            }

            // Biometric Availability (OPTIONAL - always shows as info)
            if (CheckResult.BiometricAvailable)
            {
                results.AppendLine("✓ Biometric Auth: Available on this device");
            }
            else
            {
                results.AppendLine("ℹ Biometric Auth: Not available (optional feature)");
                results.AppendLine("  → Info: Windows Hello/biometric auth not detected. Use password authentication.");
            }

            // Anti-Tamper (CRITICAL)
            results.AppendLine(CheckResult.NoTampering
                ? "✓ Anti-Tamper Check: PASSED"
                : "✗ Anti-Tamper Check: FAILED");
            if (!CheckResult.NoTampering)
                results.AppendLine("  → Fix: Review vault files for missing or suspicious content. Consider restoring from backup.");

            return results.ToString();
        }

        public ReactiveCommand<Unit, Unit> ContinueCommand { get; }
        public ReactiveCommand<Unit, Unit> RetryCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

        private async Task RunSecurityChecksAsync()
        {
            IsRunningChecks = true;
            ChecksComplete = false;
            ChecksSuccessful = false;
            PercentComplete = 0;
            StatusMessage = "Starting security checks...";

            try
            {
                var progress = new Progress<SecurityCheckProgress>(update =>
                {
                    // Marshal property updates to UI thread
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        CurrentCheckName = update.CurrentCheck;
                        PercentComplete = update.PercentComplete;

                        if (update.IsComplete)
                        {
                            StatusMessage = "All checks complete!";
                        }
                        else
                        {
                            StatusMessage = $"Running check: {update.CurrentCheck}...";
                        }
                    });
                });

                CheckResult = await _securityCheckService.RunSecurityChecksAsync(_usbPath, progress);

                // Marshal property updates to UI thread - NO DIALOGS, just update state
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ChecksComplete = true;
                    ChecksSuccessful = CheckResult.OverallPassed;

                    if (CheckResult.OverallPassed)
                    {
                        StatusMessage = "Security checks passed! Click Continue to open your vault.";
                    }
                    else
                    {
                        StatusMessage = $"{CheckResult.ChecksPassed}/5 checks passed. Review details below.";
                    }
                });

                // No auto-popup dialogs - user reviews results and clicks Continue/Retry manually
            }
            catch (Exception ex)
            {
                // Marshal all property updates and dialog to UI thread
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    ChecksComplete = true;
                    ChecksSuccessful = false;
                    StatusMessage = $"Error during security checks: {ex.Message}";

                    await _dialogService.ShowErrorAsync(
                        "Security Check Error",
                        $"An error occurred while running security checks: {ex.Message}",
                        _ownerWindow);
                });
            }
            finally
            {
                // Marshal property update to UI thread
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsRunningChecks = false;
                });
            }
        }

        private void OnContinue()
        {
            StatusMessage = "Opening vault...";

            // Invoke navigation event - the SecurityCheckScreen will handle opening the vault
            NavigateToVault?.Invoke(this, _usbPath);
        }

        private void OnCancel()
        {
            StatusMessage = "Cancelled";

            // Close the window by invoking with empty string (signals cancel)
            NavigateToVault?.Invoke(this, string.Empty);
        }

        /// <summary>
        /// Sets the owner window for dialog display.
        /// </summary>
        public void SetOwnerWindow(Window window)
        {
            _ownerWindow = window;
        }
    }
}
