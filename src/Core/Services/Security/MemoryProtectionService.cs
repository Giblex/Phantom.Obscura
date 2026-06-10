using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace PhantomVault.Core.Services.Security
{

    public sealed class MemoryProtectionService : IDisposable
    {
        private readonly byte[] _memoryKey;
        private bool _isDisposed;

        private const int NonceSize = 12;
        private const int TagSize = 16;

        public MemoryProtectionService()
        {

            _memoryKey = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(_memoryKey);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                LockWorkingSet();
            }
        }

        public byte[] ProtectString(string sensitiveData)
        {
            if (string.IsNullOrEmpty(sensitiveData))
                return Array.Empty<byte>();

            try
            {
                var plainBytes = Encoding.UTF8.GetBytes(sensitiveData);
                return ProtectBytes(plainBytes);
            }
            finally
            {

                ClearString(sensitiveData);
            }
        }

        public string UnprotectString(byte[] protectedData)
        {
            if (protectedData == null || protectedData.Length == 0)
                return string.Empty;

            var plainBytes = UnprotectBytes(protectedData);
            try
            {
                return Encoding.UTF8.GetString(plainBytes);
            }
            finally
            {
                Array.Clear(plainBytes, 0, plainBytes.Length);
            }
        }

        public byte[] ProtectBytes(byte[] sensitiveData)
        {
            if (sensitiveData == null || sensitiveData.Length == 0)
                return Array.Empty<byte>();

            try
            {

                var nonce = new byte[NonceSize];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(nonce);
                }

                var ciphertext = new byte[sensitiveData.Length];
                var tag = new byte[TagSize];

                using var aesGcm = new AesGcm(_memoryKey, TagSize);
                aesGcm.Encrypt(nonce, sensitiveData, ciphertext, tag, null);

                var result = new byte[NonceSize + ciphertext.Length + TagSize];
                Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
                Buffer.BlockCopy(ciphertext, 0, result, NonceSize, ciphertext.Length);
                Buffer.BlockCopy(tag, 0, result, NonceSize + ciphertext.Length, TagSize);

                return result;
            }
            finally
            {

                CryptographicOperations.ZeroMemory(sensitiveData);
            }
        }

        public byte[] UnprotectBytes(byte[] protectedData)
        {
            if (protectedData == null || protectedData.Length < NonceSize + TagSize)
                return Array.Empty<byte>();

            try
            {

                var nonce = new byte[NonceSize];
                Buffer.BlockCopy(protectedData, 0, nonce, 0, NonceSize);

                int ciphertextLength = protectedData.Length - NonceSize - TagSize;
                var ciphertext = new byte[ciphertextLength];
                Buffer.BlockCopy(protectedData, NonceSize, ciphertext, 0, ciphertextLength);

                var tag = new byte[TagSize];
                Buffer.BlockCopy(protectedData, NonceSize + ciphertextLength, tag, 0, TagSize);

                var plaintext = new byte[ciphertextLength];
                using var aesGcm = new AesGcm(_memoryKey, TagSize);
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, null);

                return plaintext;
            }
            catch (CryptographicException)
            {

                return Array.Empty<byte>();
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        public SecureString CreateSecureString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return new SecureString();

            var secure = new SecureString();
            foreach (char c in value)
            {
                secure.AppendChar(c);
            }
            secure.MakeReadOnly();

            ClearString(value);

            return secure;
        }

        public string SecureStringToString(SecureString secureString)
        {
            if (secureString == null || secureString.Length == 0)
                return string.Empty;

            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.SecureStringToGlobalAllocUnicode(secureString);
                return Marshal.PtrToStringUni(ptr) ?? string.Empty;
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                {
                    Marshal.ZeroFreeGlobalAllocUnicode(ptr);
                }
            }
        }

        public void SecureClear(byte[] data)
        {
            if (data == null)
                return;

            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(data);
            }

            Array.Clear(data, 0, data.Length);
        }

        public void ClearString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            unsafe
            {
                fixed (char* ptr = value)
                {
                    for (int i = 0; i < value.Length; i++)
                    {
                        ptr[i] = '\0';
                    }
                }
            }
        }

        private void LockWorkingSet()
        {
            try
            {
                var process = System.Diagnostics.Process.GetCurrentProcess();
                SetProcessWorkingSetSize(process.Handle, -1, -1);
            }
            catch
            {

            }
        }

        public bool ProtectMemoryRegion(IntPtr address, int size)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            try
            {
                return VirtualProtect(address, (UIntPtr)size, PAGE_NOACCESS, out _);
            }
            catch
            {
                return false;
            }
        }

        public IntPtr AllocateSecureMemory(int size)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Marshal.AllocHGlobal(size);
            }

            try
            {
                var ptr = VirtualAlloc(IntPtr.Zero, (UIntPtr)size,
                    MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);

                if (ptr != IntPtr.Zero)
                {

                    VirtualLock(ptr, (UIntPtr)size);
                }

                return ptr;
            }
            catch
            {
                return Marshal.AllocHGlobal(size);
            }
        }

        public void FreeSecureMemory(IntPtr ptr, int size)
        {
            if (ptr == IntPtr.Zero)
                return;

            try
            {

                var random = new byte[size];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(random);
                }
                Marshal.Copy(random, 0, ptr, size);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    VirtualUnlock(ptr, (UIntPtr)size);
                    VirtualFree(ptr, UIntPtr.Zero, MEM_RELEASE);
                }
                else
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
            catch
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        #region P/Invoke for Windows Memory Protection

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool VirtualFree(IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool VirtualLock(IntPtr lpAddress, UIntPtr dwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool VirtualUnlock(IntPtr lpAddress, UIntPtr dwSize);

        private const uint PAGE_NOACCESS = 0x01;
        private const uint PAGE_READWRITE = 0x04;
        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint MEM_RELEASE = 0x8000;

        #endregion

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            CryptographicOperations.ZeroMemory(_memoryKey);
        }
    }
}

