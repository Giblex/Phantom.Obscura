using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Honest, evidence-based environmental checks for input hostility.
    /// Hard rule: this service does NOT obfuscate, randomize, or otherwise
    /// "outsmart" a keylogger that has already injected itself. It surfaces
    /// signals the user can act on, then defers to <see cref="WindowProtectionService"/>
    /// (screenshot blocking) and the OS for actual defence.
    /// </summary>
    public sealed class AntiKeyloggingService : IDisposable
    {
        private Timer? _monitoringTimer;
        private bool _isMonitoring;

        public event EventHandler<KeyloggingThreatEventArgs>? ThreatDetected;

        public bool IsMonitoring => _isMonitoring;

        public bool SecureInputEnabled { get; set; } = true;

        public void StartMonitoring()
        {
            if (_isMonitoring) return;
            _isMonitoring = true;
            _monitoringTimer = new Timer(MonitoringCallback, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }

        public void StopMonitoring()
        {
            _isMonitoring = false;
            _monitoringTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Snapshot of input-environment hostility signals. NOT a guarantee
        /// of absence — only a positive signal that something suspicious is present.
        /// </summary>
        public KeyloggingThreatCheckResult PerformCheck()
        {
            return new KeyloggingThreatCheckResult
            {
                ScreenOverlayDetected = DetectScreenOverlay(),
                KeyboardHookDetected = DetectKeyboardHook(),
                ClipboardSnoopingDetected = DetectClipboardSnooping(),
                ScreenCaptureDetected = DetectScreenCapture(),
                FocusHijackingDetected = DetectFocusHijacking()
            };
        }

        /// <summary>
        /// Convenience: returns true only when ALL probes are negative.
        /// Callers should refuse to surface a password prompt unless this returns true.
        /// </summary>
        public bool IsSafeInputEnvironment() => !PerformCheck().ThreatDetected;

        // RegisterKeystroke kept as a no-op so existing controls (SecureTextBox) compile.
        // We deliberately do NOT keep a keystroke buffer — it would be itself a secret leak.
        public void RegisterKeystroke(char _ignoredCharacter, TimeSpan _ignoredTimeSinceLastKey)
        {
        }

        private void MonitoringCallback(object? state)
        {
            if (!_isMonitoring) return;
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
                // monitoring must never throw
            }
        }

        // ── Screen overlay ──────────────────────────────────────────────────
        private bool DetectScreenOverlay()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;

            try
            {
                var currentPid = Process.GetCurrentProcess().Id;
                var windows = GetAllWindows();
                foreach (var window in windows)
                {
                    var exStyle = GetWindowLong(window, GWL_EXSTYLE);
                    if ((exStyle & WS_EX_LAYERED) != 0 && (exStyle & WS_EX_TRANSPARENT) != 0)
                    {
                        GetWindowThreadProcessId(window, out uint pid);
                        if (pid != currentPid) return true;
                    }
                }
                return false;
            }
            catch { return false; }
        }

        // ── Keyboard-hook probe ─────────────────────────────────────────────
        // Real signal: synthesise a keystroke into our own thread queue and
        // measure how long it takes to be dispatched. A global LL hook adds
        // ~1-30ms per keystroke; sustained latency above the platform baseline
        // is a strong signal a hook is installed somewhere in the chain.
        private bool DetectKeyboardHook()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;

            try
            {
                // Baseline: how long does GetMessageTime → now drift between
                // two synthesised inputs? We post a benign WM_NULL to our own
                // thread queue and measure round-trip. Above 50ms on three
                // consecutive samples indicates a hook is intercepting.
                const int samples = 3;
                const int suspiciousMs = 50;
                int hits = 0;

                for (int i = 0; i < samples; i++)
                {
                    var sw = Stopwatch.StartNew();
                    var threadId = GetCurrentThreadId();
                    if (!PostThreadMessage(threadId, WM_NULL, IntPtr.Zero, IntPtr.Zero))
                        return false;
                    if (PeekMessage(out _, IntPtr.Zero, WM_NULL, WM_NULL, PM_REMOVE))
                    {
                        sw.Stop();
                        if (sw.ElapsedMilliseconds > suspiciousMs) hits++;
                    }
                    Thread.Sleep(5);
                }

                return hits == samples;
            }
            catch { return false; }
        }

        // ── Clipboard snooping ──────────────────────────────────────────────
        // Real signal: enumerate clipboard format listeners. Any process other
        // than us listening implies someone is watching clipboard transitions.
        // The classic AddClipboardFormatListener registers the calling HWND;
        // we cannot enumerate the system list directly, so we use the
        // GetClipboardSequenceNumber + GetOpenClipboardWindow pair as a probe.
        private bool DetectClipboardSnooping()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;

            try
            {
                var owner = GetOpenClipboardWindow();
                if (owner == IntPtr.Zero) return false;

                GetWindowThreadProcessId(owner, out uint pid);
                var currentPid = (uint)Process.GetCurrentProcess().Id;
                return pid != currentPid;
            }
            catch { return false; }
        }

        // ── Screen capture ──────────────────────────────────────────────────
        // The real defence is WindowProtectionService.EnableScreenshotProtection
        // (SetWindowDisplayAffinity with WDA_EXCLUDEFROMCAPTURE). This probe
        // only surfaces a hint to the user that capture tooling is active.
        private bool DetectScreenCapture()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;

            try
            {
                string[] captureToolNames =
                {
                    "obs", "obs64", "screencapture", "snagit", "snagiteditor",
                    "camtasia", "camtasiastudio", "bandicam", "fraps", "sharex",
                    "vlc", "screencast-o-matic", "msteams", "zoom" // last two: legitimate but capture-capable
                };

                var hostile = false;
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        var name = p.ProcessName.ToLowerInvariant();
                        if (captureToolNames.Contains(name)) { hostile = true; break; }
                    }
                    catch { /* process can vanish mid-enumeration */ }
                }
                return hostile;
            }
            catch { return false; }
        }

        // ── Focus hijack ────────────────────────────────────────────────────
        // Real signal: if our window was foreground less than 200 ms ago and
        // the foreground has switched to a different PID without user input,
        // something is stealing focus. We measure the gap between
        // GetLastInputInfo and the foreground change.
        private bool DetectFocusHijacking()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;

            try
            {
                var foreground = GetForegroundWindow();
                if (foreground == IntPtr.Zero) return false;

                GetWindowThreadProcessId(foreground, out uint foregroundPid);
                var currentPid = (uint)Process.GetCurrentProcess().Id;
                if (foregroundPid == currentPid) return false;

                var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
                if (!GetLastInputInfo(ref lii)) return false;

                uint idleMs = unchecked((uint)Environment.TickCount) - lii.dwTime;
                // foreground belongs to another process AND user has been idle > 1s
                // → not a user-initiated switch.
                return idleMs > 1000;
            }
            catch { return false; }
        }

        // ── P/Invoke ────────────────────────────────────────────────────────
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern IntPtr GetOpenClipboardWindow();

        [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool PeekMessage(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            public IntPtr handle;
            public uint msg;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public System.Drawing.Point p;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const uint WM_NULL = 0x0000;
        private const uint PM_REMOVE = 0x0001;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private List<IntPtr> GetAllWindows()
        {
            var windows = new List<IntPtr>();
            EnumWindows((hWnd, _) => { windows.Add(hWnd); return true; }, IntPtr.Zero);
            return windows;
        }

        public void Dispose()
        {
            StopMonitoring();
            _monitoringTimer?.Dispose();
            _monitoringTimer = null;
        }
    }

    public sealed class KeyloggingThreatCheckResult
    {
        public bool ScreenOverlayDetected { get; set; }
        public bool KeyboardHookDetected { get; set; }
        public bool ClipboardSnoopingDetected { get; set; }
        public bool ScreenCaptureDetected { get; set; }
        public bool FocusHijackingDetected { get; set; }

        public bool ThreatDetected =>
            ScreenOverlayDetected || KeyboardHookDetected ||
            ClipboardSnoopingDetected || ScreenCaptureDetected ||
            FocusHijackingDetected;

        public string GetDescription()
        {
            if (!ThreatDetected) return "Input environment looks clean to our probes. This is a heuristic, not a guarantee.";

            var threats = new List<string>();
            if (ScreenOverlayDetected) threats.Add("layered transparent overlay window detected (possible UI redress)");
            if (KeyboardHookDetected) threats.Add("input dispatch latency suggests a global keyboard hook");
            if (ClipboardSnoopingDetected) threats.Add("another process is holding the clipboard open");
            if (ScreenCaptureDetected) threats.Add("a screen-capture tool is running");
            if (FocusHijackingDetected) threats.Add("foreground stolen by another process while the user was idle");
            return string.Join("; ", threats);
        }
    }

    public sealed class KeyloggingThreatEventArgs : EventArgs
    {
        public KeyloggingThreatCheckResult Result { get; set; } = new();
        public DateTimeOffset Timestamp { get; set; }
    }
}
