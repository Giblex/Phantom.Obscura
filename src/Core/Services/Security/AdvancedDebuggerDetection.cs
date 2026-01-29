using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PhantomVault.Core.Services.Security;

/// <summary>
/// Advanced debugger detection techniques beyond basic IsDebuggerPresent checks.
/// SECURITY: Implements multiple anti-debugging methods for defense in depth.
/// </summary>
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

    /// <summary>
    /// Performs comprehensive debugger detection using multiple techniques.
    /// </summary>
    /// <returns>True if any debugger detection method succeeds</returns>
    public static bool IsDebuggerAttached()
    {
        // Only run on Windows
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Debugger.IsAttached;
        }

        // Method 1: Standard .NET debugger check
        if (Debugger.IsAttached)
            return true;

        // Method 2: Win32 IsDebuggerPresent
        if (IsDebuggerPresent())
            return true;

        // Method 3: Check for remote debugger
        if (IsRemoteDebuggerPresent())
            return true;

        // Method 4: Check PEB (Process Environment Block) BeingDebugged flag
        if (CheckPEBBeingDebugged())
            return true;

        // Method 5: Check for debugging handles
        if (DetectDebugPort())
            return true;

        // Method 6: Timing-based detection
        if (TimingCheckDetection())
            return true;

        return false;
    }

    /// <summary>
    /// Checks if a remote debugger is attached (kernel debugger or remote debugging).
    /// </summary>
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

    /// <summary>
    /// Checks the PEB (Process Environment Block) BeingDebugged flag directly.
    /// This is more reliable than IsDebuggerPresent as it checks the raw structure.
    /// </summary>
    private static bool CheckPEBBeingDebugged()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            var pbi = new PROCESS_BASIC_INFORMATION();
            int returnLength;

            int status = NtQueryInformationProcess(
                process.Handle,
                0, // ProcessBasicInformation
                ref pbi,
                Marshal.SizeOf(pbi),
                out returnLength);

            if (status != 0 || pbi.PebBaseAddress == IntPtr.Zero)
                return false;

            // Read the BeingDebugged flag from PEB
            // PEB+0x02 = BeingDebugged (byte)
            byte beingDebugged = Marshal.ReadByte(pbi.PebBaseAddress, 0x02);
            return beingDebugged != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks for debug port using NtQueryInformationProcess.
    /// A debugger attaches to a debug port which can be detected.
    /// </summary>
    private static bool DetectDebugPort()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            IntPtr debugPort = IntPtr.Zero;
            int returnLength;

            // ProcessDebugPort = 7
            int status;
            unsafe
            {
                status = NtQueryInformationProcess(
                    process.Handle,
                    7, // ProcessDebugPort
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

    /// <summary>
    /// Uses timing to detect if execution is being slowed by a debugger.
    /// Debuggers introduce significant timing delays during instruction stepping.
    /// </summary>
    private static bool TimingCheckDetection()
    {
        try
        {
            var sw = Stopwatch.StartNew();

            // Perform a simple operation
            int sum = 0;
            for (int i = 0; i < 100; i++)
            {
                sum += i;
            }

            sw.Stop();

            // If this took more than 10ms, likely under a debugger
            // Normal execution should be < 1ms
            return sw.ElapsedMilliseconds > 10;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks for common debugger DLLs loaded in the process.
    /// </summary>
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
                // Continue checking
            }
        }

        return false;
    }

    /// <summary>
    /// Checks for debugger by attempting to read debug registers.
    /// Hardware breakpoints are stored in debug registers DR0-DR7.
    /// </summary>
    public static bool CheckHardwareBreakpoints()
    {
        try
        {
            // This is a simplified check
            // In practice, reading debug registers requires more complex methods
            // For now, we detect if GetThreadContext is hooked (common debugger technique)
            var kernel32 = GetModuleHandle("kernel32.dll");
            if (kernel32 == IntPtr.Zero)
                return false;

            var getThreadContext = GetProcAddress(kernel32, "GetThreadContext");
            if (getThreadContext == IntPtr.Zero)
                return false;

            // Check if the first byte is a jump instruction (0xE9)
            // which would indicate hooking
            byte firstByte = Marshal.ReadByte(getThreadContext);
            return firstByte == 0xE9 || firstByte == 0xEB; // JMP or JMP short
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Performs a quick debugger check suitable for frequent polling.
    /// Only uses fast methods to minimize performance impact.
    /// </summary>
    public static bool QuickDebuggerCheck()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Debugger.IsAttached;
        }

        // Only use the fastest checks
        return Debugger.IsAttached || IsDebuggerPresent();
    }
}
