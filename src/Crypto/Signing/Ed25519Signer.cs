using System.Text;
using NSec.Cryptography;

namespace GiblexVault.Security.ZK.Signing
{
    public static class Ed25519Signer
    {
        private static readonly SignatureAlgorithm Alg = SignatureAlgorithm.Ed25519;

        public static (byte[] pub, byte[] priv) Generate()
        {
            using var k = Key.Create(Alg, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
            return (k.Export(KeyBlobFormat.RawPublicKey), k.Export(KeyBlobFormat.RawPrivateKey));
        }

        public static byte[] SignString(string msg, byte[] priv)
        {
            using var k = Key.Import(Alg, priv, KeyBlobFormat.RawPrivateKey);
            return Alg.Sign(k, Encoding.UTF8.GetBytes(msg));
        }

        public static bool VerifyString(string msg, byte[] sig, byte[] pub)
        {
            var pk = PublicKey.Import(Alg, pub, KeyBlobFormat.RawPublicKey);
            return Alg.Verify(pk, Encoding.UTF8.GetBytes(msg), sig);
        }
    }
}