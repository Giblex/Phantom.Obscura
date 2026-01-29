using System;
using System.Security.Cryptography;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public sealed class HybridEncryptionServiceTests
    {
        private static HybridEncryptionService CreateService()
        {
            return new HybridEncryptionService(new EncryptionService());
        }

        [Fact]
        public void GenerateKeyPair_ProducesExpectedSizes()
        {
            // Arrange
            var service = CreateService();

            // Act
            var (publicKey, privateKey) = service.GenerateKeyPair();

            // Assert
            Assert.Equal(1184, publicKey.Length);
            Assert.Equal(2400, privateKey.Length);
        }

        [Fact]
        public void EncryptThenDecrypt_RoundTripsPlaintext()
        {
            // Arrange
            var service = CreateService();
            var (publicKey, privateKey) = service.GenerateKeyPair();
            byte[] plaintext = RandomNumberGenerator.GetBytes(256);
            const string aad = "manifest:sample";

            // Act
            var (kemCiphertext, encryptedData) = service.EncryptWithHybrid(plaintext, publicKey, aad);
            byte[] recovered = service.DecryptWithHybrid(kemCiphertext, encryptedData, privateKey, aad);

            // Assert
            Assert.Equal(plaintext, recovered);
            Assert.Equal(1088, kemCiphertext.Length);
            Assert.NotNull(encryptedData);
            Assert.Equal(plaintext.Length, encryptedData.Ciphertext.Length);
        }

        [Fact]
        public void EncapsulateSecret_WithInvalidKey_Throws()
        {
            // Arrange
            var service = CreateService();
            byte[] invalidKey = new byte[32];

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() => service.EncapsulateSecret(invalidKey));
            Assert.Contains("public key", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DecapsulateSecret_WithInvalidCiphertext_Throws()
        {
            // Arrange
            var service = CreateService();
            var (_, privateKey) = service.GenerateKeyPair();
            byte[] invalidCiphertext = new byte[16];

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() => service.DecapsulateSecret(invalidCiphertext, privateKey));
            Assert.Contains("ciphertext", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
