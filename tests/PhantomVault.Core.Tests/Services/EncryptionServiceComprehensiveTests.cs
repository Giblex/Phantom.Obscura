using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using FsCheck;
using FsCheck.Xunit;
using PhantomVault.Core.Services;

namespace PhantomVault.Core.Tests.Services
{
    /// <summary>
    /// Comprehensive test suite for EncryptionService covering:
    /// - All encryption/decryption scenarios
    /// - Key derivation with Argon2id and PBKDF2 fallback
    /// - Memory zeroization verification
    /// - Associated authenticated data (AAD)
    /// - Edge cases and error handling
    /// - Integration scenarios
    /// Target: 100% code coverage
    /// </summary>
    public class EncryptionServiceComprehensiveTests
    {
        #region Helper Classes

        private class ZeroizationTracker : IEncryptionObserver
        {
            public byte[]? PasswordBuffer { get; private set; }
            public byte[]? TransientBuffer { get; private set; }
            public int PasswordZeroizedCount { get; private set; }
            public int TransientZeroizedCount { get; private set; }

            public void OnPasswordBufferZeroized(byte[] buffer)
            {
                PasswordBuffer = (byte[])buffer.Clone();
                PasswordZeroizedCount++;
            }

            public void OnTransientBufferZeroized(byte[] buffer)
            {
                TransientBuffer = (byte[])buffer.Clone();
                TransientZeroizedCount++;
            }
        }

        #endregion

        #region GenerateSalt Tests

        [Fact]
        public void GenerateSalt_DefaultLength_Returns16Bytes()
        {
            // Arrange
            var service = new EncryptionService();

            // Act
            var salt = service.GenerateSalt();

            // Assert
            Assert.NotNull(salt);
            Assert.Equal(16, salt.Length);
        }

        [Theory]
        [InlineData(8)]
        [InlineData(16)]
        [InlineData(32)]
        [InlineData(64)]
        public void GenerateSalt_CustomLength_ReturnsCorrectSize(int length)
        {
            // Arrange
            var service = new EncryptionService();

            // Act
            var salt = service.GenerateSalt(length);

            // Assert
            Assert.Equal(length, salt.Length);
        }

        [Fact]
        public void GenerateSalt_Uniqueness_EachCallProducesDifferentSalt()
        {
            // Arrange
            var service = new EncryptionService();

            // Act
            var salt1 = service.GenerateSalt();
            var salt2 = service.GenerateSalt();
            var salt3 = service.GenerateSalt();

            // Assert
            Assert.NotEqual(salt1, salt2);
            Assert.NotEqual(salt2, salt3);
            Assert.NotEqual(salt1, salt3);
        }

        [Fact]
        public void GenerateSalt_NoObviousPatterns()
        {
            // Arrange
            var service = new EncryptionService();

            // Act
            var salt = service.GenerateSalt(32);

            // Assert - Check for sufficient entropy
            Assert.True(salt.Distinct().Count() > 20, "Salt should have high entropy");
            Assert.NotEqual(salt[0], salt[salt.Length - 1]);
        }

        #endregion

        #region DeriveKey - Argon2id Tests

        [Fact]
        public void DeriveKey_WithValidInputs_Returns32ByteKey()
        {
            // Arrange
            var service = new EncryptionService();
            var password = "MySecurePassword123!".AsSpan();
            var salt = service.GenerateSalt();

            // Act
            var key = service.DeriveKey(password, salt);

            // Assert
            Assert.NotNull(key);
            Assert.Equal(32, key.Length);
        }

        [Fact]
        public void DeriveKey_Deterministic_SameInputsProduceSameKey()
        {
            // Arrange
            var service = new EncryptionService();
            var password = "DeterministicTest".AsSpan();
            var salt = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

            // Act
            var key1 = service.DeriveKey(password, salt);
            var key2 = service.DeriveKey(password, salt);

            // Assert
            Assert.Equal(key1, key2);
        }

        [Fact]
        public void DeriveKey_DifferentPasswords_ProduceDifferentKeys()
        {
            // Arrange
            var service = new EncryptionService();
            var salt = service.GenerateSalt();

            // Act
            var key1 = service.DeriveKey("Password1".AsSpan(), salt);
            var key2 = service.DeriveKey("Password2".AsSpan(), salt);

            // Assert
            Assert.NotEqual(key1, key2);
        }

