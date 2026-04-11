using System;
using System.Runtime.InteropServices;

namespace PhantomVault.Core.Services.Security;

/// <summary>
/// Provides window-level security protections including screenshot prevention
/// and screen capture blocking. Uses Windows Display Affinity API to exclude
/// the window from screen captures, screenshots, and screen sharing.
/// </summary>
public static class WindowProtectionService
{
    // Windows API constants for SetWindowDisplayAffinity
    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_MONITOR = 0x00000001;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011; // Windows 10 2004+ (build 19041)

    // P/Invoke declarations
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowDisplayAffinity(IntPtr hWnd, out uint dwAffinity);

    /// <summary>
    /// Enables screenshot and screen capture protection for the specified window.
    /// The window will appear black in screenshots, screen recordings, and screen sharing.
    /// Requires Windows 10 version 2004 (build 19041) or later.
    /// </summary>
    /// <param name="windowHandle">Native window handle (HWND)</param>
    /// <returns>True if protection was enabled successfully</returns>
    public static bool EnableScreenshotProtection(IntPtr windowHandle)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Not supported on non-Windows platforms
            return false;
        }

        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[WindowProtectionService] Calling SetWindowDisplayAffinity with WDA_EXCLUDEFROMCAPTURE (0x{WDA_EXCLUDEFROMCAPTURE:X}) on HWND {windowHandle}");
#endif
            // Try WDA_EXCLUDEFROMCAPTURE first (Windows 10 2004+)
            // This makes the window appear black in captures while still visible to user
            if (SetWindowDisplayAffinity(windowHandle, WDA_EXCLUDEFROMCAPTURE))
            {
#if DEBUG
                GetWindowDisplayAffinity(windowHandle, out uint verifyAffinity);
                System.Diagnostics.Debug.WriteLine($"[WindowProtectionService] SUCCESS - Current affinity after enable: 0x{verifyAffinity:X}");
#endif
                return true;
            }

            // Fallback to WDA_MONITOR for older Windows 10 versions
            // This also prevents capture but may have different visual behavior
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[WindowProtectionService] WDA_EXCLUDEFROMCAPTURE failed, trying WDA_MONITOR (0x{WDA_MONITOR:X})");
#endif
            bool result = SetWindowDisplayAffinity(windowHandle, WDA_MONITOR);
#if DEBUG
            if (result)
            {
                GetWindowDisplayAffinity(windowHandle, out uint verifyAffinity);
                System.Diagnostics.Debug.WriteLine($"[WindowProtectionService] SUCCESS with WDA_MONITOR - Current affinity: 0x{verifyAffinity:X}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[WindowProtectionService] FAILED - Both WDA_EXCLUDEFROMCAPTURE and WDA_MONITOR failed");
            }
#endif
            return result;
        }
        catch (Exception
#if DEBUG
            ex
#endif
        )
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[WindowProtectionService] EXCEPTION in EnableScreenshotProtection: {ex.Message}");
#endif
            return false;
        }
    }

    /// <summary>
    /// Disables screenshot and screen capture protection for the specified window.
    /// </summary>
    /// <param name="windowHandle">Native window handle (HWND)</param>
    /// <returns>True if protection was disabled successfully</returns>
    public static bool DisableScreenshotProtection(IntPtr windowHandle)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
#if DEBUG
            GetWindowDisplayAffinity(windowHandle, out uint beforeAffinity);
            System.Diagnostics.Debug.WriteLine($"[WindowProtectionService] Current affinity BEFORE disable: 0x{beforeAffinity:X}");
            System.Diagnostics.Debug.WriteLine($"[WindowProtectionService] Calling SetWindowDisplayAffinity with WDA_NONE (0x{WDA_NONE:X}) on HWND {windowHandle}");
#endif
            bool result = SetWindowDisplayAffinity(windowHandle, WDA_NONE);
#if DEBUG
            if (result)
            {
                GetWindowDisplayAffinity(windowHandle, out uint afterAffinity);
                System.Diagnostics.Debug.WriteLine($"[WindowProtectionService] SUCCESS - Current affinity after disable: 0x{afterAffinity:X} (should be 0x0)");
            }
            else
            {
                int errorCode = Marshal.GetLastWin32Error();
                System.Diagnostics.Debug.WriteLine($"[WindowProtectionService] FAILED - SetWindowDisplayAffinity returned false, Win32 Error: {errorCode}");
            }
#endif
            return result;
        }
        catch (Exception
#if DEBUG
            ex
#endif
        )
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[WindowProtectionService] EXCEPTION in DisableScreenshotProtection: {ex.Message}");
#endif
            return false;
        }
    }

    /// <summary>
    /// Checks if screenshot protection is currently enabled for the specified window.
    /// </summary>
    /// <param name="windowHandle">Native window handle (HWND)</param>
    /// <returns>True if protection is active</returns>
    public static bool IsScreenshotProtectionEnabled(IntPtr windowHandle)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (GetWindowDisplayAffinity(windowHandle, out uint affinity))
            {
                return affinity != WDA_NONE;
            }
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if the current platform supports screenshot protection.
    /// </summary>
    /// <returns>True if running on Windows</returns>
    public static bool IsSupported()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    }
}
