using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;

namespace PhantomVault.Core.Utils
{

    public sealed class SecurePassword : IDisposable
    {
        private char[]? _buffer;
        private GCHandle _pinnedHandle;
        private bool _disposed;

        private SecurePassword(char[] buffer)
        {
            _buffer = buffer;

            _pinnedHandle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
        }

        public static SecurePassword FromString(string? password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return new SecurePassword(Array.Empty<char>());
            }

            var buffer = password.ToCharArray();
            return new SecurePassword(buffer);
        }

        public static SecurePassword FromSecureString(SecureString secureString)
        {
            if (secureString == null || secureString.Length == 0)
            {
                return new SecurePassword(Array.Empty<char>());
            }

            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.SecureStringToGlobalAllocUnicode(secureString);
                var buffer = new char[secureString.Length];
                Marshal.Copy(ptr, buffer, 0, secureString.Length);
                return new SecurePassword(buffer);
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                {
                    Marshal.ZeroFreeGlobalAllocUnicode(ptr);
                }
            }
        }

        public static SecurePassword Empty()
        {
            return new SecurePassword(Array.Empty<char>());
        }

        public ReadOnlySpan<char> AsSpan()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _buffer;
        }

        public int Length
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _buffer?.Length ?? 0;
            }
        }

        public bool IsEmpty => _buffer == null || _buffer.Length == 0;

        public void Dispose()
        {
            if (_disposed) return;

            try
            {

                if (_buffer != null && _buffer.Length > 0)
                {

                    var random = RandomNumberGenerator.GetBytes(_buffer.Length * sizeof(char));
                    Buffer.BlockCopy(random, 0, _buffer, 0, random.Length);

                    Array.Clear(_buffer, 0, _buffer.Length);

                    random = RandomNumberGenerator.GetBytes(_buffer.Length * sizeof(char));
                    Buffer.BlockCopy(random, 0, _buffer, 0, random.Length);

                    Array.Clear(_buffer, 0, _buffer.Length);

                    CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(_buffer.AsSpan()));
                }
            }
            finally
            {

                if (_pinnedHandle.IsAllocated)
                {
                    _pinnedHandle.Free();
                }

                _buffer = null;
                _disposed = true;
            }
        }
    }
}

