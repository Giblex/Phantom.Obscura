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

        public event EventHandler<TamperDetectedEventArgs>? TamperDetected;

        public bool IsMonitoring => _isMonitoring;

        public bool IsDecoyActive => _decoyActivated;

        public void StartMonitoring()
        {
            if (_isMonitoring)
                return;

            _isMonitoring = true;

            CalculateExecutableHash();

            _monitoringTimer = new Timer(MonitoringCallback, null, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        }

        public void StopMonitoring()
        {
            _isMonitoring = false;
            _monitoringTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        public TamperCheckResult PerformCheck()
        {
            var result = new TamperCheckResult();

            result.DebuggerDetected = AdvancedDebuggerDetection.IsDebuggerAttached() ||
                                      AdvancedDebuggerDetection.DetectDebuggerDLLs() ||
                                      AdvancedDebuggerDetection.CheckHardwareBreakpoints();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                result.RemoteDebuggerDetected = IsRemoteDebuggerPresent();
            }

            result.UnknownModulesDetected = DetectUnknownModules();

            if ((DateTime.UtcNow - _lastIntegrityCheck) >= _integrityCheckInterval)
            {
                result.IntegrityViolated = !VerifyExecutableIntegrity();
                _lastIntegrityCheck = DateTime.UtcNow;
            }

            result.MemoryManipulationDetected = DetectMemoryManipulation();

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

            }
        }

        private async void ActivateDecoyVault(TamperCheckResult tamperResult)
        {
            // Deniability rule: activation must leave no on-disk trace. The previous
            // implementation wrote a plaintext "[SECURITY ALERT] Decoy vault activated…
            // Real vault is protected" file (Hidden/System, but trivially found) — a
            // smoking gun that proves the decoy exists. That alert file is no longer
            // written. A coercer inspecting the disk sees an ordinary, working vault.
            try
            {
                _decoyActivated = true;
                await _decoyService!.ActivateDecoyVaultAsync();
            }
            catch
            {
                // No decoy-identifying diagnostics, even at Debug level.
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
                    return true;

                if (!File.Exists(_executablePath))
                    return false;

                using var stream = File.OpenRead(_executablePath);
                using var sha256 = SHA256.Create();
                var currentHash = sha256.ComputeHash(stream);

                return currentHash.SequenceEqual(_executableHash);
            }
            catch
            {
                return true;
            }
        }

        private bool DetectUnknownModules()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var modules = process.Modules;

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

                    if (suspiciousPatterns.Any(pattern => moduleName.Contains(pattern)))
                        return true;

                    var modulePath = module.FileName?.ToLowerInvariant() ?? string.Empty;
                    if (modulePath.Contains("\\temp\\") || modulePath.Contains("\\tmp\\"))
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool DetectMemoryManipulation()
        {

            try
            {
                var testValue = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
                var handle = GCHandle.Alloc(testValue, GCHandleType.Pinned);

                try
                {

                    var address = handle.AddrOfPinnedObject();
                    return address == IntPtr.Zero;
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

            var sw = Stopwatch.StartNew();

            var sum = 0;
            for (int i = 0; i < 1000; i++)
            {
                sum += i;
            }

            sw.Stop();

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

    public sealed class TamperDetectedEventArgs : EventArgs
    {
        public TamperCheckResult Result { get; set; } = new();
        public DateTimeOffset Timestamp { get; set; }

        public bool DecoyActivated { get; set; }
    }
}

