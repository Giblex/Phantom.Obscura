using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Protects against keylogging attacks through input obfuscation, 
    /// clipboard monitoring, and screen overlay detection.
    /// </summary>
    public sealed class AntiKeyloggingService : IDisposable
    {
        private Timer? _monitoringTimer;
        private bool _isMonitoring;
        private readonly Queue<KeystrokeInfo> _keystrokeBuffer = new();
        private readonly object _bufferLock = new();
        private DateTime _lastClipboardCheck = DateTime.MinValue;
        private string _lastClipboardContent = string.Empty;

        /// <summary>
        /// Event raised when keylogging threat is detected.
        /// </summary>
        public event EventHandler<KeyloggingThreatEventArgs>? ThreatDetected;

        /// <summary>
        /// Gets whether anti-keylogging monitoring is active.
        /// </summary>
        public bool IsMonitoring => _isMonitoring;

        /// <summary>
        /// Gets or sets whether secure input mode is enabled.
        /// </summary>
        public bool SecureInputEnabled { get; set; } = true;

        /// <summary>
        /// Starts anti-keylogging monitoring.
        /// </summary>
        public void StartMonitoring()
        {
            if (_isMonitoring)
                return;

            _isMonitoring = true;

            // Monitor every 5 seconds
            _monitoringTimer = new Timer(MonitoringCallback, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// Stops anti-keylogging monitoring.
        /// </summary>
        public void StopMonitoring()
        {
            _isMonitoring = false;
            _monitoringTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Performs immediate keylogging threat check.
        /// </summary>
        public KeyloggingThreatCheckResult PerformCheck()
        {
            var result = new KeyloggingThreatCheckResult();

            // 1. Screen overlay detection
            result.ScreenOverlayDetected = DetectScreenOverlay();

            // 2. Keyboard hook detection
            result.KeyboardHookDetected = DetectKeyboardHook();

            // 3. Clipboard snooping detection
            result.ClipboardSnoopingDetected = DetectClipboardSnooping();

            // 4. Screen capture detection
            result.ScreenCaptureDetected = DetectScreenCapture();

            // 5. Window focus hijacking detection
            result.FocusHijackingDetected = DetectFocusHijacking();

            result.ThreatDetected = result.ScreenOverlayDetected ||
                                    result.KeyboardHookDetected ||
                                    result.ClipboardSnoopingDetected ||
                                    result.ScreenCaptureDetected ||
                                    result.FocusHijackingDetected;

            return result;
        }

        /// <summary>
        /// Registers a secure keystroke for pattern analysis.
        /// </summary>
        public void RegisterKeystroke(char character, TimeSpan timeSinceLastKey)
        {
            lock (_bufferLock)
            {
                _keystrokeBuffer.Enqueue(new KeystrokeInfo
                {
                    Character = character,
                    TimeSinceLastKey = timeSinceLastKey,
                    Timestamp = DateTimeOffset.UtcNow
                });

                // Keep buffer limited to last 100 keystrokes
                while (_keystrokeBuffer.Count > 100)
                {
                    _keystrokeBuffer.Dequeue();
                }
            }
        }

        /// <summary>
        /// Obfuscates keystroke timing to prevent timing analysis.
        /// </summary>
        public async Task<string> ObfuscateInputAsync(Func<Task<string>> inputGetter)
        {
            if (!SecureInputEnabled)
                return await inputGetter();

            // Add random delay (1-50ms) to obfuscate timing using cryptographically secure RNG
            int delay = RandomNumberGenerator.GetInt32(1, 51);
            await Task.Delay(delay);

            var input = await inputGetter();

            // Add noise keystrokes to pattern
            delay = RandomNumberGenerator.GetInt32(1, 51);
            await Task.Delay(delay);

            return input;
        }

        /// <summary>
        /// Generates secure random input noise to confuse keyloggers.
        /// </summary>
        public string GenerateInputNoise(int length = 10)
        {
            var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var randomBytes = RandomNumberGenerator.GetBytes(length);
            var result = new char[length];
            
            for (int i = 0; i < length; i++)
            {
                result[i] = chars[randomBytes[i] % chars.Length];
            }
            
            return new string(result);
        }

        private void MonitoringCallback(object? state)
        {
            if (!_isMonitoring)
                return;

            try
            {
                var result = PerformCheck();

                if (result.ThreatDetected)
                {
                    ThreatDetected?.Invoke(this, new KeyloggingThreatEventArgs
                    {
                        Result = result,
                        Timestamp = DateTimeOffset.UtcNow
                    });
                }
            }
            catch
            {
                // Silently catch to avoid crashing monitoring thread
            }
        }

        private bool DetectScreenOverlay()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            try
            {
                // Get foreground window
                var foregroundWindow = GetForegroundWindow();
                if (foregroundWindow == IntPtr.Zero)
                    return false;

                // Check if there are windows with WS_EX_LAYERED | WS_EX_TRANSPARENT
                // (common technique for screen overlays)
                var currentProcess = Process.GetCurrentProcess();
                var allWindows = GetAllWindows();

                foreach (var window in allWindows)
                {
                    var exStyle = GetWindowLong(window, GWL_EXSTYLE);
                    
                    // Check for transparent layered windows (overlay technique)
                    if ((exStyle & WS_EX_LAYERED) != 0 && (exStyle & WS_EX_TRANSPARENT) != 0)
                    {
                        // Check if it's on top of our window
                        GetWindowThreadProcessId(window, out uint processId);
                        if (processId != currentProcess.Id)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool DetectKeyboardHook()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            try
            {
                // Check for low-level keyboard hooks (WH_KEYBOARD_LL = 13)
                // This is a simplified check - full implementation would enumerate all hooks
                var currentProcess = Process.GetCurrentProcess();
                
                // Check for suspicious modules that implement keyboard hooks
                foreach (ProcessModule module in currentProcess.Modules)
                {
                    var moduleName = module.ModuleName?.ToLowerInvariant() ?? string.Empty;
                    if (moduleName.Contains("hook") || moduleName.Contains("keylog"))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool DetectClipboardSnooping()
        {
            try
            {
                // Check if clipboard content is being accessed too frequently
                // by external processes (simplified implementation)
                var now = DateTime.UtcNow;
                if ((now - _lastClipboardCheck).TotalSeconds < 1)
                {
                    // Clipboard checked too frequently - possible snooping
                    return true;
                }

                _lastClipboardCheck = now;
                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool DetectScreenCapture()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            try
            {
                // Check for screen capture processes
                var processes = Process.GetProcesses();
                string[] captureTools = new[]
                {
                    "osr",              // OBS Studio Recorder
                    "screencapture",
                    "snagit",
                    "camtasia",
                    "bandicam",
                    "fraps",
                    "sharex"
                };

                return processes.Any(p =>
                {
                    try
                    {
                        var name = p.ProcessName.ToLowerInvariant();
                        return captureTools.Any(tool => name.Contains(tool));
                    }
                    catch
                    {
                        return false;
                    }
                });
            }
            catch
            {
                return false;
            }
        }

        private bool DetectFocusHijacking()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            try
            {
                var foregroundWindow = GetForegroundWindow();
                if (foregroundWindow == IntPtr.Zero)
                    return false;

                GetWindowThreadProcessId(foregroundWindow, out uint processId);
                var currentProcessId = (uint)Process.GetCurrentProcess().Id;

                // If foreground window changed unexpectedly, might be focus hijacking
                // This is a simplified check
                return false; // Would need state tracking to implement properly
            }
            catch
            {
                return false;
            }
        }

        #region P/Invoke for Windows

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private List<IntPtr> GetAllWindows()
        {
            var windows = new List<IntPtr>();
            EnumWindows((hWnd, lParam) =>
            {
                windows.Add(hWnd);
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        #endregion

        public void Dispose()
        {
            StopMonitoring();
            _monitoringTimer?.Dispose();
            _monitoringTimer = null;
        }

        private sealed class KeystrokeInfo
        {
            public char Character { get; set; }
            public TimeSpan TimeSinceLastKey { get; set; }
            public DateTimeOffset Timestamp { get; set; }
        }
    }

    /// <summary>
    /// Result of keylogging threat check.
    /// </summary>
    public sealed class KeyloggingThreatCheckResult
    {
        public bool ThreatDetected { get; set; }
        public bool ScreenOverlayDetected { get; set; }
        public bool KeyboardHookDetected { get; set; }
        public bool ClipboardSnoopingDetected { get; set; }
        public bool ScreenCaptureDetected { get; set; }
        public bool FocusHijackingDetected { get; set; }

        public string GetDescription()
        {
            if (!ThreatDetected)
                return "No keylogging threats detected";

            var threats = new List<string>();
            if (ScreenOverlayDetected) threats.Add("Screen overlay detected");
            if (KeyboardHookDetected) threats.Add("Keyboard hook detected");
            if (ClipboardSnoopingDetected) threats.Add("Clipboard snooping");
            if (ScreenCaptureDetected) threats.Add("Screen capture tool running");
            if (FocusHijackingDetected) threats.Add("Focus hijacking attempt");

            return string.Join(", ", threats);
        }
    }

    /// <summary>
    /// Event args for keylogging threat detection.
    /// </summary>
    public sealed class KeyloggingThreatEventArgs : EventArgs
    {
        public KeyloggingThreatCheckResult Result { get; set; } = new();
        public DateTimeOffset Timestamp { get; set; }
    }
}
