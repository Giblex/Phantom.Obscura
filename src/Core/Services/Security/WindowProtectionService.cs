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
            // Try WDA_EXCLUDEFROMCAPTURE first (Windows 10 2004+)
            // This makes the window appear black in captures while still visible to user
            if (SetWindowDisplayAffinity(windowHandle, WDA_EXCLUDEFROMCAPTURE))
            {
                return true;
            }

            // Fallback to WDA_MONITOR for older Windows 10 versions
            // This also prevents capture but may have different visual behavior
            return SetWindowDisplayAffinity(windowHandle, WDA_MONITOR);
        }
        catch (Exception)
        {
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
            return SetWindowDisplayAffinity(windowHandle, WDA_NONE);
        }
        catch (Exception)
        {
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
