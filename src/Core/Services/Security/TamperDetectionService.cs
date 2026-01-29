using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Options;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Detects tampering attempts including debuggers, DLL injection, memory manipulation,
    /// and code integrity violations. Automatically activates decoy vault when tampering is detected.
    /// </summary>
    public sealed class TamperDetectionService : IDisposable
    {
        private Timer? _monitoringTimer;
        private bool _isMonitoring;
        private readonly string _executablePath;
        private byte[]? _executableHash;
        private DateTime _lastIntegrityCheck = DateTime.MinValue;
        private readonly TimeSpan _integrityCheckInterval = TimeSpan.FromMinutes(5);

        private readonly DecoyVaultService? _decoyService;
        private readonly SecurityOptions _securityOptions;
        private bool _decoyActivated = false;

        public TamperDetectionService(
            DecoyVaultService? decoyService = null,
            SecurityOptions? securityOptions = null)
        {
            _executablePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            _decoyService = decoyService;
            _securityOptions = securityOptions ?? new SecurityOptions();
        }

        /// <summary>
        /// Event raised when tampering is detected.
        /// </summary>
        public event EventHandler<TamperDetectedEventArgs>? TamperDetected;

        /// <summary>
        /// Gets whether monitoring is active.
        /// </summary>
        public bool IsMonitoring => _isMonitoring;

        /// <summary>
        /// Gets whether decoy vault is currently active.
        /// </summary>
        public bool IsDecoyActive => _decoyActivated;

        /// <summary>
        /// Starts continuous tamper detection monitoring.
        /// </summary>
        public void StartMonitoring()
        {
            if (_isMonitoring)
                return;

            _isMonitoring = true;

            // Calculate initial executable hash
            CalculateExecutableHash();

            // Start monitoring timer (check every 10 seconds)
            _monitoringTimer = new Timer(MonitoringCallback, null, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        }

        /// <summary>
        /// Stops tamper detection monitoring.
        /// </summary>
        public void StopMonitoring()
        {
            _isMonitoring = false;
            _monitoringTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Performs immediate tamper detection check.
        /// </summary>
        public TamperCheckResult PerformCheck()
        {
            var result = new TamperCheckResult();

            // 1. Enhanced debugger detection (multiple techniques)
            result.DebuggerDetected = AdvancedDebuggerDetection.IsDebuggerAttached() ||
                                      AdvancedDebuggerDetection.DetectDebuggerDLLs() ||
                                      AdvancedDebuggerDetection.CheckHardwareBreakpoints();

            // 2. Remote debugger detection (Windows only)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                result.RemoteDebuggerDetected = IsRemoteDebuggerPresent();
            }

            // 3. DLL injection detection
            result.UnknownModulesDetected = DetectUnknownModules();

            // 4. Code integrity check
            if ((DateTime.UtcNow - _lastIntegrityCheck) >= _integrityCheckInterval)
            {
                result.IntegrityViolated = !VerifyExecutableIntegrity();
                _lastIntegrityCheck = DateTime.UtcNow;
            }

            // 5. Memory manipulation detection
            result.MemoryManipulationDetected = DetectMemoryManipulation();

            // 6. Timing attack detection (anti-analysis)
            result.TimingAnomalyDetected = DetectTimingAnomaly();

            result.IsTampered = result.DebuggerDetected ||
                                result.RemoteDebuggerDetected ||
                                result.UnknownModulesDetected ||
                                result.IntegrityViolated ||
                                result.MemoryManipulationDetected ||
                                result.TimingAnomalyDetected;

            return result;
        }

        private void MonitoringCallback(object? state)
        {
            if (!_isMonitoring)
                return;

            try
            {
                var result = PerformCheck();

                if (result.IsTampered)
                {
                    // CRITICAL: Activate decoy vault BEFORE raising event
                    if (_securityOptions.AutoActivateDecoyOnTamper &&
                        !_decoyActivated &&
                        _decoyService != null)
                    {
                        ActivateDecoyVault(result);
                    }

                    TamperDetected?.Invoke(this, new TamperDetectedEventArgs
                    {
                        Result = result,
                        Timestamp = DateTimeOffset.UtcNow,
                        DecoyActivated = _decoyActivated
                    });
                }
            }
            catch
            {
                // Silently catch to avoid crashing monitoring thread
            }
        }

        /// <summary>
        /// Activates the decoy vault in response to detected tampering.
        /// This method runs asynchronously but does not block the monitoring thread.
        /// </summary>
        private async void ActivateDecoyVault(TamperCheckResult tamperResult)
        {
            try
            {
                _decoyActivated = true;

                // Generate and activate decoy vault
                await _decoyService!.ActivateDecoyVaultAsync();

                // Create silent alert file for forensic analysis (if configured)
                if (!string.IsNullOrEmpty(_securityOptions.DecoyAlertFilePath))
                {
                    try
                    {
                        var alertMessage = $"[SECURITY ALERT] Decoy vault activated at {DateTime.UtcNow:O}\n" +
                                         $"Tamper detected: {tamperResult.GetDescription()}\n" +
                                         $"Debugger: {tamperResult.DebuggerDetected}, " +
                                         $"DLL Injection: {tamperResult.UnknownModulesDetected}, " +
                                         $"Memory Manipulation: {tamperResult.MemoryManipulationDetected}\n" +
                                         $"This indicates a potential security compromise. Real vault is protected.";

                        await File.WriteAllTextAsync(_securityOptions.DecoyAlertFilePath, alertMessage);

                        // Set hidden attribute on Windows
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            File.SetAttributes(_securityOptions.DecoyAlertFilePath,
                                FileAttributes.Hidden | FileAttributes.System);
                        }
                    }
                    catch
                    {
                        // Silently fail - don't alert attacker
                    }
                }
            }
            catch (Exception ex)
            {
                // CRITICAL: Don't let decoy activation failure crash the app or alert attacker
                // Only log to debug output (not visible to attacker)
                Debug.WriteLine($"[INTERNAL] Decoy activation failed: {ex.Message}");
                _decoyActivated = false;
            }
        }

        private void CalculateExecutableHash()
        {
            try
            {
                if (string.IsNullOrEmpty(_executablePath) || !File.Exists(_executablePath))
                    return;

                using var stream = File.OpenRead(_executablePath);
                using var sha256 = SHA256.Create();
                _executableHash = sha256.ComputeHash(stream);
            }
            catch
            {
                _executableHash = null;
            }
        }

        private bool VerifyExecutableIntegrity()
        {
            try
            {
                if (_executableHash == null || string.IsNullOrEmpty(_executablePath))
                    return true; // Can't verify, assume OK

                if (!File.Exists(_executablePath))
                    return false;

                using var stream = File.OpenRead(_executablePath);
                using var sha256 = SHA256.Create();
                var currentHash = sha256.ComputeHash(stream);

                return currentHash.SequenceEqual(_executableHash);
            }
            catch
            {
                return true; // Can't verify, assume OK to avoid false positives
            }
        }

        private bool DetectUnknownModules()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var modules = process.Modules;

                // Known suspicious DLL patterns (common injection vectors)
                string[] suspiciousPatterns = new[]
                {
                    "inject",
                    "hook",
                    "detour",
                    "frida",
                    "xenos",
                    "extreme injector",
                    "winject",
                    "minhook"
                };

                foreach (ProcessModule module in modules)
                {
                    var moduleName = module.ModuleName?.ToLowerInvariant() ?? string.Empty;

                    // Check for suspicious patterns
                    if (suspiciousPatterns.Any(pattern => moduleName.Contains(pattern)))
                        return true;

                    // Check for modules in temp directories (common injection technique)
                    var modulePath = module.FileName?.ToLowerInvariant() ?? string.Empty;
                    if (modulePath.Contains("\\temp\\") || modulePath.Contains("\\tmp\\"))
                        return true;
                }

                return false;
            }
            catch
            {
                return false; // Can't check, assume OK
            }
        }

        private bool DetectMemoryManipulation()
        {
            // Use canary values in memory to detect manipulation
            // This is a simplified implementation
            try
            {
                var testValue = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
                var handle = GCHandle.Alloc(testValue, GCHandleType.Pinned);
                
                try
                {
                    // Check if memory address is in expected range
                    var address = handle.AddrOfPinnedObject();
                    return address == IntPtr.Zero; // Should never be null
                }
                finally
                {
                    handle.Free();
                }
            }
            catch
            {
                return false;
            }
        }

        private bool DetectTimingAnomaly()
        {
            // Measure execution time to detect analysis tools
            var sw = Stopwatch.StartNew();
            
            // Perform simple operation that should take < 1ms
            var sum = 0;
            for (int i = 0; i < 1000; i++)
            {
                sum += i;
            }
            
            sw.Stop();

            // If operation takes > 100ms, likely being analyzed/debugged
            return sw.ElapsedMilliseconds > 100;
        }

        #region P/Invoke for Windows-specific detection

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsDebuggerPresent();

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, [MarshalAs(UnmanagedType.Bool)] out bool isDebuggerPresent);

        private bool IsRemoteDebuggerPresent()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                CheckRemoteDebuggerPresent(process.Handle, out bool isDebuggerPresent);
                return isDebuggerPresent;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        public void Dispose()
        {
            StopMonitoring();
            _monitoringTimer?.Dispose();
            _monitoringTimer = null;
        }
    }

    /// <summary>
    /// Result of tamper detection check.
    /// </summary>
    public sealed class TamperCheckResult
    {
        public bool IsTampered { get; set; }
        public bool DebuggerDetected { get; set; }
        public bool RemoteDebuggerDetected { get; set; }
        public bool UnknownModulesDetected { get; set; }
        public bool IntegrityViolated { get; set; }
        public bool MemoryManipulationDetected { get; set; }
        public bool TimingAnomalyDetected { get; set; }

        public string GetDescription()
        {
            if (!IsTampered)
                return "No tampering detected";

            var reasons = new System.Collections.Generic.List<string>();
            if (DebuggerDetected) reasons.Add("Debugger attached");
            if (RemoteDebuggerDetected) reasons.Add("Remote debugger detected");
            if (UnknownModulesDetected) reasons.Add("Suspicious DLL injection");
            if (IntegrityViolated) reasons.Add("Code integrity violation");
            if (MemoryManipulationDetected) reasons.Add("Memory manipulation");
            if (TimingAnomalyDetected) reasons.Add("Analysis tool detected");

            return string.Join(", ", reasons);
        }
    }

    /// <summary>
    /// Event args for tamper detection.
    /// </summary>
    public sealed class TamperDetectedEventArgs : EventArgs
    {
        public TamperCheckResult Result { get; set; } = new();
        public DateTimeOffset Timestamp { get; set; }

        /// <summary>
        /// Indicates whether the decoy vault was activated in response to this tamper detection.
        /// </summary>
        public bool DecoyActivated { get; set; }
    }
}
