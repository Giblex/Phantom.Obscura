using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace PhantomVault.Core.Utils
{
    /// <summary>
    /// Secure wrapper for credential strings that ensures sensitive data
    /// is encrypted in memory and properly zeroized when disposed.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class SecureCredentialString : IDisposable
    {
        private byte[]? _encryptedData;
        private byte[]? _entropy;
        private int _length;
        private bool _disposed;

        public SecureCredentialString(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext))
            {
                _encryptedData = Array.Empty<byte>();
                _length = 0;
                return;
            }

            _length = plaintext.Length;
            _entropy = new byte[32];
            RandomNumberGenerator.Fill(_entropy);

            byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            try
            {
                _encryptedData = ProtectedData.Protect(
                    plaintextBytes,
                    _entropy,
                    DataProtectionScope.CurrentUser);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintextBytes);
            }
        }

        public SecureCredentialString(SecureString secureString)
        {
            if (secureString == null || secureString.Length == 0)
            {
                _encryptedData = Array.Empty<byte>();
                _length = 0;
                return;
            }

            _length = secureString.Length;
            _entropy = new byte[32];
            RandomNumberGenerator.Fill(_entropy);

            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.SecureStringToGlobalAllocUnicode(secureString);
                byte[] plaintextBytes = new byte[secureString.Length * 2];
                Marshal.Copy(ptr, plaintextBytes, 0, plaintextBytes.Length);

                _encryptedData = ProtectedData.Protect(
                    plaintextBytes,
                    _entropy,
                    DataProtectionScope.CurrentUser);

                CryptographicOperations.ZeroMemory(plaintextBytes);
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                    Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }
        }

        public int Length => _length;
        public bool IsEmpty => _length == 0;

        /// <summary>
        /// Retrieves plaintext value. CALLER MUST ZERO THE RETURNED ARRAY.
        /// </summary>
        public string GetPlaintext()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_encryptedData == null || _encryptedData.Length == 0)
                return string.Empty;

            byte[] decryptedBytes = ProtectedData.Unprotect(
                _encryptedData,
                _entropy,
                DataProtectionScope.CurrentUser);

            try
            {
                return Encoding.UTF8.GetString(decryptedBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(decryptedBytes);
            }
        }

        /// <summary>
        /// Executes action with plaintext and immediately zeros it.
        /// </summary>
        public void UseSecurely(Action<string> action)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            string plaintext = GetPlaintext();
            try
            {
                action(plaintext);
            }
            finally
            {
                // Force GC to zero the string memory
                unsafe
                {
                    fixed (char* ptr = plaintext)
                    {
                        for (int i = 0; i < plaintext.Length; i++)
                            ptr[i] = '\0';
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            if (_encryptedData != null)
            {
                CryptographicOperations.ZeroMemory(_encryptedData);
                _encryptedData = null;
            }

            if (_entropy != null)
            {
                CryptographicOperations.ZeroMemory(_entropy);
                _entropy = null;
            }

            _length = 0;
            _disposed = true;
        }
    }
}
