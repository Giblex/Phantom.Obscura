using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PhantomVault.PrivilegedBroker
{
    /// <summary>
    /// P/Invoke helpers for verifying the identity of a connected named-pipe client.
    /// </summary>
    internal static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint clientProcessId);

        /// <summary>Resolves the full image path of the process on the other end of the pipe, or null.</summary>
        public static string? TryGetClientProcessPath(SafePipeHandle pipeHandle)
        {
            try
            {
                if (!GetNamedPipeClientProcessId(pipeHandle.DangerousGetHandle(), out uint pid))
                    return null;

                using var process = Process.GetProcessById((int)pid);
                return process.MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }
    }
}
