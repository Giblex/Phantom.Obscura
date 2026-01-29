using System;
using System.Security.Cryptography;
using Org.BouncyCastle.Pqc.Crypto.Crystals.Kyber;
using Org.BouncyCastle.Security;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Production implementation of <see cref="IHybridEncryptionService"/> combining
    /// AES-GCM with ML-KEM-768 (CRYSTALS-Kyber) for post-quantum security.
    /// </summary>
    public sealed class HybridEncryptionService : IHybridEncryptionService
    {
        // ML-KEM-768 (CRYSTALS-Kyber) key and ciphertext sizes
        private const int MLKEM768_PUBLIC_KEY_SIZE = 1184;
        private const int MLKEM768_PRIVATE_KEY_SIZE = 2400;
        private const int MLKEM768_CIPHERTEXT_SIZE = 1088;
        private const int MLKEM768_SHARED_SECRET_SIZE = 32;

        private readonly EncryptionService _encryptionService;
        private readonly SecureRandom _secureRandom;

        public HybridEncryptionService(EncryptionService encryptionService)
        {
            _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
            _secureRandom = new SecureRandom();
        }

        /// <summary>
        /// Generates a new ML-KEM-768 key pair for post-quantum encryption.
        /// ML-KEM-768 provides security equivalent to AES-192 against quantum attacks.
        /// The public key (<see cref="MLKEM768_PUBLIC_KEY_SIZE"/> bytes) should be stored in the manifest,
        /// while the private key (<see cref="MLKEM768_PRIVATE_KEY_SIZE"/> bytes) must be stored inside the encrypted container.
        /// </summary>
        /// <returns>Tuple of (publicKey, privateKey) as byte arrays.</returns>
        public (byte[] publicKey, byte[] privateKey) GenerateKeyPair()
        {
            try
            {
                var keyGenParams = new KyberKeyGenerationParameters(_secureRandom, KyberParameters.kyber768);
                var keyGen = new KyberKeyPairGenerator();
                keyGen.Init(keyGenParams);

                var keyPair = keyGen.GenerateKeyPair();
                var publicKey = ((KyberPublicKeyParameters)keyPair.Public).GetEncoded();
                var privateKey = ((KyberPrivateKeyParameters)keyPair.Private).GetEncoded();

                return (publicKey, privateKey);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to generate ML-KEM-768 key pair.", ex);
            }
        }

        /// <summary>
        /// Encapsulates a shared secret using the recipient's ML-KEM public key.
        /// This produces a ciphertext (<see cref="MLKEM768_CIPHERTEXT_SIZE"/> bytes for Kyber768) that can only be
        /// decrypted by the holder of the corresponding private key.
        /// The shared secret (<see cref="MLKEM768_SHARED_SECRET_SIZE"/> bytes) is used as the AES-GCM key for data encryption.
        /// </summary>
        /// <param name="publicKey">The recipient's ML-KEM-768 public key (<see cref="MLKEM768_PUBLIC_KEY_SIZE"/> bytes).</param>
        /// <returns>Tuple of (ciphertext, sharedSecret).</returns>
        public (byte[] ciphertext, byte[] sharedSecret) EncapsulateSecret(byte[] publicKey)
        {
            if (publicKey == null) throw new ArgumentNullException(nameof(publicKey));
            if (publicKey.Length != MLKEM768_PUBLIC_KEY_SIZE)
                throw new ArgumentException($"Invalid ML-KEM-768 public key size. Expected {MLKEM768_PUBLIC_KEY_SIZE} bytes.", nameof(publicKey));

            try
            {
                var publicKeyParams = new KyberPublicKeyParameters(KyberParameters.kyber768, publicKey);
                var kemGenerator = new KyberKemGenerator(_secureRandom);
                var encapsulation = kemGenerator.GenerateEncapsulated(publicKeyParams);

                var ciphertext = encapsulation.GetEncapsulation();
                var sharedSecret = encapsulation.GetSecret();

                // Ensure we got the expected sizes
                if (ciphertext.Length != MLKEM768_CIPHERTEXT_SIZE)
                    throw new InvalidOperationException($"Unexpected ciphertext size: {ciphertext.Length}, expected {MLKEM768_CIPHERTEXT_SIZE}");
                if (sharedSecret.Length != MLKEM768_SHARED_SECRET_SIZE)
                    throw new InvalidOperationException($"Unexpected shared secret size: {sharedSecret.Length}, expected {MLKEM768_SHARED_SECRET_SIZE}");

                return (ciphertext, sharedSecret);
            }
            catch (Exception ex) when (!(ex is ArgumentException))
            {
                throw new InvalidOperationException("Failed to encapsulate secret with ML-KEM public key.", ex);
            }
        }

        /// <summary>
        /// Decapsulates the shared secret from a KEM ciphertext using the private key.
        /// This recovers the <see cref="MLKEM768_SHARED_SECRET_SIZE"/>-byte symmetric key needed to decrypt the data payload.
        /// </summary>
        /// <param name="ciphertext">The KEM ciphertext (<see cref="MLKEM768_CIPHERTEXT_SIZE"/> bytes) produced during encapsulation.</param>
        /// <param name="privateKey">The ML-KEM-768 private key (<see cref="MLKEM768_PRIVATE_KEY_SIZE"/> bytes).</param>
        /// <returns>The shared secret (<see cref="MLKEM768_SHARED_SECRET_SIZE"/> bytes).</returns>
        public byte[] DecapsulateSecret(byte[] ciphertext, byte[] privateKey)
        {
            if (ciphertext == null) throw new ArgumentNullException(nameof(ciphertext));
            if (privateKey == null) throw new ArgumentNullException(nameof(privateKey));
            if (ciphertext.Length != MLKEM768_CIPHERTEXT_SIZE)
                throw new ArgumentException($"Invalid ML-KEM-768 ciphertext size. Expected {MLKEM768_CIPHERTEXT_SIZE} bytes.", nameof(ciphertext));
            if (privateKey.Length != MLKEM768_PRIVATE_KEY_SIZE)
                throw new ArgumentException($"Invalid ML-KEM-768 private key size. Expected {MLKEM768_PRIVATE_KEY_SIZE} bytes.", nameof(privateKey));

            try
            {
                var privateKeyParams = new KyberPrivateKeyParameters(KyberParameters.kyber768, privateKey);
                var kemExtractor = new KyberKemExtractor(privateKeyParams);
                var sharedSecret = kemExtractor.ExtractSecret(ciphertext);

                if (sharedSecret.Length != 32)
                    throw new InvalidOperationException($"Unexpected shared secret size: {sharedSecret.Length}");

                return sharedSecret;
            }
            catch (Exception ex) when (!(ex is ArgumentException))
            {
                throw new InvalidOperationException("Failed to decapsulate secret with ML-KEM private key.", ex);
            }
        }

        /// <summary>
        /// Encrypts arbitrary plaintext using post-quantum hybrid encryption.
        /// A new shared secret is encapsulated with the provided public key and used
        /// as the AES-256-GCM key. The caller must store both the KEM ciphertext and
        /// the encrypted data together.
        /// </summary>
        /// <param name="plaintext">The data to encrypt.</param>
        /// <param name="kemPublicKey">The recipient's ML-KEM-768 public key.</param>
        /// <param name="aad">Optional associated authenticated data.</param>
        /// <returns>Tuple of (kemCiphertext, encryptedData).</returns>
        public (byte[] kemCiphertext, EncryptionResult encryptedData) EncryptWithHybrid(
            byte[] plaintext,
            byte[] kemPublicKey,
            string aad = "")
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
            if (kemPublicKey == null) throw new ArgumentNullException(nameof(kemPublicKey));

            byte[]? symmetricKey = null;
            try
            {
                // Encapsulate a new shared secret
                var (kemCiphertext, sharedSecret) = EncapsulateSecret(kemPublicKey);
                symmetricKey = sharedSecret;

                // Encrypt data using AES-GCM
                byte[] aadBytes = System.Text.Encoding.UTF8.GetBytes(aad ?? string.Empty);
                var encryptedData = _encryptionService.Encrypt(plaintext, symmetricKey, aadBytes);

                return (kemCiphertext, encryptedData);
            }
            finally
            {
                // Wipe the symmetric key from memory
                if (symmetricKey != null)
                {
                    CryptographicOperations.ZeroMemory(symmetricKey);
                }
            }
        }

        /// <summary>
        /// Decrypts hybrid-encrypted data using the private key.
        /// The shared secret is first recovered via KEM decapsulation, then used
        /// to decrypt the data payload with AES-256-GCM.
        /// </summary>
        /// <param name="kemCiphertext">The KEM ciphertext containing the encapsulated key.</param>
        /// <param name="encryptedData">The AES-GCM encrypted data.</param>
        /// <param name="kemPrivateKey">The ML-KEM-768 private key.</param>
        /// <param name="aad">The associated authenticated data used during encryption.</param>
        /// <returns>The decrypted plaintext.</returns>
        public byte[] DecryptWithHybrid(
            byte[] kemCiphertext,
            EncryptionResult encryptedData,
            byte[] kemPrivateKey,
            string aad = "")
        {
            if (kemCiphertext == null) throw new ArgumentNullException(nameof(kemCiphertext));
            if (encryptedData.Ciphertext == null) throw new ArgumentNullException(nameof(encryptedData));
            if (kemPrivateKey == null) throw new ArgumentNullException(nameof(kemPrivateKey));

            byte[]? symmetricKey = null;
            try
            {
                // Decapsulate the shared secret
                symmetricKey = DecapsulateSecret(kemCiphertext, kemPrivateKey);

                // Decrypt data using AES-GCM
                byte[] aadBytes = System.Text.Encoding.UTF8.GetBytes(aad ?? string.Empty);
                byte[] plaintext = _encryptionService.Decrypt(
                    encryptedData.Ciphertext,
                    encryptedData.Nonce,
                    encryptedData.Tag,
                    symmetricKey,
                    aadBytes);

                return plaintext;
            }
            finally
            {
                // Wipe the symmetric key from memory
                if (symmetricKey != null)
                {
                    CryptographicOperations.ZeroMemory(symmetricKey);
                }
            }
        }
    }
}
