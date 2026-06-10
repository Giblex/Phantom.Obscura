using System;
using System.Security.Cryptography;

namespace PhantomVault.Core.Utils
{

    public static class HybridKeyDerivation
    {

        public static byte[] DeriveHybridKey(byte[] traditionalKey, byte[] kemSharedSecret)
        {
            if (traditionalKey == null)
                throw new ArgumentNullException(nameof(traditionalKey));
            if (kemSharedSecret == null)
                throw new ArgumentNullException(nameof(kemSharedSecret));
            if (traditionalKey.Length != 32)
                throw new ArgumentException("Traditional key must be exactly 32 bytes", nameof(traditionalKey));
            if (kemSharedSecret.Length != 32)
                throw new ArgumentException("KEM shared secret must be exactly 32 bytes", nameof(kemSharedSecret));

            byte[] hybridKey = new byte[32];
            for (int i = 0; i < 32; i++)
            {
                hybridKey[i] = (byte)(traditionalKey[i] ^ kemSharedSecret[i]);
            }
            return hybridKey;
        }

        public static string SerializeEncryptionResult(Services.EncryptionResult result)
        {
            string nonce = Convert.ToBase64String(result.Nonce);
            string tag = Convert.ToBase64String(result.Tag);
            string ciphertext = Convert.ToBase64String(result.Ciphertext);

            return $"{nonce}|{tag}|{ciphertext}";
        }

        public static Services.EncryptionResult DeserializeEncryptionResult(string serialized)
        {
            if (string.IsNullOrEmpty(serialized))
                throw new ArgumentException("Serialized result cannot be null or empty", nameof(serialized));

            string[] parts = serialized.Split('|');
            if (parts.Length != 3)
                throw new FormatException("Invalid serialized encryption result format. Expected: nonce|tag|ciphertext");

            byte[] nonce = Convert.FromBase64String(parts[0]);
            byte[] tag = Convert.FromBase64String(parts[1]);
            byte[] ciphertext = Convert.FromBase64String(parts[2]);

            return new Services.EncryptionResult(ciphertext, nonce, tag);
        }

        public static void ZeroMemory(byte[] data)
        {
            if (data != null)
            {
                CryptographicOperations.ZeroMemory(data);
            }
        }

        public static void ZeroMemory(params byte[][] arrays)
        {
            if (arrays == null) return;

            foreach (var array in arrays)
            {
                ZeroMemory(array);
            }
        }
    }
}

