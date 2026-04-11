using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using FsCheck;
using FsCheck.Xunit;
using PhantomVault.Core.Services;
using static PhantomVault.Core.Services.LayeredEncryptionService;

namespace PhantomVault.Core.Tests.Services
{
    /// <summary>
    /// Comprehensive test suite for LayeredEncryptionService covering all security levels:
    /// - Standard (2 layers): AES-256-GCM → ChaCha20-Poly1305
    /// - Sensitive (3 layers): + AES-256-CBC
    /// - Maximum (5 layers): + Twofish-256 → Serpent-256
    /// Target: Critical path 100% coverage
    /// </summary>
    public class LayeredEncryptionServiceComprehensiveTests
    {
        #region Standard Security Level Tests

        [Fact]
        public void EncryptLayered_StandardLevel_TwoLayers_WorksCorrectly()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("Test message for standard security");
            var contextData = Encoding.UTF8.GetBytes("document1.txt");

            // Act
            var encrypted = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Standard, salt, contextData);
            var decrypted = service.DecryptLayered(encrypted, masterKey, salt, contextData);

            // Assert
            Assert.Equal(plaintext, decrypted);
        }

        [Fact]
        public void EncryptLayered_Standard_ProducesValidResult()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("Validate result structure");

            // Act
            var result = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Standard, salt);

            // Assert
            Assert.NotNull(result.Ciphertext);
            Assert.NotNull(result.Nonce1);
            Assert.NotNull(result.Tag1);
            Assert.NotNull(result.Nonce2);
            Assert.NotNull(result.Tag2);
            Assert.Equal(SecurityLevel.Standard, result.Level);
        }

        [Fact]
        public void DecryptLayered_Standard_RequiresCorrectMasterKey()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var wrongKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(wrongKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("Key verification test");

            var encrypted = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Standard, salt);

            // Act & Assert
            Assert.ThrowsAny<CryptographicException>(() =>
                service.DecryptLayered(encrypted, wrongKey, salt));
        }

        [Fact]
        public void DecryptLayered_Standard_RequiresCorrectSalt()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            var wrongSalt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            RandomNumberGenerator.Fill(wrongSalt);
            var plaintext = Encoding.UTF8.GetBytes("Salt verification test");

            var encrypted = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Standard, salt);

            // Act & Assert
            Assert.ThrowsAny<CryptographicException>(() =>
                service.DecryptLayered(encrypted, masterKey, wrongSalt));
        }

        [Fact]
        public void DecryptLayered_Standard_RequiresCorrectContextData()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("Context verification test");
            var contextData = Encoding.UTF8.GetBytes("file1.txt");
            var wrongContext = Encoding.UTF8.GetBytes("file2.txt");

            var encrypted = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Standard, salt, contextData);

            // Act & Assert
            Assert.ThrowsAny<CryptographicException>(() =>
                service.DecryptLayered(encrypted, masterKey, salt, wrongContext));
        }

        #endregion

        #region Sensitive Security Level Tests

        [Fact]
        public void EncryptLayered_SensitiveLevel_ThreeLayers_WorksCorrectly()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("Test message for sensitive security");

            // Act
            var encrypted = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Sensitive, salt);
            var decrypted = service.DecryptLayered(encrypted, masterKey, salt);

            // Assert
            Assert.Equal(plaintext, decrypted);
        }

        [Fact]
        public void EncryptLayered_Sensitive_HasThreeLayerMetadata()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("Three layer test");

            // Act
            var result = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Sensitive, salt);

            // Assert
            Assert.Equal(SecurityLevel.Sensitive, result.Level);
            Assert.NotNull(result.IV3);
            // Layer 3 uses CBC, so has IV instead of nonce+tag
        }

        [Fact]
        public void DecryptLayered_Sensitive_TamperedLayer2_ThrowsException()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("Tamper detection test");

            var encrypted = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Sensitive, salt);

            // Tamper with Layer 2 tag
            var tamperedResult = new LayeredEncryptionResult
            {
                Ciphertext = encrypted.Ciphertext,
                Nonce1 = encrypted.Nonce1,
                Tag1 = encrypted.Tag1,
                Nonce2 = (byte[])encrypted.Nonce2.Clone(),
                Tag2 = (byte[])encrypted.Tag2.Clone(),
                // Nonce3 removed - Layer 3 only has IV3
                IV3 = encrypted.IV3,
                Level = encrypted.Level
            };
            tamperedResult.Tag2[0] ^= 0xFF;

            // Act & Assert
            Assert.ThrowsAny<CryptographicException>(() =>
                service.DecryptLayered(tamperedResult, masterKey, salt));
        }

        #endregion

        #region Maximum Security Level Tests

        [Fact]
        public void EncryptLayered_MaximumLevel_FiveLayers_WorksCorrectly()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("Test message for maximum security");

            // Act
            var encrypted = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Maximum, salt);
            var decrypted = service.DecryptLayered(encrypted, masterKey, salt);

            // Assert
            Assert.Equal(plaintext, decrypted);
        }

        [Fact]
        public void EncryptLayered_Maximum_HasFiveLayerMetadata()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("Five layer test");

            // Act
            var result = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Maximum, salt);

            // Assert
            Assert.Equal(SecurityLevel.Maximum, result.Level);
            Assert.NotNull(result.Nonce1);
            Assert.NotNull(result.Tag1);
            Assert.NotNull(result.Nonce2);
            Assert.NotNull(result.Tag2);
            Assert.NotNull(result.IV3);
            Assert.NotNull(result.IV4);
            Assert.NotNull(result.IV5);
        }

        [Fact]
        public void DecryptLayered_Maximum_TamperedEncryptedData_ThrowsException()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("Data tamper test");

            var encrypted = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Maximum, salt);

            // Tamper with encrypted data
            var tamperedData = (byte[])encrypted.Ciphertext.Clone();
            tamperedData[0] ^= 0xFF;

            var tamperedResult = new LayeredEncryptionResult
            {
                Ciphertext = tamperedData,
                Nonce1 = encrypted.Nonce1,
                Tag1 = encrypted.Tag1,
                Nonce2 = encrypted.Nonce2,
                Tag2 = encrypted.Tag2,
                // Nonce3 removed - Layer 3 only has IV3
                IV3 = encrypted.IV3,
                IV4 = encrypted.IV4,
                IV5 = encrypted.IV5,
                Level = encrypted.Level
            };

            // Act & Assert — BouncyCastle Twofish/Serpent layers throw InvalidCipherTextException (not CryptographicException)
            Assert.ThrowsAny<Exception>(() =>
                service.DecryptLayered(tamperedResult, masterKey, salt));
        }

        #endregion

        #region Edge Cases and Input Validation

        [Fact]
        public void EncryptLayered_EmptyPlaintext_WorksCorrectly()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Array.Empty<byte>();

            // Act
            var encrypted = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Standard, salt);
            var decrypted = service.DecryptLayered(encrypted, masterKey, salt);

            // Assert
            Assert.Empty(decrypted);
        }

        [Fact]
        public void EncryptLayered_LargePlaintext_HandlesCorrectly()
        {
            // Arrange - 5 MB file
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = new byte[5 * 1024 * 1024];
            RandomNumberGenerator.Fill(plaintext);

            // Act
            var encrypted = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Standard, salt);
            var decrypted = service.DecryptLayered(encrypted, masterKey, salt);

            // Assert
            Assert.Equal(plaintext, decrypted);
        }

        [Fact]
        public void EncryptLayered_InvalidMasterKeyLength_ThrowsException()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var invalidKey = new byte[16]; // Must be 32 bytes
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("Test");

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                service.EncryptLayered(plaintext, invalidKey, SecurityLevel.Standard, salt));
        }

        [Fact]
        public void EncryptLayered_InvalidSaltLength_ThrowsException()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var invalidSalt = new byte[8]; // Must be at least 16 bytes
            RandomNumberGenerator.Fill(masterKey);
            var plaintext = Encoding.UTF8.GetBytes("Test");

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                service.EncryptLayered(plaintext, masterKey, SecurityLevel.Standard, invalidSalt));
        }

        [Fact]
        public void EncryptLayered_EmptyContextData_WorksCorrectly()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("No context test");

            // Act - No context data
            var encrypted = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Standard, salt);
            var decrypted = service.DecryptLayered(encrypted, masterKey, salt);

            // Assert
            Assert.Equal(plaintext, decrypted);
        }

        #endregion

        #region Layer Independence Tests

        [Fact]
        public void EncryptLayered_DifferentLevels_ProduceDifferentCiphertexts()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("Compare security levels");

            // Act
            var standard = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Standard, salt);
            var sensitive = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Sensitive, salt);
            var maximum = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Maximum, salt);

            // Assert - All should produce different encrypted data
            Assert.NotEqual(standard.Ciphertext, sensitive.Ciphertext);
            Assert.NotEqual(sensitive.Ciphertext, maximum.Ciphertext);
            Assert.NotEqual(standard.Ciphertext, maximum.Ciphertext);

            // All should decrypt to the same plaintext
            Assert.Equal(plaintext, service.DecryptLayered(standard, masterKey, salt));
            Assert.Equal(plaintext, service.DecryptLayered(sensitive, masterKey, salt));
            Assert.Equal(plaintext, service.DecryptLayered(maximum, masterKey, salt));
        }

        [Fact]
        public void EncryptLayered_SameInputsTwice_ProducesDifferentCiphertexts()
        {
            // Arrange - Test that fresh nonces/IVs are used each time
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("Nonce freshness test");

            // Act
            var result1 = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Standard, salt);
            var result2 = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Standard, salt);

            // Assert - Different nonces should produce different ciphertexts
            Assert.NotEqual(result1.Nonce1, result2.Nonce1);
            Assert.NotEqual(result1.Nonce2, result2.Nonce2);
            Assert.NotEqual(result1.Ciphertext, result2.Ciphertext);

            // Both should decrypt correctly
            Assert.Equal(plaintext, service.DecryptLayered(result1, masterKey, salt));
            Assert.Equal(plaintext, service.DecryptLayered(result2, masterKey, salt));
        }

        #endregion

        #region Binary Data Tests

        [Fact]
        public void EncryptLayered_BinaryData_PreservesAllBytes()
        {
            // Arrange - Data containing all byte values
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

            // Act
            var encrypted = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Maximum, salt);
            var decrypted = service.DecryptLayered(encrypted, masterKey, salt);

            // Assert
            Assert.Equal(plaintext, decrypted);
        }

        #endregion

        #region Property-Based Tests

        [Property(MaxTest = 20)]
        public Property EncryptDecrypt_AllLevels_AlwaysRoundTrips(byte[] plaintext)
        {
            // Arrange
            if (plaintext == null)
                return true.ToProperty();

            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);

            // Act & Assert for each security level
            foreach (SecurityLevel level in Enum.GetValues(typeof(SecurityLevel)))
            {
                var encrypted = service.EncryptLayered(plaintext, masterKey, level, salt);
                var decrypted = service.DecryptLayered(encrypted, masterKey, salt);

                if (!plaintext.SequenceEqual(decrypted))
                    return false.ToProperty();
            }

            return true.ToProperty();
        }

        #endregion

        #region Integration Tests

        [Fact]
        public void FullWorkflow_WithContextData_WorksCorrectly()
        {
            // Arrange - Simulate real-world file encryption
            var service = new LayeredEncryptionService(new EncryptionService());
            var password = "UserPassword123!";
            var encryptionService = new EncryptionService();
            var salt = encryptionService.GenerateSalt();
            var masterKey = encryptionService.DeriveKey(password.AsSpan(), salt);
            var plaintext = Encoding.UTF8.GetBytes("This is a confidential document.");
            var filename = Encoding.UTF8.GetBytes("confidential.txt");

            // Act
            var encrypted = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Sensitive, salt, filename);
            var decrypted = service.DecryptLayered(encrypted, masterKey, salt, filename);

            // Assert
            Assert.Equal(plaintext, decrypted);
        }

        [Fact]
        public void MultipleConcurrentEncryptions_AllSucceed()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);

            var messages = new[]
            {
                "Message 1",
                "Message 2",
                "Message 3"
            };

            // Act - Encrypt multiple messages
            var encrypted = messages
                .Select(m => service.EncryptLayered(
                    Encoding.UTF8.GetBytes(m), masterKey, SecurityLevel.Standard, salt))
                .ToArray();

            // Decrypt all
            var decrypted = encrypted
                .Select(e => Encoding.UTF8.GetString(service.DecryptLayered(e, masterKey, salt)))
                .ToArray();

            // Assert
            Assert.Equal(messages, decrypted);
        }

        #endregion

        #region Performance Characteristic Tests

        [Fact]
        public void EncryptLayered_HigherSecurityLevels_TakeLonger()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = new byte[100 * 1024]; // 100 KB
            RandomNumberGenerator.Fill(plaintext);

            // Act & Measure
            var sw1 = System.Diagnostics.Stopwatch.StartNew();
            service.EncryptLayered(plaintext, masterKey, SecurityLevel.Standard, salt);
            sw1.Stop();

            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            service.EncryptLayered(plaintext, masterKey, SecurityLevel.Maximum, salt);
            sw2.Stop();

            // Assert - Maximum should take longer than Standard
            Assert.True(sw2.ElapsedMilliseconds >= sw1.ElapsedMilliseconds * 0.9,
                $"Maximum ({sw2.ElapsedMilliseconds}ms) should take longer than Standard ({sw1.ElapsedMilliseconds}ms)");
        }

        #endregion

        #region Security Property Tests

        [Fact]
        public void EncryptLayered_OutputDoesNotContainPlaintext()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("SecretMessageDoNotLeak");

            // Act
            var encrypted = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Maximum, salt);

            // Assert - Encrypted data should not contain plaintext
            var encryptedHex = Convert.ToHexString(encrypted.Ciphertext);
            var plaintextHex = Convert.ToHexString(plaintext);
            Assert.DoesNotContain(plaintextHex.Substring(0, Math.Min(8, plaintextHex.Length)), encryptedHex);
        }

        [Fact]
        public void EncryptLayered_DifferentContexts_ProduceIndependentCiphertexts()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("Same message");
            var context1 = Encoding.UTF8.GetBytes("file1.txt");
            var context2 = Encoding.UTF8.GetBytes("file2.txt");

            // Act
            var encrypted1 = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Standard, salt, context1);
            var encrypted2 = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Standard, salt, context2);

            // Assert - Different contexts should produce different ciphertexts
            Assert.NotEqual(encrypted1.Ciphertext, encrypted2.Ciphertext);

            // Cannot decrypt with wrong context
            Assert.ThrowsAny<CryptographicException>(() =>
                service.DecryptLayered(encrypted1, masterKey, salt, context2));
        }

        #endregion

        #region Error Recovery Tests

        [Fact]
        public void DecryptLayered_AfterFailure_ServiceStillWorks()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var wrongKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(wrongKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("Recovery test");

            var encrypted = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Standard, salt);

            // Act - Cause a failure
            try
            {
                service.DecryptLayered(encrypted, wrongKey, salt);
            }
            catch (CryptographicException)
            {
                // Expected
            }

            // Service should still work after failure
            var newEncrypted = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Standard, salt);
            var decrypted = service.DecryptLayered(newEncrypted, masterKey, salt);

            // Assert
            Assert.Equal(plaintext, decrypted);
        }

        #endregion

        #region Specific Algorithm Tests

        [Fact]
        public void Layer1_UsesAesGcm()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("Layer 1 algorithm test");

            // Act
            var result = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Standard, salt);

            // Assert - AES-GCM uses 12-byte nonce and 16-byte tag
            Assert.Equal(12, result.Nonce1.Length);
            Assert.Equal(16, result.Tag1.Length);
        }

        [Fact]
        public void Layer2_UsesChaCha20Poly1305()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("Layer 2 algorithm test");

            // Act
            var result = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Standard, salt);

            // Assert - ChaCha20-Poly1305 uses 12-byte nonce and 16-byte tag
            Assert.Equal(12, result.Nonce2.Length);
            Assert.Equal(16, result.Tag2.Length);
        }

        [Fact]
        public void Layer3_UsesAesCbc()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);
            var plaintext = Encoding.UTF8.GetBytes("Layer 3 algorithm test");

            // Act
            var result = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Sensitive, salt);

            // Assert - AES-CBC uses 16-byte IV, no separate tag
            Assert.NotNull(result.IV3);
            Assert.Equal(16, result.IV3.Length);
        }

        #endregion
    }
}
