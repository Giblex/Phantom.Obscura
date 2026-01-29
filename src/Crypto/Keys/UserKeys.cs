using NSec.Cryptography;

namespace GiblexVault.Security.ZK.Keys
{
    public static class UserKeys
    {
        private static readonly SignatureAlgorithm Alg = SignatureAlgorithm.Ed25519;

        public static (byte[] pub, byte[] priv) Generate()
        {
            using var k = Key.Create(Alg, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
            return (k.Export(KeyBlobFormat.RawPublicKey), k.Export(KeyBlobFormat.RawPrivateKey));
        }

        public static byte[] Sign(byte[] msg, byte[] priv)
        {
            using var k = Key.Import(Alg, priv, KeyBlobFormat.RawPrivateKey);
            return Alg.Sign(k, msg);
        }

        public static bool Verify(byte[] msg, byte[] sig, byte[] pub)
        {
            var pk = PublicKey.Import(Alg, pub, KeyBlobFormat.RawPublicKey);
            return Alg.Verify(pk, msg, sig);
        }
    }
}