using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace PhantomVault.Core.Utils
{
    /// <summary>
    /// Provides utilities for handling sensitive data in memory. On
    /// supported platforms this includes locking pages to prevent
    /// swapping and clearing buffers after use. These APIs are
    /// best‑effort; if the underlying platform does not support
    /// mlock/VirtualLock the calls will be no‑ops.
    /// </summary>
    public static class SecureMemory
    {
        /// <summary>
        /// Locks the specified buffer into physical memory, preventing
        /// it from being paged to disk. Returns true on success or
        /// false if the platform does not support locking.
        /// </summary>
        public static bool Lock(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty) return false;
            try
            {
                unsafe
                {
                    fixed (byte* ptr = data)
                    {
                        return LockMemory((IntPtr)ptr, data.Length);
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Unlocks the specified buffer, allowing it to be paged out.
        /// </summary>
        public static void Unlock(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty) return;
            try
            {
                unsafe
                {
                    fixed (byte* ptr = data)
                    {
                        UnlockMemory((IntPtr)ptr, data.Length);
                    }
                }
            }
            catch
            {
                // Ignore errors
            }
        }

        /// <summary>
        /// Securely zeros a byte array. Uses the built‑in
        /// CryptographicOperations.ZeroMemory which is immune to
        /// compiler optimisations that might skip clearing.
        /// </summary>
        public static void SecureClear(byte[] buffer)
        {
            if (buffer == null) return;
            CryptographicOperations.ZeroMemory(buffer);
        }

        // Platform interop declarations (declared for both; invoked conditionally at runtime)
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualLock(IntPtr lpAddress, UIntPtr dwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualUnlock(IntPtr lpAddress, UIntPtr dwSize);

        [DllImport("libc", SetLastError = true)]
        private static extern int mlock(IntPtr addr, UIntPtr len);

        [DllImport("libc", SetLastError = true)]
        private static extern int munlock(IntPtr addr, UIntPtr len);

        private static bool LockMemory(IntPtr address, int length)
        {
            if (OperatingSystem.IsWindows())
            {
                return VirtualLock(address, (UIntPtr)length);
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                return mlock(address, (UIntPtr)length) == 0;
            }
            return false;
        }

        private static void UnlockMemory(IntPtr address, int length)
        {
            if (OperatingSystem.IsWindows())
            {
                _ = VirtualUnlock(address, (UIntPtr)length);
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                _ = munlock(address, (UIntPtr)length);
            }
        }
    }
}
