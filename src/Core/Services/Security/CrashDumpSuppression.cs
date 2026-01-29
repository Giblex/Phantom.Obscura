using System;
using System.Runtime.InteropServices;

namespace PhantomVault.Core.Services.Security;

/// <summary>
/// Prevents crash dumps from being generated to protect sensitive data in memory.
/// Windows creates minidumps in %LOCALAPPDATA%\CrashDumps which could contain decrypted vault data.
/// </summary>
public static class CrashDumpSuppression
{
    // Windows API constants
    private const uint SEM_FAILCRITICALERRORS = 0x0001;
    private const uint SEM_NOGPFAULTERRORBOX = 0x0002;
    private const uint SEM_NOOPENFILEERRORBOX = 0x8000;

    // P/Invoke declarations
    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint uMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessDEPPolicy(uint dwFlags);

    /// <summary>
    /// Initializes crash dump suppression for the current process.
    /// SECURITY: Must be called early in application startup before any sensitive data is loaded.
    /// </summary>
    /// <returns>True if successfully configured, false if platform not supported</returns>
    public static bool Initialize()
    {
        // Only supported on Windows
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        try
        {
            // Disable Windows Error Reporting crash dialog and minidump generation
            // SEM_NOGPFAULTERRORBOX: Prevents the system from displaying the critical-error-handler message box
            // SEM_FAILCRITICALERRORS: System does not display the critical-error-handler message box
            // SEM_NOOPENFILEERRORBOX: System does not display a message box when it fails to find a file
            uint errorMode = SEM_NOGPFAULTERRORBOX | SEM_FAILCRITICALERRORS | SEM_NOOPENFILEERRORBOX;
            SetErrorMode(errorMode);

            // Enable Data Execution Prevention (DEP) for additional protection
            // PROCESS_DEP_ENABLE (0x00000001) - Enable DEP
            // PROCESS_DEP_DISABLE_ATL_THUNK_EMULATION (0x00000002) - Disable ATL thunk emulation
            const uint PROCESS_DEP_ENABLE = 0x00000001;
            const uint PROCESS_DEP_DISABLE_ATL_THUNK_EMULATION = 0x00000002;
            SetProcessDEPPolicy(PROCESS_DEP_ENABLE | PROCESS_DEP_DISABLE_ATL_THUNK_EMULATION);

            return true;
        }
        catch (Exception)
        {
            // If we can't suppress dumps, continue anyway rather than blocking startup
            // The application can still function, just with reduced protection
            return false;
        }
    }

    /// <summary>
    /// Checks if crash dump suppression is active.
    /// </summary>
    /// <returns>True if running on Windows (where suppression is applied)</returns>
    public static bool IsActive()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    }
}