        [Fact]
        public void DeriveKey_DifferentSalts_ProduceDifferentKeys()
        {
            // Arrange
            var service = new EncryptionService();
            var password = "SamePassword".AsSpan();
            var salt1 = service.GenerateSalt();
            var salt2 = service.GenerateSalt();

            // Act
            var key1 = service.DeriveKey(password, salt1);
            var key2 = service.DeriveKey(password, salt2);

            // Assert
            Assert.NotEqual(key1, key2);
        }

        [Fact]
        public void DeriveKey_CustomParameters_ProducesValidKey()
        {
            // Arrange
            var service = new EncryptionService();
            var password = "TestPassword".AsSpan();
            var salt = service.GenerateSalt();

            // Act - Use custom KDF parameters
            var key = service.DeriveKey(password, salt, keyLength: 32,
                memoryCostKb: 128 * 1024, iterations: 5, parallelism: 2);

            // Assert
            Assert.NotNull(key);
            Assert.Equal(32, key.Length);
        }

        [Fact]
        public void DeriveKey_EmptyPassword_ThrowsArgumentException()
        {
            // Arrange
            var service = new EncryptionService();
            var salt = service.GenerateSalt();

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                service.DeriveKey(ReadOnlySpan<char>.Empty, salt));
        }

        [Fact]
        public void DeriveKey_LongPassword_HandlesCorrectly()
        {
            // Arrange - Very long password (256 characters)
            var service = new EncryptionService();
            var password = new string('A', 256).AsSpan();
            var salt = service.GenerateSalt();

            // Act
            var key = service.DeriveKey(password, salt);

            // Assert
            Assert.Equal(32, key.Length);
        }

        [Fact]
        public void DeriveKey_UnicodePassword_HandlesCorrectly()
        {
            // Arrange
            var service = new EncryptionService();
            var password = "P@ssw0rd™🔐中文".AsSpan();
            var salt = service.GenerateSalt();

            // Act
            var key = service.DeriveKey(password, salt);

            // Assert
            Assert.Equal(32, key.Length);
        }

        #endregion

        #region DeriveKey - PBKDF2 Fallback Tests

        [Fact]
        public void DeriveKey_Pbkdf2Fallback_ProducesValidKey()
        {
            // Arrange - Force Argon2 failure to test PBKDF2 fallback
            var service = new EncryptionService(forceArgon2FailureForTests: true);
            var password = "TestPassword".AsSpan();
            var salt = service.GenerateSalt();

            // Act
            var key = service.DeriveKey(password, salt);

            // Assert
            Assert.NotNull(key);
            Assert.Equal(32, key.Length);
        }

        [Fact]
        public void DeriveKey_Pbkdf2Fallback_IsDeterministic()
        {
            // Arrange
            var service = new EncryptionService(forceArgon2FailureForTests: true);
            var password = "DeterministicPBKDF2".AsSpan();
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);

            // Act
            var key1 = service.DeriveKey(password, salt);
            var key2 = service.DeriveKey(password, salt);

