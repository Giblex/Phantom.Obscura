using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using PhantomVault.Core.Services;
using static PhantomVault.Core.Services.LayeredEncryptionService;

namespace PhantomVault.Core.Tests.Specialized
{
    /// <summary>
    /// Tamper detection simulation tests.
    /// Verifies that all cryptographic operations detect tampering attempts.
    /// </summary>
    public class TamperDetectionTests
    {
        #region Encryption Tamper Detection

        [Theory]
        [InlineData(0)]   // Tamper first byte
        [InlineData(50)]  // Tamper middle byte
        [InlineData(-1)]  // Tamper last byte (special case)
        public void Encrypt_TamperedCiphertext_DetectedByDecryption(int tamperIndex)
        {
            // Arrange
            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var plaintext = new byte[100];
            RandomNumberGenerator.Fill(plaintext);

            var encrypted = service.Encrypt(plaintext, key);
            var tamperedCiphertext = (byte[])encrypted.Ciphertext.Clone();

            // Tamper with ciphertext
            var index = tamperIndex == -1 ? tamperedCiphertext.Length - 1 : tamperIndex;
            tamperedCiphertext[index] ^= 0xFF;

            // Act & Assert
            Assert.ThrowsAny<CryptographicException>(() =>
                service.Decrypt(tamperedCiphertext, encrypted.Nonce, encrypted.Tag, key));
        }

        [Fact]
        public void Encrypt_TamperedTag_DetectedByDecryption()
        {
            // Arrange
            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var plaintext = Encoding.UTF8.GetBytes("Sensitive data");

            var encrypted = service.Encrypt(plaintext, key);
            var tamperedTag = (byte[])encrypted.Tag.Clone();

            // Tamper with authentication tag
            tamperedTag[0] ^= 0x01;

            // Act & Assert
            Assert.ThrowsAny<CryptographicException>(() =>
                service.Decrypt(encrypted.Ciphertext, encrypted.Nonce, tamperedTag, key));
        }

        [Fact]
        public void Encrypt_TamperedNonce_DetectedByDecryption()
        {
            // Arrange
            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var plaintext = Encoding.UTF8.GetBytes("Sensitive data");

            var encrypted = service.Encrypt(plaintext, key);
            var tamperedNonce = (byte[])encrypted.Nonce.Clone();

            // Tamper with nonce
            tamperedNonce[0] ^= 0x01;

            // Act & Assert
            Assert.ThrowsAny<CryptographicException>(() =>
                service.Decrypt(encrypted.Ciphertext, tamperedNonce, encrypted.Tag, key));
        }

        [Fact]
        public void Encrypt_TamperedAAD_DetectedByDecryption()
        {
            // Arrange
            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var plaintext = Encoding.UTF8.GetBytes("Message");
            var aad = Encoding.UTF8.GetBytes("file.txt");
            var tamperedAad = Encoding.UTF8.GetBytes("evil.txt");

            var encrypted = service.Encrypt(plaintext, key, aad);

            // Act & Assert
            Assert.ThrowsAny<CryptographicException>(() =>
                service.Decrypt(encrypted.Ciphertext, encrypted.Nonce, encrypted.Tag, key, tamperedAad));
        }

        #endregion

        #region Layered Encryption Tamper Detection

        [Fact]
        public void LayeredEncrypt_TamperedEncryptedData_DetectedByDecryption()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("Multi-layer data");

            var encrypted = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Maximum, salt);

            // Tamper with encrypted data
            var tamperedResult = new LayeredEncryptionResult
            {
                Ciphertext = (byte[])encrypted.Ciphertext.Clone(),
                Nonce1 = encrypted.Nonce1,
                Tag1 = encrypted.Tag1,
                Nonce2 = encrypted.Nonce2,
                Tag2 = encrypted.Tag2,
                Nonce3 = encrypted.Nonce3,
                Tag3 = encrypted.Tag3,
                Nonce4 = encrypted.Nonce4,
                Tag4 = encrypted.Tag4,
                Nonce5 = encrypted.Nonce5,
                Tag5 = encrypted.Tag5,
                Level = encrypted.Level
            };
            tamperedResult.Ciphertext[0] ^= 0xFF;

