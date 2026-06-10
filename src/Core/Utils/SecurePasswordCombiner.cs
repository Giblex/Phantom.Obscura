using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace PhantomVault.Core.Utils
{

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

        public static SecurePasswordCombiner Combine(SecurePassword passphrase, string? keyfilePath, bool keyfileRequired = false)
        {
            if (passphrase == null)
            {
                throw new ArgumentNullException(nameof(passphrase));
            }

            if (string.IsNullOrWhiteSpace(keyfilePath))
            {
                if (keyfileRequired)
                {
                    throw new SecurityException("Keyfile required but no keyfile path was provided.");
                }

                var buffer = new char[passphrase.Length];
                passphrase.AsSpan().CopyTo(buffer);
                return new SecurePasswordCombiner(buffer);
            }

            byte[] keyfileBytes = CompositeKeyfilePath.ReadCombinedBytes(keyfilePath, keyfileRequired);
            byte[]? keyfileBase64Bytes = null;
            char[]? keyfileBase64Chars = null;

            try
            {

                string keyfileBase64 = Convert.ToBase64String(keyfileBytes);
                keyfileBase64Chars = keyfileBase64.ToCharArray();

                int combinedLength = passphrase.Length + keyfileBase64Chars.Length;
                var combined = new char[combinedLength];

                passphrase.AsSpan().CopyTo(combined.AsSpan(0, passphrase.Length));

                keyfileBase64Chars.AsSpan().CopyTo(combined.AsSpan(passphrase.Length));

                return new SecurePasswordCombiner(combined);
            }
            finally
            {

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

        [Obsolete("Use SecurePassword overload for better security")]
        public static SecurePasswordCombiner Combine(string? passphrase, string? keyfilePath, bool keyfileRequired = false)
        {
            using var securePass = SecurePassword.FromString(passphrase);
            return Combine(securePass, keyfilePath, keyfileRequired);
        }

        public ReadOnlySpan<char> AsSpan()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _combinedBuffer;
        }

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

                if (_combinedBuffer != null && _combinedBuffer.Length > 0)
                {

                    var random = RandomNumberGenerator.GetBytes(_combinedBuffer.Length * sizeof(char));
                    Buffer.BlockCopy(random, 0, _combinedBuffer, 0, random.Length);

                    Array.Clear(_combinedBuffer, 0, _combinedBuffer.Length);

                    random = RandomNumberGenerator.GetBytes(_combinedBuffer.Length * sizeof(char));
                    Buffer.BlockCopy(random, 0, _combinedBuffer, 0, random.Length);

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

