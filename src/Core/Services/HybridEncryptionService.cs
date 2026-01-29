using System;
using System.Security.Cryptography;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Abstraction for the post-quantum hybrid encryption workflow used during
    /// vault provisioning. Implementations must encapsulate/decrypt secrets
    /// using a Kyber (ML-KEM) key pair and then feed the resulting shared
    /// secret into the symmetric <see cref="EncryptionService"/> helpers.
    /// </summary>
    public interface IHybridEncryptionService
    {
        /// <summary>Generates a Kyber key pair.</summary>
        (byte[] publicKey, byte[] privateKey) GenerateKeyPair();

        /// <summary>Encapsulates a shared secret with the provided public key.</summary>
        (byte[] ciphertext, byte[] sharedSecret) EncapsulateSecret(byte[] publicKey);

        /// <summary>Decapsulates the shared secret using the private key.</summary>
        byte[] DecapsulateSecret(byte[] ciphertext, byte[] privateKey);

        /// <summary>
        /// Encrypts plaintext with AES-GCM using a fresh Kyber-derived shared
        /// secret. Returns the KEM ciphertext alongside the AEAD payload.
        /// </summary>
        (byte[] kemCiphertext, EncryptionResult encryptedData) EncryptWithHybrid(byte[] plaintext, byte[] kemPublicKey, string aad = "");

        /// <summary>
        /// Decrypts a hybrid payload by decapsulating the shared secret and
        /// then decrypting the AES-GCM ciphertext.
        /// </summary>
        byte[] DecryptWithHybrid(byte[] kemCiphertext, EncryptionResult encryptedData, byte[] kemPrivateKey, string aad = "");
    }
}