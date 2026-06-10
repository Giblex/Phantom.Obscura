using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services.Security
{

    public sealed class AutofillSecurityService : IDisposable
    {
        private Timer? _monitoringTimer;
        private bool _isMonitoring;
        private readonly object _lock = new object();

        private readonly Dictionary<IntPtr, WindowInfo> _trackedWindows = new();
        private readonly List<AutofillOperationInfo> _recentOperations = new();
        private readonly HashSet<string> _suspiciousProcesses = new();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("psapi.dll")]
        private static extern uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, StringBuilder lpFilename, int nSize);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_VM_READ = 0x0010;

        public event EventHandler<AutofillSecurityEventArgs>? ThreatDetected;
        public event EventHandler<AutofillOperationEventArgs>? SuspiciousOperation;

        public bool IsMonitoring => _isMonitoring;

        public AutofillSecurityService()
        {
            InitializeSuspiciousProcessList();
        }

        private void InitializeSuspiciousProcessList()
        {

            _suspiciousProcesses.Add("keylogger");
            _suspiciousProcesses.Add("spyware");
            _suspiciousProcesses.Add("formgrabber");
            _suspiciousProcesses.Add("banker");
            _suspiciousProcesses.Add("infostealer");
            _suspiciousProcesses.Add("credential");
            _suspiciousProcesses.Add("harvester");
            _suspiciousProcesses.Add("inject");
            _suspiciousProcesses.Add("hook");
        }

        public void StartMonitoring()
        {
            lock (_lock)
            {
                if (_isMonitoring) return;

                _monitoringTimer = new Timer(MonitoringCallback, null, TimeSpan.Zero, TimeSpan.FromSeconds(3));
                _isMonitoring = true;

                Debug.WriteLine("[AutofillSecurity] Monitoring started");
            }
        }

        public void StopMonitoring()
        {
            lock (_lock)
            {
                if (!_isMonitoring) return;

                _monitoringTimer?.Dispose();
                _monitoringTimer = null;
                _isMonitoring = false;

                Debug.WriteLine("[AutofillSecurity] Monitoring stopped");
            }
        }

        private void MonitoringCallback(object? state)
        {
            try
            {
                var result = PerformSecurityCheck();

                if (result.HasThreat)
                {
                    ThreatDetected?.Invoke(this, new AutofillSecurityEventArgs(result));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AutofillSecurity] Monitoring error: {ex.Message}");
            }
        }

        public AutofillSecurityCheckResult PerformSecurityCheck()
        {
            var result = new AutofillSecurityCheckResult
            {
                Timestamp = DateTime.UtcNow
            };

            result.FormInjectionDetected = DetectFormInjection();

            result.CredentialHarvestingDetected = DetectCredentialHarvesting();

            result.WindowManipulationDetected = DetectWindowManipulation();

            result.ClipboardHijackingDetected = DetectClipboardHijacking();

            result.ProcessInjectionDetected = DetectProcessInjection();

            result.InvalidRequesterDetected = DetectInvalidRequester();

            result.RapidRequestsDetected = DetectRapidAutofillRequests();

            result.ThreatLevel = DetermineThreatLevel(result);

            return result;
        }

        public AutofillValidationResult ValidateAutofillOperation(
            IntPtr targetWindow,
            string targetProcessName,
            string? targetUrl = null)
        {
            var result = new AutofillValidationResult
            {
                IsValid = true,
                TargetWindow = targetWindow,
                TargetProcessName = targetProcessName,
                TargetUrl = targetUrl
            };

            if (!IsWindowVisible(targetWindow))
            {
                result.IsValid = false;
                result.InvalidReason = "Target window is not visible";
                result.RiskLevel = AutofillRiskLevel.High;
                RaiseSuspiciousOperation("Autofill", targetProcessName, targetUrl, true, result.InvalidReason);
                return result;
            }

            if (IsSuspiciousProcess(targetProcessName))
            {
                result.IsValid = false;
                result.InvalidReason = $"Target process '{targetProcessName}' is flagged as suspicious";
                result.RiskLevel = AutofillRiskLevel.Critical;
                RaiseSuspiciousOperation("Autofill", targetProcessName, targetUrl, true, result.InvalidReason);
                return result;
            }

            var windowInfo = GetWindowInfo(targetWindow);
            result.WindowTitle = windowInfo.Title;
            result.WindowClassName = windowInfo.ClassName;

            if (DetectWindowSpoofing(windowInfo))
            {
                result.IsValid = false;
                result.InvalidReason = "Possible window spoofing detected";
                result.RiskLevel = AutofillRiskLevel.High;
                RaiseSuspiciousOperation("Autofill", targetProcessName, targetUrl, true, result.InvalidReason);
                return result;
            }

            if (!string.IsNullOrEmpty(targetUrl) && DetectPhishingUrl(targetUrl))
            {
                result.IsValid = false;
                result.InvalidReason = $"Suspicious URL detected: {targetUrl}";
                result.RiskLevel = AutofillRiskLevel.Critical;
                RaiseSuspiciousOperation("Autofill", targetProcessName, targetUrl, true, result.InvalidReason);
                return result;
            }

            if (IsRateLimitExceeded(targetWindow))
            {
                result.IsValid = false;
                result.InvalidReason = "Too many autofill requests in short time period";
                result.RiskLevel = AutofillRiskLevel.Medium;
                RaiseSuspiciousOperation("Autofill", targetProcessName, targetUrl, true, result.InvalidReason);
                return result;
            }

            TrackAutofillOperation(new AutofillOperationInfo
            {
                Timestamp = DateTime.UtcNow,
                TargetWindow = targetWindow,
                ProcessName = targetProcessName,
                Url = targetUrl,
                Success = true
            });

            result.RiskLevel = AutofillRiskLevel.Low;
            return result;
        }

        private bool DetectFormInjection()
        {

            var suspiciousCount = 0;

            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd)) return true;

                var className = GetWindowClassName(hWnd);
                var title = GetWindowTitle(hWnd);

                if (className.Contains("Edit", StringComparison.OrdinalIgnoreCase) ||
                    className.Contains("Input", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("login", StringComparison.OrdinalIgnoreCase))
                {
                    GetWindowThreadProcessId(hWnd, out var processId);
                    var processName = GetProcessName(processId);

                    if (IsSuspiciousProcess(processName))
                    {
                        suspiciousCount++;
                    }
                }

                return true;
            }, IntPtr.Zero);

            return suspiciousCount > 0;
        }

        private bool DetectCredentialHarvesting()
        {

            var processes = Process.GetProcesses();

            foreach (var process in processes)
            {
                try
                {
                    var processName = process.ProcessName.ToLowerInvariant();

                    if (_suspiciousProcesses.Any(s => processName.Contains(s)))
                    {
                        Debug.WriteLine($"[AutofillSecurity] Suspicious process detected: {process.ProcessName}");
                        return true;
                    }

                    if (HasSuspiciousMemoryPattern(process))
                    {
                        Debug.WriteLine($"[AutofillSecurity] Process with suspicious memory pattern: {process.ProcessName}");
                        return true;
                    }
                }
                catch
                {

                }
            }

            return false;
        }

        private bool DetectWindowManipulation()
        {
            var foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero) return false;

            lock (_lock)
            {
                if (_trackedWindows.TryGetValue(foregroundWindow, out var windowInfo))
                {

                    var currentInfo = GetWindowInfo(foregroundWindow);

                    if (windowInfo.ClassName != currentInfo.ClassName ||
                        windowInfo.Title != currentInfo.Title)
                    {
                        Debug.WriteLine("[AutofillSecurity] Window manipulation detected");
                        return true;
                    }
                }
                else
                {

                    _trackedWindows[foregroundWindow] = GetWindowInfo(foregroundWindow);
                }
            }

            return false;
        }

        private bool DetectClipboardHijacking()
        {

            var processes = Process.GetProcesses();
            var clipboardMonitors = 0;

            foreach (var process in processes)
            {
                try
                {
                    var processName = process.ProcessName.ToLowerInvariant();

                    if ((processName.Contains("clipboard") || processName.Contains("clip")) &&
                        !processName.Contains("clipboardhistory") &&
                        !processName.Contains("rdpclip"))
                    {
                        clipboardMonitors++;
                    }
                }
                catch (Exception ex)
                {

                    System.Diagnostics.Debug.WriteLine($"[AutofillSecurity] Failed to query process: {ex.Message}");
                }
            }

            return clipboardMonitors > 2;
        }

        private bool DetectProcessInjection()
        {

            var foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero) return false;

            GetWindowThreadProcessId(foregroundWindow, out var processId);

            var hProcess = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, processId);
            if (hProcess == IntPtr.Zero) return false;

            try
            {

                return false;
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }

        private bool DetectInvalidRequester()
        {
            var foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero) return true;

            GetWindowThreadProcessId(foregroundWindow, out var processId);
            var processName = GetProcessName(processId);

            return IsSuspiciousProcess(processName);
        }

        private bool DetectRapidAutofillRequests()
        {
            lock (_lock)
            {
                var recentCount = _recentOperations
                    .Count(op => op.Timestamp > DateTime.UtcNow.AddSeconds(-10));

                return recentCount > 5;
            }
        }

        private bool DetectWindowSpoofing(WindowInfo windowInfo)
        {

            if (windowInfo.Title.Contains("Google") && !windowInfo.ClassName.Contains("Chrome"))
                return true;

            if (windowInfo.Title.Contains("Microsoft") && !windowInfo.ClassName.Contains("ApplicationFrameWindow"))
                return true;

            return false;
        }

        private bool DetectPhishingUrl(string url)
        {

            var suspiciousPatterns = new[]
            {
                "secure-login",
                "verify-account",
                "update-password",
                "confirm-identity",
                ".tk", ".ml", ".ga", ".cf", ".gq"
            };

            return suspiciousPatterns.Any(pattern =>
                url.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsRateLimitExceeded(IntPtr window)
        {
            lock (_lock)
            {
                var recentForWindow = _recentOperations
                    .Count(op => op.TargetWindow == window &&
                                op.Timestamp > DateTime.UtcNow.AddSeconds(-5));

                return recentForWindow > 3;
            }
        }

        private void TrackAutofillOperation(AutofillOperationInfo operation)
        {
            lock (_lock)
            {
                _recentOperations.Add(operation);

                if (_recentOperations.Count > 100)
                {
                    _recentOperations.RemoveAt(0);
                }
            }
        }

        private bool IsSuspiciousProcess(string processName)
        {
            var lower = processName.ToLowerInvariant();
            return _suspiciousProcesses.Any(s => lower.Contains(s));
        }

        private bool HasSuspiciousMemoryPattern(Process process)
        {
            try
            {

                var privateMemoryMB = process.PrivateMemorySize64 / (1024 * 1024);

                if (process.ProcessName.Length < 10 && privateMemoryMB > 500)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {

                System.Diagnostics.Debug.WriteLine($"[AutofillSecurity] Memory pattern check failed: {ex.Message}");
            }

            return false;
        }

        private WindowInfo GetWindowInfo(IntPtr hWnd)
        {
            return new WindowInfo
            {
                Handle = hWnd,
                Title = GetWindowTitle(hWnd),
                ClassName = GetWindowClassName(hWnd),
                ProcessId = GetWindowProcessId(hWnd)
            };
        }

        private string GetWindowTitle(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, 256);
            return sb.ToString();
        }

        private string GetWindowClassName(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, 256);
            return sb.ToString();
        }

        private uint GetWindowProcessId(IntPtr hWnd)
        {
            GetWindowThreadProcessId(hWnd, out var processId);
            return processId;
        }

        private string GetProcessName(uint processId)
        {
            try
            {
                var process = Process.GetProcessById((int)processId);
                return process.ProcessName;
            }
            catch
            {
                return string.Empty;
            }
        }

        private AutofillThreatLevel DetermineThreatLevel(AutofillSecurityCheckResult result)
        {
            if (result.CredentialHarvestingDetected || result.InvalidRequesterDetected)
                return AutofillThreatLevel.Critical;

            if (result.FormInjectionDetected || result.ProcessInjectionDetected)
                return AutofillThreatLevel.High;

            if (result.WindowManipulationDetected || result.ClipboardHijackingDetected)
                return AutofillThreatLevel.Medium;

            if (result.RapidRequestsDetected)
                return AutofillThreatLevel.Low;

            return AutofillThreatLevel.None;
        }

        private void RaiseSuspiciousOperation(string operationType, string targetApp, string? targetUrl, bool wasBlocked, string reason)
        {
            SuspiciousOperation?.Invoke(this, new AutofillOperationEventArgs
            {
                OperationType = operationType,
                TargetApplication = targetApp,
                TargetUrl = targetUrl,
                WasBlocked = wasBlocked,
                Reason = reason
            });

            Debug.WriteLine($"[AutofillSecurity] Suspicious operation blocked: {reason} (App: {targetApp}, URL: {targetUrl})");
        }

        public void Dispose()
        {
            StopMonitoring();
        }

        private class WindowInfo
        {
            public IntPtr Handle { get; set; }
            public string Title { get; set; } = string.Empty;
            public string ClassName { get; set; } = string.Empty;
            public uint ProcessId { get; set; }
        }

        private class AutofillOperationInfo
        {
            public DateTime Timestamp { get; set; }
            public IntPtr TargetWindow { get; set; }
            public string ProcessName { get; set; } = string.Empty;
            public string? Url { get; set; }
            public bool Success { get; set; }
        }
    }

    public enum AutofillThreatLevel
    {
        None,
        Low,
        Medium,
        High,
        Critical
    }

    public enum AutofillRiskLevel
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class AutofillSecurityCheckResult
    {
        public DateTime Timestamp { get; set; }
        public bool FormInjectionDetected { get; set; }
        public bool CredentialHarvestingDetected { get; set; }
        public bool WindowManipulationDetected { get; set; }
        public bool ClipboardHijackingDetected { get; set; }
        public bool ProcessInjectionDetected { get; set; }
        public bool InvalidRequesterDetected { get; set; }
        public bool RapidRequestsDetected { get; set; }
        public AutofillThreatLevel ThreatLevel { get; set; }

        public bool HasThreat => ThreatLevel > AutofillThreatLevel.None;

        public string GetThreatDescription()
        {
            var threats = new List<string>();

            if (FormInjectionDetected) threats.Add("Form injection");
            if (CredentialHarvestingDetected) threats.Add("Credential harvesting");
            if (WindowManipulationDetected) threats.Add("Window manipulation");
            if (ClipboardHijackingDetected) threats.Add("Clipboard hijacking");
            if (ProcessInjectionDetected) threats.Add("Process injection");
            if (InvalidRequesterDetected) threats.Add("Invalid requester");
            if (RapidRequestsDetected) threats.Add("Rapid requests");

            return threats.Count > 0 ? string.Join(", ", threats) : "No threats detected";
        }
    }

    public class AutofillValidationResult
    {
        public bool IsValid { get; set; }
        public string InvalidReason { get; set; } = string.Empty;
        public AutofillRiskLevel RiskLevel { get; set; }
        public IntPtr TargetWindow { get; set; }
        public string TargetProcessName { get; set; } = string.Empty;
        public string? TargetUrl { get; set; }
        public string WindowTitle { get; set; } = string.Empty;
        public string WindowClassName { get; set; } = string.Empty;
    }

    public class AutofillSecurityEventArgs : EventArgs
    {
        public AutofillSecurityCheckResult CheckResult { get; }
        public DateTime Timestamp { get; }

        public AutofillSecurityEventArgs(AutofillSecurityCheckResult checkResult)
        {
            CheckResult = checkResult;
            Timestamp = DateTime.UtcNow;
        }
    }

    public class AutofillOperationEventArgs : EventArgs
    {
        public string OperationType { get; set; } = string.Empty;
        public string TargetApplication { get; set; } = string.Empty;
        public string? TargetUrl { get; set; }
        public bool WasBlocked { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

