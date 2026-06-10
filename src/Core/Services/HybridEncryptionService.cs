using System;
using System.Security.Cryptography;

namespace PhantomVault.Core.Services
{

    public interface IHybridEncryptionService
    {

        (byte[] publicKey, byte[] privateKey) GenerateKeyPair();

        (byte[] ciphertext, byte[] sharedSecret) EncapsulateSecret(byte[] publicKey);

        byte[] DecapsulateSecret(byte[] ciphertext, byte[] privateKey);

        (byte[] kemCiphertext, EncryptionResult encryptedData) EncryptWithHybrid(byte[] plaintext, byte[] kemPublicKey, string aad = "");

        byte[] DecryptWithHybrid(byte[] kemCiphertext, EncryptionResult encryptedData, byte[] kemPrivateKey, string aad = "");
    }
}

