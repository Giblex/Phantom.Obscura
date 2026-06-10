using System;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Services.Security;

namespace PhantomVault.UI.Services
{

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

            _tamperDetection.TamperDetected += OnTamperDetected;
            _antiKeylogging.ThreatDetected += OnKeyloggingThreatDetected;
            _autofillSecurity.ThreatDetected += OnAutofillThreatDetected;
        }

        public bool IsMonitoring => _isMonitoring;

        public SecurityThreatLevel CurrentThreatLevel => _currentThreatLevel;

        public TamperDetectionService TamperDetection => _tamperDetection;

        public AntiKeyloggingService AntiKeylogging => _antiKeylogging;

        public MemoryProtectionService MemoryProtection => _memoryProtection;

        public AutofillSecurityService AutofillSecurity => _autofillSecurity;

        public event EventHandler<ThreatLevelChangedEventArgs>? ThreatLevelChanged;

        public event EventHandler<CriticalThreatEventArgs>? CriticalThreatDetected;

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

        public SecurityCheckResult PerformSecurityCheck()
        {
            var result = new SecurityCheckResult();

            result.TamperCheckResult = _tamperDetection.PerformCheck();

            result.KeyloggingCheckResult = _antiKeylogging.PerformCheck();

            result.AutofillCheckResult = _autofillSecurity.PerformSecurityCheck();

            result.ThreatLevel = DetermineThreatLevel(result);

            return result;
        }

        public async Task<SecurityActionResult> RespondToThreatAsync(SecurityCheckResult check)
        {
            var result = new SecurityActionResult();

            switch (check.ThreatLevel)
            {
                case SecurityThreatLevel.Critical:

                    result.Action = SecurityAction.ImmediateLockdown;
                    result.Message = "Critical security threat detected. Vault locked immediately.";
                    result.ShouldLockVault = true;
                    result.ShouldExitApplication = true;
                    break;

                case SecurityThreatLevel.High:

                    result.Action = SecurityAction.LockVault;
                    result.Message = "High security threat detected. Vault has been locked.";
                    result.ShouldLockVault = true;
                    result.ShouldExitApplication = false;
                    break;

                case SecurityThreatLevel.Medium:

                    result.Action = SecurityAction.WarnUser;
                    result.Message = "Security threat detected. Enhanced monitoring active.";
                    result.ShouldLockVault = false;
                    result.ShouldExitApplication = false;
                    break;

                case SecurityThreatLevel.Low:

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

        public void EnableMaximumSecurity()
        {
            _antiKeylogging.SecureInputEnabled = true;
            StartMonitoring();
        }

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

            if (check.TamperCheckResult.DebuggerDetected ||
                check.TamperCheckResult.RemoteDebuggerDetected ||
                check.TamperCheckResult.IntegrityViolated ||
                check.AutofillCheckResult.CredentialHarvestingDetected ||
                check.AutofillCheckResult.InvalidRequesterDetected)
            {
                return SecurityThreatLevel.Critical;
            }

            if (check.TamperCheckResult.UnknownModulesDetected ||
                check.KeyloggingCheckResult.KeyboardHookDetected ||
                check.AutofillCheckResult.FormInjectionDetected ||
                check.AutofillCheckResult.ProcessInjectionDetected)
            {
                return SecurityThreatLevel.High;
            }

            if (check.KeyloggingCheckResult.ScreenOverlayDetected ||
                check.KeyloggingCheckResult.ScreenCaptureDetected ||
                check.AutofillCheckResult.WindowManipulationDetected ||
                check.AutofillCheckResult.ClipboardHijackingDetected)
            {
                return SecurityThreatLevel.Medium;
            }

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

    public enum SecurityThreatLevel
    {
        None = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        Critical = 4
    }

    public enum SecurityAction
    {
        None,
        Monitor,
        WarnUser,
        LockVault,
        ImmediateLockdown
    }

    public sealed class SecurityCheckResult
    {
        public TamperCheckResult TamperCheckResult { get; set; } = new();
        public KeyloggingThreatCheckResult KeyloggingCheckResult { get; set; } = new();
        public AutofillSecurityCheckResult AutofillCheckResult { get; set; } = new();
        public SecurityThreatLevel ThreatLevel { get; set; }
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed class SecurityActionResult
    {
        public SecurityAction Action { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool ShouldLockVault { get; set; }
        public bool ShouldExitApplication { get; set; }
    }

    public sealed class ThreatLevelChangedEventArgs : EventArgs
    {
        public SecurityThreatLevel PreviousLevel { get; set; }
        public SecurityThreatLevel CurrentLevel { get; set; }
        public SecurityCheckResult Check { get; set; } = new();
    }

    public sealed class CriticalThreatEventArgs : EventArgs
    {
        public SecurityThreatLevel ThreatLevel { get; set; }
        public SecurityCheckResult Check { get; set; } = new();
        public DateTimeOffset Timestamp { get; set; }
    }
}

