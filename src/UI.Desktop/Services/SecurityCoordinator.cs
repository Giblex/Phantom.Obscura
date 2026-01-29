using System;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Services.Security;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Coordinates all security services and monitors overall threat level.
    /// </summary>
    public sealed class SecurityCoordinator : IDisposable
    {
        private readonly TamperDetectionService _tamperDetection;
        private readonly AntiKeyloggingService _antiKeylogging;
        private readonly MemoryProtectionService _memoryProtection;
        private readonly AutofillSecurityService _autofillSecurity;
        private bool _isMonitoring;
        private SecurityThreatLevel _currentThreatLevel = SecurityThreatLevel.None;

        public SecurityCoordinator()
        {
            _tamperDetection = new TamperDetectionService();
            _antiKeylogging = new AntiKeyloggingService();
            _memoryProtection = new MemoryProtectionService();
            _autofillSecurity = new AutofillSecurityService();

            // Subscribe to threat detection events
            _tamperDetection.TamperDetected += OnTamperDetected;
            _antiKeylogging.ThreatDetected += OnKeyloggingThreatDetected;
            _autofillSecurity.ThreatDetected += OnAutofillThreatDetected;
        }

        /// <summary>
        /// Gets whether security monitoring is active.
        /// </summary>
        public bool IsMonitoring => _isMonitoring;

        /// <summary>
        /// Gets the current threat level.
        /// </summary>
        public SecurityThreatLevel CurrentThreatLevel => _currentThreatLevel;

        /// <summary>
        /// Gets the tamper detection service.
        /// </summary>
        public TamperDetectionService TamperDetection => _tamperDetection;

        /// <summary>
        /// Gets the anti-keylogging service.
        /// </summary>
        public AntiKeyloggingService AntiKeylogging => _antiKeylogging;

        /// <summary>
        /// Gets the memory protection service.
        /// </summary>
        public MemoryProtectionService MemoryProtection => _memoryProtection;

        /// <summary>
        /// Gets the autofill security service.
        /// </summary>
        public AutofillSecurityService AutofillSecurity => _autofillSecurity;

        /// <summary>
        /// Event raised when threat level changes.
        /// </summary>
        public event EventHandler<ThreatLevelChangedEventArgs>? ThreatLevelChanged;

        /// <summary>
        /// Event raised when a critical security threat is detected.
        /// </summary>
        public event EventHandler<CriticalThreatEventArgs>? CriticalThreatDetected;

        /// <summary>
        /// Starts all security monitoring services.
        /// </summary>
        public void StartMonitoring()
        {
            if (_isMonitoring)
                return;

            _isMonitoring = true;

            _tamperDetection.StartMonitoring();
            _antiKeylogging.StartMonitoring();
            _autofillSecurity.StartMonitoring();

            UpdateThreatLevel();
        }

        /// <summary>
        /// Stops all security monitoring services.
        /// </summary>
        public void StopMonitoring()
        {
            if (!_isMonitoring)
                return;

            _isMonitoring = false;

            _tamperDetection.StopMonitoring();
            _antiKeylogging.StopMonitoring();
            _autofillSecurity.StopMonitoring();

            _currentThreatLevel = SecurityThreatLevel.None;
        }

        /// <summary>
        /// Performs comprehensive security check.
        /// </summary>
        public SecurityCheckResult PerformSecurityCheck()
        {
            var result = new SecurityCheckResult();

            // Check tamper detection
            result.TamperCheckResult = _tamperDetection.PerformCheck();

            // Check keylogging threats
            result.KeyloggingCheckResult = _antiKeylogging.PerformCheck();

            // Check autofill security
            result.AutofillCheckResult = _autofillSecurity.PerformSecurityCheck();

            // Determine overall threat level
            result.ThreatLevel = DetermineThreatLevel(result);

            return result;
        }

        /// <summary>
        /// Handles response to detected threat (lockdown, notification, etc.).
        /// </summary>
        public async Task<SecurityActionResult> RespondToThreatAsync(SecurityCheckResult check)
        {
            var result = new SecurityActionResult();

            switch (check.ThreatLevel)
            {
                case SecurityThreatLevel.Critical:
                    // Critical threat - immediate lockdown
                    result.Action = SecurityAction.ImmediateLockdown;
                    result.Message = "Critical security threat detected. Vault locked immediately.";
                    result.ShouldLockVault = true;
                    result.ShouldExitApplication = true;
                    break;

                case SecurityThreatLevel.High:
                    // High threat - lock vault
                    result.Action = SecurityAction.LockVault;
                    result.Message = "High security threat detected. Vault has been locked.";
                    result.ShouldLockVault = true;
                    result.ShouldExitApplication = false;
                    break;

                case SecurityThreatLevel.Medium:
                    // Medium threat - warn user
                    result.Action = SecurityAction.WarnUser;
                    result.Message = "Security threat detected. Enhanced monitoring active.";
                    result.ShouldLockVault = false;
                    result.ShouldExitApplication = false;
                    break;

                case SecurityThreatLevel.Low:
                    // Low threat - log and monitor
                    result.Action = SecurityAction.Monitor;
                    result.Message = "Low security risk detected. Monitoring...";
                    result.ShouldLockVault = false;
                    result.ShouldExitApplication = false;
                    break;

                default:
                    result.Action = SecurityAction.None;
                    result.Message = "No security threats detected.";
                    break;
            }

            await Task.CompletedTask;
            return result;
        }

        /// <summary>
        /// Enables maximum security mode (all protections).
        /// </summary>
        public void EnableMaximumSecurity()
        {
            _antiKeylogging.SecureInputEnabled = true;
            StartMonitoring();
        }

        /// <summary>
        /// Disables security monitoring (not recommended).
        /// </summary>
        public void DisableSecurity()
        {
            StopMonitoring();
        }

        private void UpdateThreatLevel()
        {
            if (!_isMonitoring)
                return;

            var check = PerformSecurityCheck();
            
            if (check.ThreatLevel != _currentThreatLevel)
            {
                var previousLevel = _currentThreatLevel;
                _currentThreatLevel = check.ThreatLevel;

                ThreatLevelChanged?.Invoke(this, new ThreatLevelChangedEventArgs
                {
                    PreviousLevel = previousLevel,
                    CurrentLevel = _currentThreatLevel,
                    Check = check
                });

                // Raise critical threat event for high/critical levels
                if (_currentThreatLevel >= SecurityThreatLevel.High)
                {
                    CriticalThreatDetected?.Invoke(this, new CriticalThreatEventArgs
                    {
                        ThreatLevel = _currentThreatLevel,
                        Check = check,
                        Timestamp = DateTimeOffset.UtcNow
                    });
                }
            }
        }

        private SecurityThreatLevel DetermineThreatLevel(SecurityCheckResult check)
        {
            // Critical: Debugger, code integrity violation, or credential harvesting
            if (check.TamperCheckResult.DebuggerDetected ||
                check.TamperCheckResult.RemoteDebuggerDetected ||
                check.TamperCheckResult.IntegrityViolated ||
                check.AutofillCheckResult.CredentialHarvestingDetected ||
                check.AutofillCheckResult.InvalidRequesterDetected)
            {
                return SecurityThreatLevel.Critical;
            }

            // High: DLL injection, keyboard hook, form injection, or process injection
            if (check.TamperCheckResult.UnknownModulesDetected ||
                check.KeyloggingCheckResult.KeyboardHookDetected ||
                check.AutofillCheckResult.FormInjectionDetected ||
                check.AutofillCheckResult.ProcessInjectionDetected)
            {
                return SecurityThreatLevel.High;
            }

            // Medium: Screen overlay, screen capture, window manipulation, or clipboard hijacking
            if (check.KeyloggingCheckResult.ScreenOverlayDetected ||
                check.KeyloggingCheckResult.ScreenCaptureDetected ||
                check.AutofillCheckResult.WindowManipulationDetected ||
                check.AutofillCheckResult.ClipboardHijackingDetected)
            {
                return SecurityThreatLevel.Medium;
            }

            // Low: Timing anomaly, clipboard snooping, memory manipulation, or rapid requests
            if (check.TamperCheckResult.TimingAnomalyDetected ||
                check.KeyloggingCheckResult.ClipboardSnoopingDetected ||
                check.TamperCheckResult.MemoryManipulationDetected ||
                check.AutofillCheckResult.RapidRequestsDetected)
            {
                return SecurityThreatLevel.Low;
            }

            return SecurityThreatLevel.None;
        }

        private void OnTamperDetected(object? sender, TamperDetectedEventArgs e)
        {
            UpdateThreatLevel();
        }

        private void OnKeyloggingThreatDetected(object? sender, KeyloggingThreatEventArgs e)
        {
            UpdateThreatLevel();
        }

        private void OnAutofillThreatDetected(object? sender, AutofillSecurityEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[SecurityCoordinator] Autofill threat detected: {e.CheckResult.GetThreatDescription()}");
            UpdateThreatLevel();
        }

        public void Dispose()
        {
            _tamperDetection.TamperDetected -= OnTamperDetected;
            _antiKeylogging.ThreatDetected -= OnKeyloggingThreatDetected;
            _autofillSecurity.ThreatDetected -= OnAutofillThreatDetected;

            _tamperDetection?.Dispose();
            _antiKeylogging?.Dispose();
            _autofillSecurity?.Dispose();
            _memoryProtection?.Dispose();
        }
    }

    /// <summary>
    /// Security threat levels.
    /// </summary>
    public enum SecurityThreatLevel
    {
        None = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        Critical = 4
    }

    /// <summary>
    /// Security actions to take in response to threats.
    /// </summary>
    public enum SecurityAction
    {
        None,
        Monitor,
        WarnUser,
        LockVault,
        ImmediateLockdown
    }

    /// <summary>
    /// Result of comprehensive security check.
    /// </summary>
    public sealed class SecurityCheckResult
    {
        public TamperCheckResult TamperCheckResult { get; set; } = new();
        public KeyloggingThreatCheckResult KeyloggingCheckResult { get; set; } = new();
        public AutofillSecurityCheckResult AutofillCheckResult { get; set; } = new();
        public SecurityThreatLevel ThreatLevel { get; set; }
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Result of security action response.
    /// </summary>
    public sealed class SecurityActionResult
    {
        public SecurityAction Action { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool ShouldLockVault { get; set; }
        public bool ShouldExitApplication { get; set; }
    }

    /// <summary>
    /// Event args for threat level changes.
    /// </summary>
    public sealed class ThreatLevelChangedEventArgs : EventArgs
    {
        public SecurityThreatLevel PreviousLevel { get; set; }
        public SecurityThreatLevel CurrentLevel { get; set; }
        public SecurityCheckResult Check { get; set; } = new();
    }

    /// <summary>
    /// Event args for critical threats.
    /// </summary>
    public sealed class CriticalThreatEventArgs : EventArgs
    {
        public SecurityThreatLevel ThreatLevel { get; set; }
        public SecurityCheckResult Check { get; set; } = new();
        public DateTimeOffset Timestamp { get; set; }
    }
}
