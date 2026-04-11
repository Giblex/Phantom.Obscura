using System;
using System.Security.Cryptography;
using System.Text;
using GiblexVault.Security.ZK.Primitives;

namespace GiblexVault.Security.ZK.Recovery
{
    public static class Recovery
    {
        private static readonly byte[] WrapMagic = Encoding.ASCII.GetBytes("RKW1");
        private static readonly byte[] RecoveryWrapInfo = Encoding.UTF8.GetBytes("PhantomVault.Recovery.Wrap.v1");
        private const int SaltSize = 32;
        private const int NonceSize = 12;
        private const int TagSize = 16;

        public static byte[] GenerateRecoveryKey() => RandomNumberGenerator.GetBytes(32);

        public static byte[] WrapMasterKey(byte[] masterKey, byte[] recoveryKey)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var kek = Hkdf.Sha256(recoveryKey, salt, RecoveryWrapInfo);

            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            using var aes = new AesGcm(kek.AsSpan(), TagSize);
            var tag = new byte[TagSize];
            var ct = new byte[masterKey.Length];
            try
            {
                aes.Encrypt(nonce, masterKey, ct, tag);

                var wrapped = new byte[WrapMagic.Length + SaltSize + NonceSize + ct.Length + TagSize];
                int offset = 0;
                Buffer.BlockCopy(WrapMagic, 0, wrapped, offset, WrapMagic.Length);
                offset += WrapMagic.Length;
                Buffer.BlockCopy(salt, 0, wrapped, offset, SaltSize);
                offset += SaltSize;
                Buffer.BlockCopy(nonce, 0, wrapped, offset, NonceSize);
                offset += NonceSize;
                Buffer.BlockCopy(ct, 0, wrapped, offset, ct.Length);
                offset += ct.Length;
                Buffer.BlockCopy(tag, 0, wrapped, offset, TagSize);
                return wrapped;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(kek);
            }
        }

        public static byte[] UnwrapMasterKey(byte[] wrapped, byte[] recoveryKey)
        {
            if (wrapped == null || wrapped.Length < NonceSize + TagSize)
                throw new ArgumentException("Wrapped recovery payload is malformed.", nameof(wrapped));

            bool usesVersionedFormat = wrapped.Length > WrapMagic.Length + SaltSize + NonceSize + TagSize &&
                wrapped.AsSpan(0, WrapMagic.Length).SequenceEqual(WrapMagic);

            byte[] salt;
            int nonceOffset;
            int ciphertextOffset;
            int ciphertextLength;
            if (usesVersionedFormat)
            {
                salt = wrapped.AsSpan(WrapMagic.Length, SaltSize).ToArray();
                nonceOffset = WrapMagic.Length + SaltSize;
                ciphertextOffset = nonceOffset + NonceSize;
                ciphertextLength = wrapped.Length - ciphertextOffset - TagSize;
            }
            else
            {
                // Legacy format: nonce|ct|tag with an implicit all-zero salt.
                salt = new byte[SaltSize];
                nonceOffset = 0;
                ciphertextOffset = NonceSize;
                ciphertextLength = wrapped.Length - NonceSize - TagSize;
            }

            var kek = Hkdf.Sha256(recoveryKey, salt, RecoveryWrapInfo);

            var nonce = new byte[NonceSize];
            Array.Copy(wrapped, nonceOffset, nonce, 0, NonceSize);

            var ct = new byte[ciphertextLength];
            Array.Copy(wrapped, ciphertextOffset, ct, 0, ciphertextLength);

            var tag = new byte[TagSize];
            Array.Copy(wrapped, wrapped.Length - TagSize, tag, 0, TagSize);

            var plain = new byte[ct.Length];
            using var aes = new AesGcm(kek.AsSpan(), TagSize);
            try
            {
                aes.Decrypt(nonce, ct, tag, plain);
                return plain;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(kek);
            }
        }
    }
}
