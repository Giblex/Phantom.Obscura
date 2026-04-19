using System;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using PhantomVault.Core.Services;
using PhantomVault.UI.Models;
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
        private static readonly IBrush ActiveProgressBrush = new SolidColorBrush(Color.Parse("#6F92C8"));
        private static readonly IBrush SuccessProgressBrush = new SolidColorBrush(Color.Parse("#7FAE8F"));
        private static readonly IBrush FailureProgressBrush = new SolidColorBrush(Color.Parse("#B36A6A"));
        private static readonly IBrush PendingProgressBrush = new SolidColorBrush(Color.Parse("#30485A"));
        private static readonly IBrush PendingGlyphBrush = new SolidColorBrush(Color.Parse("#6E8391"));

        private readonly SecurityCheckService _securityCheckService;
        private string _usbPath = string.Empty;
        private readonly DetectedVaultLaunchRequest _launchRequest;

        private bool _isRunningChecks;
        private string _currentCheckName = "Initializing...";
        private string _currentCheckDisplay = "Initializing...";
        private bool _currentCheckPassed;
        private bool _currentCheckFailed;
        private int _percentComplete;
        private string _statusMessage = "Starting security checks...";
        private bool _checksComplete;
        private bool _checksSuccessful;
        private CoreSecurityCheckResult? _checkResult;
        private readonly DialogService _dialogService;
        private Window? _ownerWindow;

        public event EventHandler<DetectedVaultLaunchRequest>? NavigateToVault;

        public SecurityCheckScreenViewModel(SecurityCheckService securityCheckService, DetectedVaultLaunchRequest launchRequest)
        {
            _securityCheckService = securityCheckService ?? throw new ArgumentNullException(nameof(securityCheckService));
            _launchRequest = launchRequest ?? throw new ArgumentNullException(nameof(launchRequest));
            _usbPath = launchRequest.UsbPath;
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

        public string CurrentCheckDisplay
        {
            get => _currentCheckDisplay;
            set => this.RaiseAndSetIfChanged(ref _currentCheckDisplay, value);
        }

        public bool CurrentCheckPassed
        {
            get => _currentCheckPassed;
            set => this.RaiseAndSetIfChanged(ref _currentCheckPassed, value);
        }

        public bool CurrentCheckFailed
        {
            get => _currentCheckFailed;
            set => this.RaiseAndSetIfChanged(ref _currentCheckFailed, value);
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
                this.RaisePropertyChanged(nameof(SecuritySummaryHeadline));
                this.RaisePropertyChanged(nameof(SecuritySummaryDetail));
                this.RaisePropertyChanged(nameof(ManifestStatusText));
                this.RaisePropertyChanged(nameof(UsbHealthStatusText));
                this.RaisePropertyChanged(nameof(HardwareTokenStatusText));
                this.RaisePropertyChanged(nameof(BiometricStatusText));
                this.RaisePropertyChanged(nameof(AntiTamperStatusText));
                this.RaisePropertyChanged(nameof(DriveSnapshotText));
                this.RaisePropertyChanged(nameof(OverallProgressBrush));
                this.RaisePropertyChanged(nameof(ManifestProgress));
                this.RaisePropertyChanged(nameof(UsbHealthProgress));
                this.RaisePropertyChanged(nameof(HardwareProgress));
                this.RaisePropertyChanged(nameof(BiometricProgress));
                this.RaisePropertyChanged(nameof(AntiTamperProgress));
                this.RaisePropertyChanged(nameof(ManifestIsActive));
                this.RaisePropertyChanged(nameof(UsbHealthIsActive));
                this.RaisePropertyChanged(nameof(HardwareIsActive));
                this.RaisePropertyChanged(nameof(BiometricIsActive));
                this.RaisePropertyChanged(nameof(AntiTamperIsActive));
                this.RaisePropertyChanged(nameof(ManifestProgressBrush));
                this.RaisePropertyChanged(nameof(UsbHealthProgressBrush));
                this.RaisePropertyChanged(nameof(HardwareProgressBrush));
                this.RaisePropertyChanged(nameof(BiometricProgressBrush));
                this.RaisePropertyChanged(nameof(AntiTamperProgressBrush));
                this.RaisePropertyChanged(nameof(ManifestGlyph));
                this.RaisePropertyChanged(nameof(UsbHealthGlyph));
                this.RaisePropertyChanged(nameof(HardwareGlyph));
                this.RaisePropertyChanged(nameof(BiometricGlyph));
                this.RaisePropertyChanged(nameof(AntiTamperGlyph));
                this.RaisePropertyChanged(nameof(ManifestGlyphBrush));
                this.RaisePropertyChanged(nameof(UsbHealthGlyphBrush));
                this.RaisePropertyChanged(nameof(HardwareGlyphBrush));
                this.RaisePropertyChanged(nameof(BiometricGlyphBrush));
                this.RaisePropertyChanged(nameof(AntiTamperGlyphBrush));
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

        public string SecuritySummaryHeadline => CheckResult == null
            ? "Preparing checks"
            : CheckResult.OverallPassed
                ? "Vault passed the pre-open security checks"
                : "Review required before the vault can be trusted";

        public string SecuritySummaryDetail => CheckResult == null
            ? "Phantom Obscura is validating the device, vault structure, and tamper state."
            : $"Completed in {CheckResult.Duration.TotalSeconds:0.0}s with {CheckResult.ChecksPassed}/5 checks passing.";

        public string ManifestStatusText => BuildStatusText(CheckResult?.ManifestValid, "Manifest integrity confirmed", "Manifest integrity check failed");

        public string UsbHealthStatusText => BuildStatusText(CheckResult?.UsbHealthy, "USB device is writable and healthy", "USB health check failed");

        public string HardwareTokenStatusText => CheckResult == null
            ? "Checking hardware token support"
            : CheckResult.YubiKeyDetected
                ? "Hardware token detected"
                : "No hardware token detected";

        public string BiometricStatusText => CheckResult == null
            ? "Checking biometric availability"
            : CheckResult.BiometricAvailable
                ? "Biometric authentication available"
                : "Biometric authentication unavailable";

        public string AntiTamperStatusText => BuildStatusText(CheckResult?.NoTampering, "No tamper indicators detected", "Tamper indicators were detected");

        public string DriveSnapshotText => !HasUsbDriveInfo
            ? "Drive details will appear after the USB health check completes."
            : $"{UsbDriveFormat} • {UsbDriveFreeSpaceFormatted} free of {UsbDriveTotalSizeFormatted}";

        public IBrush OverallProgressBrush => !ChecksComplete
            ? ActiveProgressBrush
            : ChecksSuccessful
                ? SuccessProgressBrush
                : FailureProgressBrush;

        public double ManifestProgress => GetProgressForCheck("Manifest Integrity", CheckResult?.ManifestValid);
        public double UsbHealthProgress => GetProgressForCheck("USB Health", CheckResult?.UsbHealthy);
        public double HardwareProgress => GetProgressForCheck("Hardware Tokens", CheckResult != null);
        public double BiometricProgress => GetProgressForCheck("Biometric Availability", CheckResult != null);
        public double AntiTamperProgress => GetProgressForCheck("Anti-Tamper Check", CheckResult?.NoTampering);

        public bool ManifestIsActive => IsCheckActive("Manifest Integrity");
        public bool UsbHealthIsActive => IsCheckActive("USB Health");
        public bool HardwareIsActive => IsCheckActive("Hardware Tokens");
        public bool BiometricIsActive => IsCheckActive("Biometric Availability");
        public bool AntiTamperIsActive => IsCheckActive("Anti-Tamper Check");

        public IBrush ManifestProgressBrush => GetBrushForCheck(CheckResult?.ManifestValid, ManifestIsActive);
        public IBrush UsbHealthProgressBrush => GetBrushForCheck(CheckResult?.UsbHealthy, UsbHealthIsActive);
        public IBrush HardwareProgressBrush => GetBrushForCheck(CheckResult != null ? true : null, HardwareIsActive);
        public IBrush BiometricProgressBrush => GetBrushForCheck(CheckResult != null ? true : null, BiometricIsActive);
        public IBrush AntiTamperProgressBrush => GetBrushForCheck(CheckResult?.NoTampering, AntiTamperIsActive);

        public string ManifestGlyph => GetGlyphForCheck(CheckResult?.ManifestValid, ManifestIsActive);
        public string UsbHealthGlyph => GetGlyphForCheck(CheckResult?.UsbHealthy, UsbHealthIsActive);
        public string HardwareGlyph => GetGlyphForCheck(CheckResult != null ? true : null, HardwareIsActive);
        public string BiometricGlyph => GetGlyphForCheck(CheckResult != null ? true : null, BiometricIsActive);
        public string AntiTamperGlyph => GetGlyphForCheck(CheckResult?.NoTampering, AntiTamperIsActive);

        public IBrush ManifestGlyphBrush => GetGlyphBrushForCheck(CheckResult?.ManifestValid, ManifestIsActive);
        public IBrush UsbHealthGlyphBrush => GetGlyphBrushForCheck(CheckResult?.UsbHealthy, UsbHealthIsActive);
        public IBrush HardwareGlyphBrush => GetGlyphBrushForCheck(CheckResult != null ? true : null, HardwareIsActive);
        public IBrush BiometricGlyphBrush => GetGlyphBrushForCheck(CheckResult != null ? true : null, BiometricIsActive);
        public IBrush AntiTamperGlyphBrush => GetGlyphBrushForCheck(CheckResult?.NoTampering, AntiTamperIsActive);

        private static string BuildStatusText(bool? status, string successText, string failureText)
        {
            if (status == null)
                return "Pending";

            return status.Value ? successText : failureText;
        }

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

        private bool IsCheckActive(string expectedName)
        {
            if (ChecksComplete)
                return false;

            return string.Equals(CurrentCheckName, expectedName, StringComparison.OrdinalIgnoreCase);
        }

        private double GetProgressForCheck(string expectedName, bool? completedStatus)
        {
            if (completedStatus.HasValue || (expectedName is "Hardware Tokens" or "Biometric Availability" && CheckResult != null))
                return 100;

            return IsCheckActive(expectedName) ? 56 : 0;
        }

        private static IBrush GetBrushForCheck(bool? completedStatus, bool isActive)
        {
            if (completedStatus == true)
                return SuccessProgressBrush;

            if (completedStatus == false)
                return FailureProgressBrush;

            return isActive ? ActiveProgressBrush : PendingProgressBrush;
        }

        private static string GetGlyphForCheck(bool? completedStatus, bool isActive)
        {
            if (completedStatus == true)
                return "✓";

            if (completedStatus == false)
                return "!";

            return isActive ? "•" : "•";
        }

        private static IBrush GetGlyphBrushForCheck(bool? completedStatus, bool isActive)
        {
            if (completedStatus == true)
                return SuccessProgressBrush;

            if (completedStatus == false)
                return FailureProgressBrush;

            return isActive ? ActiveProgressBrush : PendingGlyphBrush;
        }

        private void NotifyCheckVisuals()
        {
            this.RaisePropertyChanged(nameof(OverallProgressBrush));
            this.RaisePropertyChanged(nameof(ManifestProgress));
            this.RaisePropertyChanged(nameof(UsbHealthProgress));
            this.RaisePropertyChanged(nameof(HardwareProgress));
            this.RaisePropertyChanged(nameof(BiometricProgress));
            this.RaisePropertyChanged(nameof(AntiTamperProgress));
            this.RaisePropertyChanged(nameof(ManifestIsActive));
            this.RaisePropertyChanged(nameof(UsbHealthIsActive));
            this.RaisePropertyChanged(nameof(HardwareIsActive));
            this.RaisePropertyChanged(nameof(BiometricIsActive));
            this.RaisePropertyChanged(nameof(AntiTamperIsActive));
            this.RaisePropertyChanged(nameof(ManifestProgressBrush));
            this.RaisePropertyChanged(nameof(UsbHealthProgressBrush));
            this.RaisePropertyChanged(nameof(HardwareProgressBrush));
            this.RaisePropertyChanged(nameof(BiometricProgressBrush));
            this.RaisePropertyChanged(nameof(AntiTamperProgressBrush));
            this.RaisePropertyChanged(nameof(ManifestGlyph));
            this.RaisePropertyChanged(nameof(UsbHealthGlyph));
            this.RaisePropertyChanged(nameof(HardwareGlyph));
            this.RaisePropertyChanged(nameof(BiometricGlyph));
            this.RaisePropertyChanged(nameof(AntiTamperGlyph));
            this.RaisePropertyChanged(nameof(ManifestGlyphBrush));
            this.RaisePropertyChanged(nameof(UsbHealthGlyphBrush));
            this.RaisePropertyChanged(nameof(HardwareGlyphBrush));
            this.RaisePropertyChanged(nameof(BiometricGlyphBrush));
            this.RaisePropertyChanged(nameof(AntiTamperGlyphBrush));
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

                        // Format display with status
                        if (update.IsComplete)
                        {
                            StatusMessage = "All checks complete!";
                            CurrentCheckDisplay = "All checks complete!";
                            CurrentCheckPassed = false;
                            CurrentCheckFailed = false;
                        }
                        else if (update.CheckFailed)
                        {
                            CurrentCheckDisplay = $"{update.CurrentCheck}: Failed";
                            StatusMessage = $"Check failed: {update.CurrentCheck}";
                            CurrentCheckPassed = false;
                            CurrentCheckFailed = true;
                        }
                        else if (update.CheckPassed && update.PercentComplete > 0)
                        {
                            CurrentCheckDisplay = $"{update.CurrentCheck}: Passed";
                            StatusMessage = $"Running check: {update.CurrentCheck}...";
                            CurrentCheckPassed = true;
                            CurrentCheckFailed = false;
                        }
                        else
                        {
                            CurrentCheckDisplay = $"{update.CurrentCheck}...";
                            StatusMessage = $"Running check: {update.CurrentCheck}...";
                            CurrentCheckPassed = false;
                            CurrentCheckFailed = false;
                        }

                        NotifyCheckVisuals();
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

                    NotifyCheckVisuals();
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
                    NotifyCheckVisuals();

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
            NavigateToVault?.Invoke(this, _launchRequest);
        }

        private void OnCancel()
        {
            StatusMessage = "Cancelled";

            // Close the window by invoking with empty string (signals cancel)
            NavigateToVault?.Invoke(this, new DetectedVaultLaunchRequest
            {
                UsbPath = string.Empty,
                UsbDisplayName = string.Empty,
                VaultPath = string.Empty,
                DisplayName = string.Empty
            });
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
