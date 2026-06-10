using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Serilog;

namespace PhantomVault.UI.Services
{
    // Registers a system-wide hotkey via a dedicated message-only window running its own
    // message pump on a background thread, so it never subclasses or interferes with the
    // Avalonia window proc. Raises HotkeyPressed (off the UI thread — marshal as needed).
    [SupportedOSPlatform("windows")]
    public sealed class GlobalHotkeyService : IDisposable
    {
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;
        private const int WM_HOTKEY = 0x0312;
        private const int WM_CLOSE = 0x0010;
        private const int HOTKEY_ID = 0xB10C;
        private static readonly IntPtr HWND_MESSAGE = new(-3);

        public event Action? HotkeyPressed;

        private readonly uint _modifiers;
        private readonly uint _vk;
        private readonly WndProc _wndProcDelegate;
        private Thread? _pumpThread;
        private IntPtr _hwnd;
        private volatile bool _disposed;

        public GlobalHotkeyService(uint modifiers, uint virtualKey)
        {
            _modifiers = modifiers | MOD_NOREPEAT;
            _vk = virtualKey;
            _wndProcDelegate = WindowProc;
        }

        public void Start()
        {
            if (_pumpThread != null) return;
            _pumpThread = new Thread(PumpLoop)
            {
                IsBackground = true,
                Name = "PhantomGlobalHotkey"
            };
            _pumpThread.SetApartmentState(ApartmentState.STA);
            _pumpThread.Start();
        }

        private void PumpLoop()
        {
            var className = "PhantomHotkeyWnd_" + Guid.NewGuid().ToString("N");
            var wndClass = new WNDCLASS
            {
                lpfnWndProc = _wndProcDelegate,
                lpszClassName = className,
                hInstance = GetModuleHandle(null)
            };

            if (RegisterClassW(ref wndClass) == 0)
            {
                Log.Warning("GlobalHotkeyService: RegisterClassW failed ({Err})", Marshal.GetLastWin32Error());
                return;
            }

            _hwnd = CreateWindowExW(0, className, "PhantomHotkey", 0, 0, 0, 0, 0,
                HWND_MESSAGE, IntPtr.Zero, wndClass.hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
            {
                Log.Warning("GlobalHotkeyService: CreateWindowExW failed ({Err})", Marshal.GetLastWin32Error());
                return;
            }

            if (!RegisterHotKey(_hwnd, HOTKEY_ID, _modifiers, _vk))
            {
                Log.Warning("GlobalHotkeyService: RegisterHotKey failed ({Err}) — chord may be in use", Marshal.GetLastWin32Error());
            }

            while (!_disposed && GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            UnregisterHotKey(_hwnd, HOTKEY_ID);
            DestroyWindow(_hwnd);
            UnregisterClassW(className, wndClass.hInstance);
            _hwnd = IntPtr.Zero;
        }

        private IntPtr WindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                try { HotkeyPressed?.Invoke(); }
                catch (Exception ex) { Log.Warning(ex, "GlobalHotkeyService: handler threw"); }
                return IntPtr.Zero;
            }
            return DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_hwnd != IntPtr.Zero)
                PostMessageW(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        private delegate IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASS
        {
            public uint style;
            public WndProc lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int pt_x;
            public int pt_y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool UnregisterClassW(string lpClassName, IntPtr hInstance);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName,
            uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr hInstance, IntPtr param);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProcW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool PostMessageW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