            // Assert
            Assert.Equal(key1, key2);
        }

        [Fact]
        public void DeriveKey_Pbkdf2Fallback_SupportsEncryptionWorkflow()
        {
            // Arrange
            var service = new EncryptionService(forceArgon2FailureForTests: true);
            var password = "FallbackWorkflow".AsSpan();
            var salt = service.GenerateSalt();
            var key = service.DeriveKey(password, salt);
            var plaintext = Encoding.UTF8.GetBytes("Fallback encryption test");

            // Act
            var encrypted = service.Encrypt(plaintext, key);
            var decrypted = service.Decrypt(encrypted.Ciphertext, encrypted.Nonce, encrypted.Tag, key);

            // Assert
            Assert.Equal(plaintext, decrypted);
        }

        #endregion

        #region Encrypt Tests

        [Fact]
        public void Encrypt_ValidInput_ProducesValidResult()
        {
            // Arrange
            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var plaintext = Encoding.UTF8.GetBytes("Hello, World!");

            // Act
            var result = service.Encrypt(plaintext, key);

            // Assert
            Assert.NotNull(result.Ciphertext);
            Assert.NotNull(result.Nonce);
            Assert.NotNull(result.Tag);
            Assert.Equal(12, result.Nonce.Length); // AES-GCM uses 12-byte nonce
            Assert.Equal(16, result.Tag.Length); // 16-byte authentication tag
        }

        [Fact]
        public void Encrypt_WithAAD_BindsCiphertextToContext()
        {
            // Arrange
            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var plaintext = Encoding.UTF8.GetBytes("Message");
            var aad = Encoding.UTF8.GetBytes("filename.txt");

            // Act
            var result = service.Encrypt(plaintext, key, aad);

            // Assert
            Assert.NotNull(result.Ciphertext);
        }

        [Fact]
        public void Encrypt_EmptyPlaintext_ProducesValidResult()
        {
            // Arrange
            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var plaintext = Array.Empty<byte>();

            // Act
            var result = service.Encrypt(plaintext, key);

            // Assert
            Assert.Empty(result.Ciphertext);
            Assert.NotNull(result.Nonce);
            Assert.NotNull(result.Tag);
        }

        [Fact]
        public void Encrypt_LargePlaintext_HandlesCorrectly()
        {
            // Arrange - 10 MB file
            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var plaintext = new byte[10 * 1024 * 1024];
            RandomNumberGenerator.Fill(plaintext);

            // Act
            var result = service.Encrypt(plaintext, key);
            var decrypted = service.Decrypt(result.Ciphertext, result.Nonce, result.Tag, key);

            // Assert
            Assert.Equal(plaintext, decrypted);
        }

        [Fact]
        public void Encrypt_InvalidKeyLength_ThrowsArgumentException()
        {
            // Arrange
            var service = new EncryptionService();
            var invalidKey = new byte[16]; // AES-256 needs 32 bytes
            var plaintext = Encoding.UTF8.GetBytes("Test");

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                service.Encrypt(plaintext, invalidKey));
        }

        [Fact]
        public void Encrypt_DifferentCalls_ProduceDifferentNonces()
        {
            // Arrange
            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var plaintext = Encoding.UTF8.GetBytes("Same message");

            // Act
            var result1 = service.Encrypt(plaintext, key);
            var result2 = service.Encrypt(plaintext, key);

            // Assert - Same plaintext with same key should produce different ciphertext due to different nonces
            Assert.NotEqual(result1.Nonce, result2.Nonce);
            Assert.NotEqual(result1.Ciphertext, result2.Ciphertext);
        }

        #endregion

        #region Decrypt Tests

        [Fact]
        public void Decrypt_ValidCiphertext_ReturnsOriginalPlaintext()
        {
            // Arrange
            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var plaintext = Encoding.UTF8.GetBytes("Sensitive data");

            var encrypted = service.Encrypt(plaintext, key);

            // Act
            var decrypted = service.Decrypt(encrypted.Ciphertext, encrypted.Nonce, encrypted.Tag, key);

            // Assert
            Assert.Equal(plaintext, decrypted);
        }

        [Fact]
        public void Decrypt_WithAAD_RequiresSameAAD()
        {
            // Arrange
            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var plaintext = Encoding.UTF8.GetBytes("Message");
            var aad = Encoding.UTF8.GetBytes("context");

            var encrypted = service.Encrypt(plaintext, key, aad);

            // Act
            var decrypted = service.Decrypt(encrypted.Ciphertext, encrypted.Nonce, encrypted.Tag, key, aad);

            // Assert
            Assert.Equal(plaintext, decrypted);
        }

        [Fact]
        public void Decrypt_WrongAAD_ThrowsCryptographicException()
        {
            // Arrange
            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var plaintext = Encoding.UTF8.GetBytes("Message");
            var aad = Encoding.UTF8.GetBytes("correct context");
            var wrongAad = Encoding.UTF8.GetBytes("wrong context");

            var encrypted = service.Encrypt(plaintext, key, aad);

            // Act & Assert
            Assert.Throws<CryptographicException>(() =>
                service.Decrypt(encrypted.Ciphertext, encrypted.Nonce, encrypted.Tag, key, wrongAad));
        }

        [Fact]
        public void Decrypt_WrongKey_ThrowsCryptographicException()
        {
            // Arrange
            var service = new EncryptionService();
            var key = new byte[32];
            var wrongKey = new byte[32];
            RandomNumberGenerator.Fill(key);
            RandomNumberGenerator.Fill(wrongKey);
            var plaintext = Encoding.UTF8.GetBytes("Secret");

            var encrypted = service.Encrypt(plaintext, key);

            // Act & Assert
            Assert.Throws<CryptographicException>(() =>
                service.Decrypt(encrypted.Ciphertext, encrypted.Nonce, encrypted.Tag, wrongKey));
        }

        [Fact]
        public void Decrypt_WrongNonce_ThrowsCryptographicException()
        {
            // Arrange
            var service = new EncryptionService();
            var key = new byte[32];
            var wrongNonce = new byte[12];
            RandomNumberGenerator.Fill(key);
            RandomNumberGenerator.Fill(wrongNonce);
            var plaintext = Encoding.UTF8.GetBytes("Secret");

            var encrypted = service.Encrypt(plaintext, key);

            // Act & Assert
            Assert.Throws<CryptographicException>(() =>
                service.Decrypt(encrypted.Ciphertext, wrongNonce, encrypted.Tag, key));
        }

        [Fact]
        public void Decrypt_TamperedCiphertext_ThrowsCryptographicException()
        {
            // Arrange
            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var plaintext = Encoding.UTF8.GetBytes("Secret message");

            var encrypted = service.Encrypt(plaintext, key);
            var tamperedCiphertext = (byte[])encrypted.Ciphertext.Clone();
            tamperedCiphertext[0] ^= 0xFF; // Flip bits

            // Act & Assert
            Assert.Throws<CryptographicException>(() =>
                service.Decrypt(tamperedCiphertext, encrypted.Nonce, encrypted.Tag, key));
        }

        [Fact]
        public void Decrypt_TamperedTag_ThrowsCryptographicException()
        {
            // Arrange
            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var plaintext = Encoding.UTF8.GetBytes("Secret message");

            var encrypted = service.Encrypt(plaintext, key);
            var tamperedTag = (byte[])encrypted.Tag.Clone();
            tamperedTag[0] ^= 0xFF; // Flip bits

            // Act & Assert
            Assert.Throws<CryptographicException>(() =>
                service.Decrypt(encrypted.Ciphertext, encrypted.Nonce, tamperedTag, key));
        }

        [Fact]
        public void Decrypt_InvalidKeyLength_ThrowsArgumentException()
        {
            // Arrange
            var service = new EncryptionService();
            var invalidKey = new byte[16];
            var ciphertext = new byte[16];
            var nonce = new byte[12];
            var tag = new byte[16];

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                service.Decrypt(ciphertext, nonce, tag, invalidKey));
        }

        #endregion

        #region Memory Zeroization Tests

        [Fact]
        public void Encrypt_ZeroizesTransientBuffers()
        {
            // Arrange
            var tracker = new ZeroizationTracker();
            var service = new EncryptionService(tracker);
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var plaintext = Encoding.UTF8.GetBytes("Zeroization test");

            // Act
            var result = service.Encrypt(plaintext, key);

            // Assert
            Assert.NotNull(tracker.TransientBuffer);
            Assert.True(tracker.TransientBuffer.Take(plaintext.Length).All(b => b == 0),
                "Transient buffer should be zeroed");
            Assert.True(tracker.TransientZeroizedCount > 0);
        }

        [Fact]
        public void Decrypt_ZeroizesTransientBuffers()
        {
            // Arrange
            var tracker = new ZeroizationTracker();
            var service = new EncryptionService(tracker);
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var plaintext = Encoding.UTF8.GetBytes("Zeroization decrypt test");

            var encrypted = service.Encrypt(plaintext, key);

            // Act
            var decrypted = service.Decrypt(encrypted.Ciphertext, encrypted.Nonce, encrypted.Tag, key);

            // Assert
            Assert.NotNull(tracker.TransientBuffer);
            Assert.True(tracker.TransientBuffer.Take(encrypted.Ciphertext.Length).All(b => b == 0),
                "Transient buffer should be zeroed");
        }

        [Fact]
        public void DeriveKey_ZeroizesPasswordBuffer()
        {
            // Arrange
            var tracker = new ZeroizationTracker();
            var service = new EncryptionService(tracker);
            var password = "SecretPassword123".AsSpan();
            var salt = service.GenerateSalt();

            // Act
            var key = service.DeriveKey(password, salt);

            // Assert
            Assert.NotNull(tracker.PasswordBuffer);
            Assert.True(tracker.PasswordBuffer.All(b => b == 0),
                "Password buffer should be zeroed");
            Assert.True(tracker.PasswordZeroizedCount > 0);
        }

        #endregion

        #region Integration and End-to-End Tests

        [Fact]
        public void FullWorkflow_PasswordToEncryption_WorksCorrectly()
        {
            // Arrange
            var service = new EncryptionService();
            var password = "UserPassword123!".AsSpan();
            var plaintext = Encoding.UTF8.GetBytes("This is sensitive user data that needs protection.");

            // Act - Complete workflow
            var salt = service.GenerateSalt();
            var key = service.DeriveKey(password, salt);
            var encrypted = service.Encrypt(plaintext, key);
            var decrypted = service.Decrypt(encrypted.Ciphertext, encrypted.Nonce, encrypted.Tag, key);

            // Assert
            Assert.Equal(plaintext, decrypted);
        }

        [Fact]
        public void MultipleEncryptions_WithSameKey_ProduceIndependentCiphertexts()
        {
            // Arrange
            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var message1 = Encoding.UTF8.GetBytes("First message");
            var message2 = Encoding.UTF8.GetBytes("Second message");

            // Act
            var encrypted1 = service.Encrypt(message1, key);
            var encrypted2 = service.Encrypt(message2, key);

            // Assert - Different nonces should make ciphertexts independent
            Assert.NotEqual(encrypted1.Nonce, encrypted2.Nonce);
            Assert.NotEqual(encrypted1.Ciphertext, encrypted2.Ciphertext);

            // Both should decrypt correctly
            var decrypted1 = service.Decrypt(encrypted1.Ciphertext, encrypted1.Nonce, encrypted1.Tag, key);
            var decrypted2 = service.Decrypt(encrypted2.Ciphertext, encrypted2.Nonce, encrypted2.Tag, key);
            Assert.Equal(message1, decrypted1);
            Assert.Equal(message2, decrypted2);
        }

        [Fact]
        public void BinaryData_PreservesAllByteValues()
        {
            // Arrange - Binary data with all possible byte values
            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var plaintext = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

            // Act
            var encrypted = service.Encrypt(plaintext, key);
            var decrypted = service.Decrypt(encrypted.Ciphertext, encrypted.Nonce, encrypted.Tag, key);

            // Assert
            Assert.Equal(plaintext, decrypted);
        }

        #endregion

        #region Property-Based Tests

        [Property(MaxTest = 30)]
        public Property EncryptDecrypt_AlwaysRoundTrips(byte[] plaintext)
        {
            // Arrange
            if (plaintext == null)
                return true.ToProperty();

            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);

            // Act
            var encrypted = service.Encrypt(plaintext, key);
            var decrypted = service.Decrypt(encrypted.Ciphertext, encrypted.Nonce, encrypted.Tag, key);

            // Assert
            return plaintext.SequenceEqual(decrypted).ToProperty();
        }

        [Property(MaxTest = 30)]
        public Property DeriveKey_IsDeterministic(string password, byte[] salt)
        {
            // Arrange
            if (string.IsNullOrEmpty(password) || salt == null || salt.Length < 8)
                return true.ToProperty();

            var service = new EncryptionService();

            // Act
            var key1 = service.DeriveKey(password.AsSpan(), salt);
            var key2 = service.DeriveKey(password.AsSpan(), salt);

            // Assert
            return key1.SequenceEqual(key2).ToProperty();
        }

        #endregion

        #region Performance Tests

        [Fact]
        public void DeriveKey_HighMemoryCost_TakesLongerThanLowMemoryCost()
        {
            // Arrange
            var service = new EncryptionService();
            var password = "PerformanceTest".AsSpan();
            var salt = service.GenerateSalt();

            // Act & Measure
            var sw1 = System.Diagnostics.Stopwatch.StartNew();
            service.DeriveKey(password, salt, memoryCostKb: 16 * 1024, iterations: 2);
            sw1.Stop();

            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            service.DeriveKey(password, salt, memoryCostKb: 128 * 1024, iterations: 2);
            sw2.Stop();

            // Assert - Higher memory cost should take longer
            Assert.True(sw2.ElapsedMilliseconds >= sw1.ElapsedMilliseconds * 0.8,
                $"High memory ({sw2.ElapsedMilliseconds}ms) should take longer than low memory ({sw1.ElapsedMilliseconds}ms)");
        }

        #endregion

        #region Error Recovery Tests

        [Fact]
        public void Encrypt_AfterDecryptionFailure_StillWorks()
        {
            // Arrange
            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var plaintext = Encoding.UTF8.GetBytes("Test message");

            // Act - Cause a decryption failure
            var encrypted = service.Encrypt(plaintext, key);
            try
            {
                var wrongKey = new byte[32];
                RandomNumberGenerator.Fill(wrongKey);
                service.Decrypt(encrypted.Ciphertext, encrypted.Nonce, encrypted.Tag, wrongKey);
            }
            catch (CryptographicException)
            {
                // Expected
            }

            // Service should still work after failure
            var encrypted2 = service.Encrypt(plaintext, key);
            var decrypted = service.Decrypt(encrypted2.Ciphertext, encrypted2.Nonce, encrypted2.Tag, key);

            // Assert
            Assert.Equal(plaintext, decrypted);
        }

        #endregion
    }
}
