using System;
using System.Security.Cryptography;
using NSec.Cryptography;
using M = GiblexVault.Security.ZK.Models;

namespace GiblexVault.Security.ZK.Primitives
{
    public static class Aead
    {
        public record Suite(int NonceSize, int TagSize);

        public static Suite GetSuite(M.CipherSuite s) => s == M.CipherSuite.XChaCha20Poly1305 ? new Suite(24, 16) : new Suite(12, 16);

        /// <summary>
        /// Creates an AesGcm instance for reuse across multiple encrypt/decrypt calls.
        /// Caller is responsible for disposing the instance.
        /// </summary>
        public static AesGcm CreateAesGcm(byte[] key) => new AesGcm(key.AsSpan(), 16);

        /// <summary>
        /// Creates an NSec Key instance for reuse across multiple XChaCha20 calls.
        /// Caller is responsible for disposing the instance.
        /// </summary>
        public static Key CreateXChaChaKey(byte[] key) => Key.Import(AeadAlgorithm.XChaCha20Poly1305, key, KeyBlobFormat.RawSymmetricKey);

        public static byte[] Encrypt(M.CipherSuite s, byte[] key, byte[] nonce, byte[] aad, byte[] plain)
        {
            if (s == M.CipherSuite.XChaCha20Poly1305)
            {
                using var k = CreateXChaChaKey(key);
                return EncryptWithKey(k, nonce, aad, plain);
            }
            else
            {
                using var aes = CreateAesGcm(key);
                return EncryptWithAesGcm(aes, nonce, aad, plain);
            }
        }

        /// <summary>
        /// Encrypts using a pre-created AesGcm instance (for AES-256-GCM).
        /// </summary>
        public static byte[] EncryptWithAesGcm(AesGcm aes, byte[] nonce, byte[] aad, byte[] plain)
        {
            var tag = new byte[16];
            var ct = new byte[plain.Length];
            aes.Encrypt(nonce, plain, ct, tag, aad);
            var result = new byte[ct.Length + tag.Length];
            Buffer.BlockCopy(ct, 0, result, 0, ct.Length);
            Buffer.BlockCopy(tag, 0, result, ct.Length, tag.Length);
            return result;
        }

        /// <summary>
        /// Encrypts using a pre-created NSec Key instance (for XChaCha20-Poly1305).
        /// </summary>
        public static byte[] EncryptWithKey(Key k, byte[] nonce, byte[] aad, byte[] plain)
        {
            var alg = AeadAlgorithm.XChaCha20Poly1305;
            var ct = new byte[plain.Length + alg.TagSize];
            alg.Encrypt(k, nonce, aad, plain, ct);
            return ct;
        }

        public static byte[] Decrypt(M.CipherSuite s, byte[] key, byte[] nonce, byte[] aad, byte[] ct)
        {
            if (s == M.CipherSuite.XChaCha20Poly1305)
            {
                using var k = CreateXChaChaKey(key);
                return DecryptWithKey(k, nonce, aad, ct);
            }
            else
            {
                using var aes = CreateAesGcm(key);
                return DecryptWithAesGcm(aes, nonce, aad, ct);
            }
        }

        /// <summary>
        /// Decrypts using a pre-created AesGcm instance (for AES-256-GCM).
        /// </summary>
        public static byte[] DecryptWithAesGcm(AesGcm aes, byte[] nonce, byte[] aad, byte[] ct)
        {
            var tag = new byte[16];
            Buffer.BlockCopy(ct, ct.Length - 16, tag, 0, 16);
            var cLen = ct.Length - 16;
            var c = new byte[cLen];
            Buffer.BlockCopy(ct, 0, c, 0, cLen);
            var plain = new byte[c.Length];
            aes.Decrypt(nonce, c, tag, plain, aad);
            return plain;
        }

        /// <summary>
        /// Decrypts using a pre-created NSec Key instance (for XChaCha20-Poly1305).
        /// </summary>
        public static byte[] DecryptWithKey(Key k, byte[] nonce, byte[] aad, byte[] ct)
        {
            var alg = AeadAlgorithm.XChaCha20Poly1305;
            var plain = new byte[ct.Length - alg.TagSize];
            if (!alg.Decrypt(k, nonce, aad, ct, plain))
                throw new CryptographicException("Bad tag");
            return plain;
        }
    }
}