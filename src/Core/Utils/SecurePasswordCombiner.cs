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

        /// <param name="passphrase">The user's passphrase; may be empty when a keyfile is used.</param>
        /// <param name="keyfilePath">Keyfile (or composite keyfile set) to fold in.</param>
        /// <param name="keyfileRequired">Fail rather than derive without keyfile material.</param>
        /// <param name="additionalMaterial">
        /// Extra secret appended to the derived secret — the Boot ROM's contribution when the
        /// vault is ROM-bound. Must be supplied identically on read and write, which is why the
        /// only caller takes it from <c>BootRomSession</c> at the single derivation chokepoint.
        /// </param>
        public static SecurePasswordCombiner Combine(
            SecurePassword passphrase,
            string? keyfilePath,
            bool keyfileRequired = false,
            ReadOnlySpan<byte> additionalMaterial = default)
        {
            if (passphrase == null)
            {
                throw new ArgumentNullException(nameof(passphrase));
            }

            char[]? extraChars = additionalMaterial.IsEmpty
                ? null
                : Convert.ToBase64String(additionalMaterial).ToCharArray();
            int extraLength = extraChars?.Length ?? 0;

            try
            {
                if (string.IsNullOrWhiteSpace(keyfilePath))
                {
                    if (keyfileRequired)
                    {
                        throw new SecurityException("Keyfile required but no keyfile path was provided.");
                    }

                    var buffer = new char[passphrase.Length + extraLength];
                    passphrase.AsSpan().CopyTo(buffer);
                    extraChars?.AsSpan().CopyTo(buffer.AsSpan(passphrase.Length));
                    return new SecurePasswordCombiner(buffer);
                }

                byte[] keyfileBytes = CompositeKeyfilePath.ReadCombinedBytes(keyfilePath, keyfileRequired);
                char[]? keyfileBase64Chars = null;

                try
                {

                    string keyfileBase64 = Convert.ToBase64String(keyfileBytes);
                    keyfileBase64Chars = keyfileBase64.ToCharArray();

                    int combinedLength = passphrase.Length + keyfileBase64Chars.Length + extraLength;
                    var combined = new char[combinedLength];

                    passphrase.AsSpan().CopyTo(combined.AsSpan(0, passphrase.Length));

                    keyfileBase64Chars.AsSpan().CopyTo(combined.AsSpan(passphrase.Length));

                    extraChars?.AsSpan().CopyTo(combined.AsSpan(passphrase.Length + keyfileBase64Chars.Length));

                    return new SecurePasswordCombiner(combined);
                }
                finally
                {

                    if (keyfileBytes != null)
                    {
                        CryptographicOperations.ZeroMemory(keyfileBytes);
                    }
                    if (keyfileBase64Chars != null)
                    {
                        Array.Clear(keyfileBase64Chars, 0, keyfileBase64Chars.Length);
                    }
                }
            }
            finally
            {
                if (extraChars != null)
                {
                    Array.Clear(extraChars, 0, extraChars.Length);
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