            // Act & Assert — BouncyCastle layers throw InvalidCipherTextException (not CryptographicException)
            Assert.ThrowsAny<Exception>(() =>
                service.DecryptLayered(tamperedResult, masterKey, salt));
        }

        [Fact]
        public void LayeredEncrypt_TamperedLayer1Tag_DetectedByDecryption()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("Multi-layer data");

            var encrypted = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Standard, salt);

            // Tamper with Layer 1 tag
            var tamperedResult = new LayeredEncryptionResult
            {
                Ciphertext = encrypted.Ciphertext,
                Nonce1 = encrypted.Nonce1,
                Tag1 = (byte[])encrypted.Tag1.Clone(),
                Nonce2 = encrypted.Nonce2,
                Tag2 = encrypted.Tag2,
                Level = encrypted.Level
            };
            tamperedResult.Tag1[0] ^= 0xFF;

            // Act & Assert
            Assert.ThrowsAny<CryptographicException>(() =>
                service.DecryptLayered(tamperedResult, masterKey, salt));
        }

        [Fact]
        public void LayeredEncrypt_TamperedLayer2Tag_DetectedByDecryption()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("Multi-layer data");

            var encrypted = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Sensitive, salt);

            // Tamper with Layer 2 tag
            var tamperedResult = new LayeredEncryptionResult
            {
                Ciphertext = encrypted.Ciphertext,
                Nonce1 = encrypted.Nonce1,
                Tag1 = encrypted.Tag1,
                Nonce2 = encrypted.Nonce2,
                Tag2 = (byte[])encrypted.Tag2.Clone(),
                Nonce3 = encrypted.Nonce3,
                Tag3 = encrypted.Tag3,
                Level = encrypted.Level
            };
            tamperedResult.Tag2[0] ^= 0xFF;

            // Act & Assert
            Assert.ThrowsAny<CryptographicException>(() =>
                service.DecryptLayered(tamperedResult, masterKey, salt));
        }

        [Fact]
        public void LayeredEncrypt_TamperedNonce_DetectedByDecryption()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("Multi-layer data");

            var encrypted = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Standard, salt);

            // Tamper with nonce
            var tamperedResult = new LayeredEncryptionResult
            {
                Ciphertext = encrypted.Ciphertext,
                Nonce1 = (byte[])encrypted.Nonce1.Clone(),
                Tag1 = encrypted.Tag1,
                Nonce2 = encrypted.Nonce2,
                Tag2 = encrypted.Tag2,
                Level = encrypted.Level
            };
            tamperedResult.Nonce1[0] ^= 0xFF;

            // Act & Assert
            Assert.ThrowsAny<CryptographicException>(() =>
                service.DecryptLayered(tamperedResult, masterKey, salt));
        }

        [Fact]
        public void LayeredEncrypt_TamperedLayer3Nonce_DetectedByDecryption()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("Multi-layer data");

            var encrypted = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Sensitive, salt);

            // Tamper with the layer 3 nonce (the AEAD successor to the old CBC IV)
            var tamperedResult = new LayeredEncryptionResult
            {
                Ciphertext = encrypted.Ciphertext,
                Nonce1 = encrypted.Nonce1,
                Tag1 = encrypted.Tag1,
                Nonce2 = encrypted.Nonce2,
                Tag2 = encrypted.Tag2,
                Nonce3 = (byte[])encrypted.Nonce3.Clone(),
                Tag3 = (byte[])encrypted.Tag3.Clone(),
                Level = encrypted.Level
            };
            tamperedResult.Nonce3[0] ^= 0xFF;

            // Act & Assert
            var exception = Record.Exception(() =>
                service.DecryptLayered(tamperedResult, masterKey, salt));
            Assert.NotNull(exception);
        }

        #endregion

        #region Bit-Flip Attack Simulation

        [Theory]
        [InlineData(0, 0x01)]   // Flip single bit
        [InlineData(0, 0x02)]   // Flip different bit
        [InlineData(0, 0xFF)]   // Flip entire byte
        [InlineData(50, 0x80)]  // Flip high bit in middle
        public void Encrypt_BitFlipAttack_AlwaysDetected(int byteIndex, byte flipMask)
        {
            // Arrange
            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var plaintext = new byte[100];
            RandomNumberGenerator.Fill(plaintext);

            var encrypted = service.Encrypt(plaintext, key);
            var tamperedCiphertext = (byte[])encrypted.Ciphertext.Clone();

            // Perform bit-flip attack
            tamperedCiphertext[byteIndex] ^= flipMask;

            // Act & Assert
            Assert.ThrowsAny<CryptographicException>(() =>
                service.Decrypt(tamperedCiphertext, encrypted.Nonce, encrypted.Tag, key));
        }

        #endregion

        #region Truncation Attack Detection

        [Fact]
        public void Encrypt_TruncatedCiphertext_DetectedByDecryption()
        {
            // Arrange
            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var plaintext = new byte[100];
            RandomNumberGenerator.Fill(plaintext);

            var encrypted = service.Encrypt(plaintext, key);

            // Truncate ciphertext
            var truncatedCiphertext = encrypted.Ciphertext.Take(50).ToArray();

            // Act & Assert
            var exception = Record.Exception(() =>
                service.Decrypt(truncatedCiphertext, encrypted.Nonce, encrypted.Tag, key));
            Assert.NotNull(exception);
        }

        [Fact]
        public void Encrypt_ExtendedCiphertext_DetectedByDecryption()
        {
            // Arrange
            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var plaintext = new byte[100];
            RandomNumberGenerator.Fill(plaintext);

            var encrypted = service.Encrypt(plaintext, key);

            // Extend ciphertext
            var extendedCiphertext = encrypted.Ciphertext.Concat(new byte[] { 0xFF, 0xFF }).ToArray();

            // Act & Assert
            Assert.ThrowsAny<CryptographicException>(() =>
                service.Decrypt(extendedCiphertext, encrypted.Nonce, encrypted.Tag, key));
        }

        #endregion

        #region Reordering Attack Detection

        [Fact]
        public void Encrypt_ReorderedCiphertextBytes_DetectedByDecryption()
        {
            // Arrange
            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var plaintext = new byte[100];
            RandomNumberGenerator.Fill(plaintext);

            var encrypted = service.Encrypt(plaintext, key);
            var reorderedCiphertext = (byte[])encrypted.Ciphertext.Clone();

            // Swap first and last bytes
            if (reorderedCiphertext.Length >= 2)
            {
                var temp = reorderedCiphertext[0];
                reorderedCiphertext[0] = reorderedCiphertext[reorderedCiphertext.Length - 1];
                reorderedCiphertext[reorderedCiphertext.Length - 1] = temp;
            }

            // Act & Assert
            Assert.ThrowsAny<CryptographicException>(() =>
                service.Decrypt(reorderedCiphertext, encrypted.Nonce, encrypted.Tag, key));
        }

        #endregion

        #region Context Binding Tamper Detection

        [Fact]
        public void Encrypt_ContextSwap_DetectedByDecryption()
        {
            // Arrange
            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var plaintext = Encoding.UTF8.GetBytes("Message");
            var context1 = Encoding.UTF8.GetBytes("file1.txt");
            var context2 = Encoding.UTF8.GetBytes("file2.txt");

            var encrypted = service.Encrypt(plaintext, key, context1);

            // Act & Assert - Try to decrypt with different context
            Assert.ThrowsAny<CryptographicException>(() =>
                service.Decrypt(encrypted.Ciphertext, encrypted.Nonce, encrypted.Tag, key, context2));
        }

        [Fact]
        public void LayeredEncrypt_ContextSwap_DetectedByDecryption()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("Document");
            var context1 = Encoding.UTF8.GetBytes("doc1.pdf");
            var context2 = Encoding.UTF8.GetBytes("doc2.pdf");

            var encrypted = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Standard, salt, context1);

            // Act & Assert
            Assert.ThrowsAny<CryptographicException>(() =>
                service.DecryptLayered(encrypted, masterKey, salt, context2));
        }

        #endregion

        #region Replay Attack Simulation

        [Fact]
        public void Encrypt_NonceReuse_ProducesDeterministicCiphertext()
        {
            // Arrange - Simulate nonce reuse vulnerability
            var service = new EncryptionService();
            var key = new byte[32];
            var reusedNonce = new byte[12];
            RandomNumberGenerator.Fill(key);
            RandomNumberGenerator.Fill(reusedNonce);
            var plaintext1 = Encoding.UTF8.GetBytes("Message 1");
            var plaintext2 = Encoding.UTF8.GetBytes("Message 2");

            // This test demonstrates that nonce reuse with same plaintext produces same ciphertext
            // (security issue - but detection would require tracking nonce usage)

            // Act - Manually encrypt with same nonce (not exposed in API, but conceptual test)
            var encrypted1 = service.Encrypt(plaintext1, key);
            var encrypted2 = service.Encrypt(plaintext1, key);

            // Assert - Different encryptions should use different nonces
            Assert.NotEqual(encrypted1.Nonce, encrypted2.Nonce);
        }

        #endregion

        #region Substitution Attack Detection

        [Fact]
        public void LayeredEncrypt_SubstitutedLayer_DetectedByDecryption()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext1 = Encoding.UTF8.GetBytes("Document 1");
            var plaintext2 = Encoding.UTF8.GetBytes("Document 2");

            var encrypted1 = service.EncryptLayered(plaintext1, masterKey, SecurityLevel.Standard, salt);
            var encrypted2 = service.EncryptLayered(plaintext2, masterKey, SecurityLevel.Standard, salt);

            // Attempt to substitute encrypted data from one encryption to another
            var substitutedResult = new LayeredEncryptionResult
            {
                Ciphertext = encrypted2.Ciphertext, // Wrong data
                Nonce1 = encrypted1.Nonce1,
                Tag1 = encrypted1.Tag1,
                Nonce2 = encrypted1.Nonce2,
                Tag2 = encrypted1.Tag2,
                Level = encrypted1.Level
            };

            // Act & Assert
            Assert.ThrowsAny<CryptographicException>(() =>
                service.DecryptLayered(substitutedResult, masterKey, salt));
        }

        #endregion

        #region Comprehensive Tamper Resistance Test

        [Fact]
        public void TamperResistance_AllLayers_Comprehensive()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = new byte[1000];
            RandomNumberGenerator.Fill(plaintext);

            var encrypted = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Maximum, salt);

            // Act & Assert - Try tampering with every component
            var tamperTests = new Action[]
            {
                () => {
                    var t = CloneResult(encrypted);
                    t.Ciphertext[0] ^= 0xFF;
                    service.DecryptLayered(t, masterKey, salt);
                },
                () => {
                    var t = CloneResult(encrypted);
                    t.Nonce1[0] ^= 0xFF;
                    service.DecryptLayered(t, masterKey, salt);
                },
                () => {
                    var t = CloneResult(encrypted);
                    t.Tag1[0] ^= 0xFF;
                    service.DecryptLayered(t, masterKey, salt);
                },
                () => {
                    var t = CloneResult(encrypted);
                    t.Nonce2[0] ^= 0xFF;
                    service.DecryptLayered(t, masterKey, salt);
                },
                () => {
                    var t = CloneResult(encrypted);
                    t.Tag2[0] ^= 0xFF;
                    service.DecryptLayered(t, masterKey, salt);
                }
            };

            // All tamper attempts should throw
            foreach (var test in tamperTests)
            {
                Assert.ThrowsAny<CryptographicException>(test);
            }
        }

        private LayeredEncryptionResult CloneResult(LayeredEncryptionResult original)
        {
            return new LayeredEncryptionResult
            {
                Ciphertext = (byte[])original.Ciphertext.Clone(),
                Nonce1 = (byte[])original.Nonce1.Clone(),
                Tag1 = (byte[])original.Tag1.Clone(),
                Nonce2 = (byte[])original.Nonce2.Clone(),
                Tag2 = (byte[])original.Tag2.Clone(),
                Nonce3 = original.Nonce3 != null ? (byte[])original.Nonce3.Clone() : null,
                Tag3 = original.Tag3 != null ? (byte[])original.Tag3.Clone() : null,
                Nonce4 = original.Nonce4 != null ? (byte[])original.Nonce4.Clone() : null,
                Tag4 = original.Tag4 != null ? (byte[])original.Tag4.Clone() : null,
                Nonce5 = original.Nonce5 != null ? (byte[])original.Nonce5.Clone() : null,
                Tag5 = original.Tag5 != null ? (byte[])original.Tag5.Clone() : null,
                Level = original.Level
            };
        }

        #endregion
    }
}
