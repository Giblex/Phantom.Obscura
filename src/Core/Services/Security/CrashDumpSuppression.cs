using System;
using System.Runtime.InteropServices;

namespace PhantomVault.Core.Services.Security;

public static class CrashDumpSuppression
{

    private const uint SEM_FAILCRITICALERRORS = 0x0001;
    private const uint SEM_NOGPFAULTERRORBOX = 0x0002;
    private const uint SEM_NOOPENFILEERRORBOX = 0x8000;

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint uMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessDEPPolicy(uint dwFlags);

    public static bool Initialize()
    {

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        try
        {

            uint errorMode = SEM_NOGPFAULTERRORBOX | SEM_FAILCRITICALERRORS | SEM_NOOPENFILEERRORBOX;
            SetErrorMode(errorMode);

            const uint PROCESS_DEP_ENABLE = 0x00000001;
            const uint PROCESS_DEP_DISABLE_ATL_THUNK_EMULATION = 0x00000002;
            SetProcessDEPPolicy(PROCESS_DEP_ENABLE | PROCESS_DEP_DISABLE_ATL_THUNK_EMULATION);

            return true;
        }
        catch (Exception)
        {

            return false;
        }
    }

    public static bool IsActive()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    }
}

