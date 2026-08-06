using System;
using System.Security.Cryptography;
using NSec.Cryptography;
using GiblexVault.Security.ZK.Util;

namespace GiblexVault.Security.ZK.Keys
{
    /// <summary>
    /// Cross-platform device key-agreement helpers used for the manifest-centric
    /// multi-device model. Each device owns an X25519 key-agreement keypair; the
    /// private key is sealed at rest with the platform key protector (DPAPI on
    /// Windows, file-sealed on Unix). A vault key can be wrapped to any device's
    /// public key using an ephemeral-static ECIES construction so only the holder
    /// of that device's private key can unwrap it.
    /// </summary>
    public static class DeviceAgreementKeys
    {
        private static readonly KeyAgreementAlgorithm Alg = KeyAgreementAlgorithm.X25519;
        private static readonly KeyDerivationAlgorithm Kdf = KeyDerivationAlgorithm.HkdfSha256;

        // Domain separation for the HKDF step so a device-wrap key can never collide
        // with any other HKDF usage in the suite.
        private static readonly byte[] WrapInfo =
            System.Text.Encoding.ASCII.GetBytes("PhantomObscura/device-wrap/v1");

        private const int WrapMagic = 0x50_44_57_31; // "PDW1"

        /// <summary>
        /// Generates a new X25519 keypair and returns the raw public key plus the
        /// sealed (protected-at-rest) private key. Cross-platform.
        /// </summary>
        public static (byte[] publicKey, byte[] sealedPrivateKey) CreateSealed()
        {
            using var k = Key.Create(Alg, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
            var pub = k.Export(KeyBlobFormat.RawPublicKey);
            var priv = k.Export(KeyBlobFormat.RawPrivateKey);
            try
            {
                var sealedPriv = SecurityTuning.KeyProtector.Protect(priv);
                return (pub, sealedPriv);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(priv);
            }
        }

        /// <summary>
        /// Wraps a 32-byte vault key so that only the holder of the private key
        /// matching <paramref name="recipientPublicKey"/> can recover it. Returns a
        /// self-describing blob: magic | ephemeralPub(32) | nonce(12) | ciphertext+tag.
        /// </summary>
        public static byte[] WrapVaultKey(byte[] vaultKey, byte[] recipientPublicKey)
        {
            if (vaultKey == null || vaultKey.Length == 0) throw new ArgumentException("Vault key must not be empty", nameof(vaultKey));
            if (recipientPublicKey == null || recipientPublicKey.Length == 0) throw new ArgumentException("Recipient public key must not be empty", nameof(recipientPublicKey));

            var recipient = PublicKey.Import(Alg, recipientPublicKey, KeyBlobFormat.RawPublicKey);

            using var ephemeral = Key.Create(Alg, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
            var ephemeralPub = ephemeral.Export(KeyBlobFormat.RawPublicKey);

            byte[] wrapKeyBytes = new byte[32];
            using (var shared = Alg.Agree(ephemeral, recipient))
            {
                if (shared == null)
                    throw new CryptographicException("X25519 agreement failed during vault-key wrap.");
                Kdf.DeriveBytes(shared, ephemeralPub, WrapInfo, wrapKeyBytes);
            }

            try
            {
                byte[] nonce = RandomNumberGenerator.GetBytes(12);
                byte[] ciphertext = new byte[vaultKey.Length];
                byte[] tag = new byte[16];
                using (var aes = new AesGcm(wrapKeyBytes, 16))
                {
                    aes.Encrypt(nonce, vaultKey, ciphertext, tag);
                }

                byte[] blob = new byte[4 + ephemeralPub.Length + 12 + ciphertext.Length + 16];
                int o = 0;
                BitConverter.GetBytes(WrapMagic).CopyTo(blob, o); o += 4;
                ephemeralPub.CopyTo(blob, o); o += ephemeralPub.Length;
                nonce.CopyTo(blob, o); o += 12;
                ciphertext.CopyTo(blob, o); o += ciphertext.Length;
                tag.CopyTo(blob, o);
                return blob;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(wrapKeyBytes);
            }
        }

        /// <summary>
        /// Unwraps a vault key previously produced by <see cref="WrapVaultKey"/>,
        /// using this device's sealed private key.
        /// </summary>
        public static byte[] UnwrapVaultKey(byte[] wrapped, byte[] sealedPrivateKey)
        {
            if (wrapped == null || wrapped.Length < 4 + 32 + 12 + 16) throw new ArgumentException("Wrapped blob is too short", nameof(wrapped));
            if (sealedPrivateKey == null || sealedPrivateKey.Length == 0) throw new ArgumentException("Sealed private key must not be empty", nameof(sealedPrivateKey));

            int o = 0;
            int magic = BitConverter.ToInt32(wrapped, o); o += 4;
            if (magic != WrapMagic) throw new CryptographicException("Unrecognized device-wrap blob.");

            byte[] ephemeralPub = new byte[32];
            Array.Copy(wrapped, o, ephemeralPub, 0, 32); o += 32;
            byte[] nonce = new byte[12];
            Array.Copy(wrapped, o, nonce, 0, 12); o += 12;
            int ctLen = wrapped.Length - o - 16;
            if (ctLen <= 0) throw new CryptographicException("Wrapped blob has no ciphertext.");
            byte[] ciphertext = new byte[ctLen];
            Array.Copy(wrapped, o, ciphertext, 0, ctLen); o += ctLen;
            byte[] tag = new byte[16];
            Array.Copy(wrapped, o, tag, 0, 16);

            byte[] priv = SecurityTuning.KeyProtector.Unprotect(sealedPrivateKey);
            byte[] wrapKeyBytes = new byte[32];
            try
            {
                using var my = Key.Import(Alg, priv, KeyBlobFormat.RawPrivateKey);
                var ephemeral = PublicKey.Import(Alg, ephemeralPub, KeyBlobFormat.RawPublicKey);
                using (var shared = Alg.Agree(my, ephemeral))
                {
                    if (shared == null)
                        throw new CryptographicException("X25519 agreement failed during vault-key unwrap.");
                    Kdf.DeriveBytes(shared, ephemeralPub, WrapInfo, wrapKeyBytes);
                }

                byte[] plaintext = new byte[ctLen];
                using (var aes = new AesGcm(wrapKeyBytes, 16))
                {
                    aes.Decrypt(nonce, ciphertext, tag, plaintext);
                }
                return plaintext;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(priv);
                CryptographicOperations.ZeroMemory(wrapKeyBytes);
            }
        }
    }
}
