using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;

namespace PhantomVault.Core.Utils
{
    /// <summary>
    /// Represents a password that can be securely zeroed from memory.
    /// This class provides a safer alternative to passing passwords as strings,
    /// which cannot be reliably cleared from managed memory in .NET.
    ///
    /// Usage pattern:
    /// using var password = SecurePassword.FromString(userInput);
    /// SomeMethod(password.AsSpan());
    /// // Password is automatically zeroed when disposed
    /// </summary>
    public sealed class SecurePassword : IDisposable
    {
        private char[]? _buffer;
        private GCHandle _pinnedHandle;
        private bool _disposed;

        private SecurePassword(char[] buffer)
        {
            _buffer = buffer;
            // Pin the array to prevent GC from moving it in memory
            _pinnedHandle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
        }

        /// <summary>
        /// Creates a SecurePassword from a string. The string parameter will still
        /// remain in memory (this is a .NET limitation), but the SecurePassword
        /// instance can be reliably zeroed.
        ///
        /// For best security, use this immediately when receiving user input and
        /// do not store the original string.
        /// </summary>
        public static SecurePassword FromString(string? password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return new SecurePassword(Array.Empty<char>());
            }

            var buffer = password.ToCharArray();
            return new SecurePassword(buffer);
        }

        /// <summary>
        /// Creates a SecurePassword from a SecureString (for compatibility with
        /// legacy code that uses SecureString).
        /// </summary>
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

        /// <summary>
        /// Creates an empty SecurePassword (useful for keyfile-only authentication).
        /// </summary>
        public static SecurePassword Empty()
        {
            return new SecurePassword(Array.Empty<char>());
        }

        /// <summary>
        /// Returns a read-only span over the password characters.
        /// This span is only valid while the SecurePassword is not disposed.
        /// </summary>
        public ReadOnlySpan<char> AsSpan()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _buffer;
        }

        /// <summary>
        /// Returns the length of the password.
        /// </summary>
        public int Length
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _buffer?.Length ?? 0;
            }
        }

        /// <summary>
        /// Checks if the password is empty.
        /// </summary>
        public bool IsEmpty => _buffer == null || _buffer.Length == 0;

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                // Zero the buffer using three passes for extra security
                if (_buffer != null && _buffer.Length > 0)
                {
                    // Pass 1: Fill with random data
                    var random = RandomNumberGenerator.GetBytes(_buffer.Length * sizeof(char));
                    Buffer.BlockCopy(random, 0, _buffer, 0, random.Length);

                    // Pass 2: Fill with zeros
                    Array.Clear(_buffer, 0, _buffer.Length);

                    // Pass 3: Fill with different random data
                    random = RandomNumberGenerator.GetBytes(_buffer.Length * sizeof(char));
                    Buffer.BlockCopy(random, 0, _buffer, 0, random.Length);

                    // Final zero
                    Array.Clear(_buffer, 0, _buffer.Length);

                    // Use CryptographicOperations for final secure zero
                    CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(_buffer.AsSpan()));
                }
            }
            finally
            {
                // Unpin the array
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
