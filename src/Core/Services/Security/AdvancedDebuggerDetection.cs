using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PhantomVault.Core.Services.Security;

public static class AdvancedDebuggerDetection
{
    #region P/Invoke Declarations

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsDebuggerPresent();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, [MarshalAs(UnmanagedType.Bool)] out bool isDebuggerPresent);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref PROCESS_BASIC_INFORMATION processInformation,
        int processInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    #endregion

    public static bool IsDebuggerAttached()
    {

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Debugger.IsAttached;
        }

        if (Debugger.IsAttached)
            return true;

        if (IsDebuggerPresent())
            return true;

        if (IsRemoteDebuggerPresent())
            return true;

        if (CheckPEBBeingDebugged())
            return true;

        if (DetectDebugPort())
            return true;

        if (TimingCheckDetection())
            return true;

        return false;
    }

    private static bool IsRemoteDebuggerPresent()
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

    private static bool CheckPEBBeingDebugged()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            var pbi = new PROCESS_BASIC_INFORMATION();
            int returnLength;

            int status = NtQueryInformationProcess(
                process.Handle,
                0,
                ref pbi,
                Marshal.SizeOf(pbi),
                out returnLength);

            if (status != 0 || pbi.PebBaseAddress == IntPtr.Zero)
                return false;

            byte beingDebugged = Marshal.ReadByte(pbi.PebBaseAddress, 0x02);
            return beingDebugged != 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectDebugPort()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            IntPtr debugPort = IntPtr.Zero;
            int returnLength;

            int status;
            unsafe
            {
                status = NtQueryInformationProcess(
                    process.Handle,
                    7,
                    ref *(PROCESS_BASIC_INFORMATION*)&debugPort,
                    IntPtr.Size,
                    out returnLength);
            }

            return debugPort != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    private static bool TimingCheckDetection()
    {
        try
        {
            var sw = Stopwatch.StartNew();

            int sum = 0;
            for (int i = 0; i < 100; i++)
            {
                sum += i;
            }

            sw.Stop();

            return sw.ElapsedMilliseconds > 10;
        }
        catch
        {
            return false;
        }
    }

    public static bool DetectDebuggerDLLs()
    {
        string[] debuggerDlls = new[]
        {
            "x64dbg.dll",
            "x32dbg.dll",
            "windbg.dll",
            "ida.dll",
            "ida64.dll",
            "ollydbg.dll",
            "scylla.dll"
        };

        foreach (var dll in debuggerDlls)
        {
            try
            {
                IntPtr handle = GetModuleHandle(dll);
                if (handle != IntPtr.Zero)
                    return true;
            }
            catch
            {

            }
        }

        return false;
    }

    public static bool CheckHardwareBreakpoints()
    {
        try
        {

            var kernel32 = GetModuleHandle("kernel32.dll");
            if (kernel32 == IntPtr.Zero)
                return false;

            var getThreadContext = GetProcAddress(kernel32, "GetThreadContext");
            if (getThreadContext == IntPtr.Zero)
                return false;

            byte firstByte = Marshal.ReadByte(getThreadContext);
            return firstByte == 0xE9 || firstByte == 0xEB;
        }
        catch
        {
            return false;
        }
    }

    public static bool QuickDebuggerCheck()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Debugger.IsAttached;
        }

        return Debugger.IsAttached || IsDebuggerPresent();
    }
}

