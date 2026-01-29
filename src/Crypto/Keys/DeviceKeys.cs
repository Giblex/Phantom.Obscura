using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NSec.Cryptography;
using System.Security.Cryptography;

namespace GiblexVault.Security.ZK.Keys
{
    [SupportedOSPlatform("windows")]
    public static class DeviceKeys
    {
        private static readonly KeyAgreementAlgorithm Alg = KeyAgreementAlgorithm.X25519;

        public static (byte[] dPub, byte[] dPrivProtected) CreateAndSeal()
        {
            using var k = Key.Create(Alg, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
            var pub = k.Export(KeyBlobFormat.RawPublicKey);
            var priv = k.Export(KeyBlobFormat.RawPrivateKey);
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                throw new PlatformNotSupportedException("Device key sealing via DPAPI is only supported on Windows.");

            var sealedPriv = GiblexVault.Security.ZK.Util.SecurityTuning.KeyProtector.Protect(priv);
            return (pub, sealedPriv);
        }

        public static byte[] DeriveSharedSecret(byte[] mySealedPriv, byte[] theirPub)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new PlatformNotSupportedException("Device key sealing/unsealing via DPAPI is only supported on Windows. Provide a platform-specific sealing implementation.");
            }

            var priv = GiblexVault.Security.ZK.Util.SecurityTuning.KeyProtector.Unprotect(mySealedPriv);
            try
            {
                using var my = Key.Import(Alg, priv, KeyBlobFormat.RawPrivateKey);
                var their = PublicKey.Import(Alg, theirPub, KeyBlobFormat.RawPublicKey);

                // Derive a 32-byte shared secret using HKDF-SHA256 over the ECDH shared secret
                using var shared = Alg.Agree(my, their);
                if (shared == null)
                    throw new CryptographicException("Failed to derive shared secret");

                var okm = new byte[32];
                KeyDerivationAlgorithm.HkdfSha256.DeriveBytes(shared, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty, okm);
                return okm;
            }
            finally
            {
                // Zero the unprotected private key bytes as soon as possible
                try { CryptographicOperations.ZeroMemory(priv); } catch { }
            }
        }
    }
}
