using System;
using System.Security.Cryptography;
using Org.BouncyCastle.Pqc.Crypto.Crystals.Kyber;
using Org.BouncyCastle.Security;

namespace PhantomVault.Core.Services
{

    public sealed class HybridEncryptionService : IHybridEncryptionService
    {

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

                var (kemCiphertext, sharedSecret) = EncapsulateSecret(kemPublicKey);
                symmetricKey = sharedSecret;

                byte[] aadBytes = System.Text.Encoding.UTF8.GetBytes(aad ?? string.Empty);
                var encryptedData = _encryptionService.Encrypt(plaintext, symmetricKey, aadBytes);

                return (kemCiphertext, encryptedData);
            }
            finally
            {

                if (symmetricKey != null)
                {
                    CryptographicOperations.ZeroMemory(symmetricKey);
                }
            }
        }

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

                symmetricKey = DecapsulateSecret(kemCiphertext, kemPrivateKey);

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

                if (symmetricKey != null)
                {
                    CryptographicOperations.ZeroMemory(symmetricKey);
                }
            }
        }
    }
}

