using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace PhantomVault.Core.Utils
{
    /// <summary>
    /// Securely combines a passphrase with keyfile contents for multi-factor authentication.
    /// Unlike string concatenation, this class ensures all intermediate buffers are properly
    /// zeroed after use.
    /// </summary>
    public sealed class SecurePasswordCombiner : IDisposable
    {
        private char[]? _combinedBuffer;
        private GCHandle _pinnedHandle;
        private bool _disposed;

        private SecurePasswordCombiner(char[] buffer)
        {
            _combinedBuffer = buffer;
            _pinnedHandle = GCHandle.Alloc(_combinedBuffer, GCHandleType.Pinned);
        }

        /// <summary>
        /// Combines a passphrase with optional keyfile contents.
        /// The resulting buffer can be securely zeroed when disposed.
        /// </summary>
        /// <param name="passphrase">The user passphrase (can be empty for keyfile-only).</param>
        /// <param name="keyfilePath">Optional path to keyfile.</param>
        /// <param name="keyfileRequired">If true, throws if keyfile path is not provided.</param>
        public static SecurePasswordCombiner Combine(SecurePassword passphrase, string? keyfilePath, bool keyfileRequired = false)
        {
            if (passphrase == null)
            {
                throw new ArgumentNullException(nameof(passphrase));
            }

            // If no keyfile path provided
            if (string.IsNullOrWhiteSpace(keyfilePath))
            {
                if (keyfileRequired)
                {
                    throw new SecurityException("Keyfile required but no keyfile path was provided.");
                }

                // Return copy of passphrase only
                var buffer = new char[passphrase.Length];
                passphrase.AsSpan().CopyTo(buffer);
                return new SecurePasswordCombiner(buffer);
            }

            byte[] keyfileBytes = CompositeKeyfilePath.ReadCombinedBytes(keyfilePath, keyfileRequired);
            byte[]? keyfileBase64Bytes = null;
            char[]? keyfileBase64Chars = null;

            try
            {
                // Convert keyfile bytes to Base64 (same as original implementation for compatibility)
                string keyfileBase64 = Convert.ToBase64String(keyfileBytes);
                keyfileBase64Chars = keyfileBase64.ToCharArray();

                // Calculate combined length
                int combinedLength = passphrase.Length + keyfileBase64Chars.Length;
                var combined = new char[combinedLength];

                // Copy passphrase
                passphrase.AsSpan().CopyTo(combined.AsSpan(0, passphrase.Length));

                // Append keyfile
                keyfileBase64Chars.AsSpan().CopyTo(combined.AsSpan(passphrase.Length));

                return new SecurePasswordCombiner(combined);
            }
            finally
            {
                // Zero all intermediate buffers
                if (keyfileBytes != null)
                {
                    CryptographicOperations.ZeroMemory(keyfileBytes);
                }
                if (keyfileBase64Bytes != null)
                {
                    CryptographicOperations.ZeroMemory(keyfileBase64Bytes);
                }
                if (keyfileBase64Chars != null)
                {
                    Array.Clear(keyfileBase64Chars, 0, keyfileBase64Chars.Length);
                }
            }
        }

        /// <summary>
        /// Convenience method for backward compatibility with string passphrases.
        /// NOTE: The string passphrase will remain in memory - prefer using SecurePassword directly.
        /// </summary>
        [Obsolete("Use SecurePassword overload for better security")]
        public static SecurePasswordCombiner Combine(string? passphrase, string? keyfilePath, bool keyfileRequired = false)
        {
            using var securePass = SecurePassword.FromString(passphrase);
            return Combine(securePass, keyfilePath, keyfileRequired);
        }

        /// <summary>
        /// Returns a read-only span over the combined secret.
        /// This span is only valid while the combiner is not disposed.
        /// </summary>
        public ReadOnlySpan<char> AsSpan()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _combinedBuffer;
        }

        /// <summary>
        /// Returns the length of the combined secret.
        /// </summary>
        public int Length
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _combinedBuffer?.Length ?? 0;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                // Zero the buffer using three passes
                if (_combinedBuffer != null && _combinedBuffer.Length > 0)
                {
                    // Pass 1: Random data
                    var random = RandomNumberGenerator.GetBytes(_combinedBuffer.Length * sizeof(char));
                    Buffer.BlockCopy(random, 0, _combinedBuffer, 0, random.Length);

                    // Pass 2: Zeros
                    Array.Clear(_combinedBuffer, 0, _combinedBuffer.Length);

                    // Pass 3: Different random data
                    random = RandomNumberGenerator.GetBytes(_combinedBuffer.Length * sizeof(char));
                    Buffer.BlockCopy(random, 0, _combinedBuffer, 0, random.Length);

                    // Final zero
                    Array.Clear(_combinedBuffer, 0, _combinedBuffer.Length);
                    CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(_combinedBuffer.AsSpan()));
                }
            }
            finally
            {
                if (_pinnedHandle.IsAllocated)
                {
                    _pinnedHandle.Free();
                }

                _combinedBuffer = null;
                _disposed = true;
            }
        }
    }
}
