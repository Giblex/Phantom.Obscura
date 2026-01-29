using System;
using System.Security.Cryptography;

namespace PhantomVault.Core.Utils
{
    /// <summary>
    /// Utilities for deriving hybrid encryption keys that combine classical
    /// password-based key derivation (Argon2id) with post-quantum key
    /// encapsulation mechanisms (ML-KEM-768).
    /// 
    /// The hybrid approach provides defense-in-depth: an attacker must break
    /// both the classical password scheme AND the quantum-resistant KEM to
    /// compromise the vault.
    /// </summary>
    public static class HybridKeyDerivation
    {
        /// <summary>
        /// Derives a hybrid encryption key by XORing a traditional key
        /// (from Argon2id) with a quantum-resistant shared secret (from ML-KEM).
        /// This provides dual resistance: classical password-based security
        /// plus post-quantum key exchange.
        /// 
        /// Security Properties:
        /// - If Argon2id is secure, the hybrid key is secure
        /// - If ML-KEM is secure, the hybrid key is secure
        /// - An attacker must break BOTH to compromise the key
        /// - XOR is information-theoretically secure combiner (one-time pad)
        /// </summary>
        /// <param name="traditionalKey">32-byte key from Argon2id derivation (KEK).</param>
        /// <param name="kemSharedSecret">32-byte shared secret from ML-KEM encapsulation.</param>
        /// <returns>32-byte hybrid Data Encryption Key (DEK).</returns>
        /// <exception cref="ArgumentException">If either input is not exactly 32 bytes.</exception>
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

        /// <summary>
        /// Serializes an EncryptionResult to Base64 string for storage in manifest.
        /// Format: nonce|tag|ciphertext (all Base64 encoded, pipe-separated)
        /// </summary>
        public static string SerializeEncryptionResult(Services.EncryptionResult result)
        {
            string nonce = Convert.ToBase64String(result.Nonce);
            string tag = Convert.ToBase64String(result.Tag);
            string ciphertext = Convert.ToBase64String(result.Ciphertext);

            return $"{nonce}|{tag}|{ciphertext}";
        }

        /// <summary>
        /// Deserializes a Base64 string back to EncryptionResult.
        /// </summary>
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

        /// <summary>
        /// Securely zeros a byte array to remove sensitive key material from memory.
        /// This is a best-effort operation as the GC may have already copied the data.
        /// </summary>
        public static void ZeroMemory(byte[] data)
        {
            if (data != null)
            {
                CryptographicOperations.ZeroMemory(data);
            }
        }

        /// <summary>
        /// Securely zeros multiple byte arrays in a single call.
        /// Useful for cleanup in finally blocks.
        /// </summary>
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
