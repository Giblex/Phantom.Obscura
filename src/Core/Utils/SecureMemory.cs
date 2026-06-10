using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace PhantomVault.Core.Utils
{

    public static class SecureMemory
    {

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

            }
        }

        public static void SecureClear(byte[] buffer)
        {
            if (buffer == null) return;
            CryptographicOperations.ZeroMemory(buffer);
        }

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

