#nullable enable

using System;
using System.Security.Cryptography;
using PhantomVault.Core.Utils;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    /// <summary>
    /// Unit tests for hybrid key derivation functionality (Phase 2 post-quantum encryption).
    /// Tests the HybridKeyDerivation utility class methods.
    /// </summary>
    public class HybridKeyDerivationTests
    {
        #region DeriveHybridKey Tests

        [Fact]
        public void DeriveHybridKey_ValidInputs_ReturnsCorrectLength()
        {
            // Arrange
            byte[] kek = RandomNumberGenerator.GetBytes(32);
            byte[] kemSharedSecret = RandomNumberGenerator.GetBytes(32);

            // Act
            byte[] hybridKey = HybridKeyDerivation.DeriveHybridKey(kek, kemSharedSecret);

            // Assert
            Assert.NotNull(hybridKey);
            Assert.Equal(32, hybridKey.Length);
        }

        [Fact]
        public void DeriveHybridKey_XorOperation_ProducesCorrectResult()
        {
            // Arrange - Use known values to verify XOR operation
            byte[] kek = new byte[32];
            byte[] kemSharedSecret = new byte[32];
            
            // Set first 4 bytes to known values
            kek[0] = 0xAA; kek[1] = 0xBB; kek[2] = 0xCC; kek[3] = 0xDD;
            kemSharedSecret[0] = 0x11; kemSharedSecret[1] = 0x22; kemSharedSecret[2] = 0x33; kemSharedSecret[3] = 0x44;

            // Act
            byte[] hybridKey = HybridKeyDerivation.DeriveHybridKey(kek, kemSharedSecret);

            // Assert - Verify XOR results
            Assert.Equal(0xAA ^ 0x11, hybridKey[0]); // 0xBB
            Assert.Equal(0xBB ^ 0x22, hybridKey[1]); // 0x99
            Assert.Equal(0xCC ^ 0x33, hybridKey[2]); // 0xFF
            Assert.Equal(0xDD ^ 0x44, hybridKey[3]); // 0x99
        }

        [Fact]
        public void DeriveHybridKey_AllZeros_ReturnsOtherKey()
        {
            // Arrange - XOR with all zeros should return the other key
            byte[] kek = RandomNumberGenerator.GetBytes(32);
            byte[] kemSharedSecret = new byte[32]; // All zeros

            // Act
            byte[] hybridKey = HybridKeyDerivation.DeriveHybridKey(kek, kemSharedSecret);

            // Assert - Should equal kek (anything XOR 0 = anything)
            Assert.Equal(kek, hybridKey);
        }

        [Fact]
        public void DeriveHybridKey_AllOnes_ReturnsInvertedKey()
        {
            // Arrange - XOR with all ones should invert the key
            byte[] kek = new byte[32];
            for (int i = 0; i < 32; i++) kek[i] = 0xAA;
            
            byte[] kemSharedSecret = new byte[32];
            for (int i = 0; i < 32; i++) kemSharedSecret[i] = 0xFF;

            // Act
            byte[] hybridKey = HybridKeyDerivation.DeriveHybridKey(kek, kemSharedSecret);

            // Assert - All bytes should be 0x55 (0xAA XOR 0xFF)
            for (int i = 0; i < 32; i++)
            {
                Assert.Equal(0x55, hybridKey[i]);
            }
        }

        [Fact]
        public void DeriveHybridKey_SameInputs_ReturnsSameOutput()
        {
            // Arrange - Deterministic operation
            byte[] kek = RandomNumberGenerator.GetBytes(32);
            byte[] kemSharedSecret = RandomNumberGenerator.GetBytes(32);

            // Act
            byte[] hybridKey1 = HybridKeyDerivation.DeriveHybridKey(kek, kemSharedSecret);
            byte[] hybridKey2 = HybridKeyDerivation.DeriveHybridKey(kek, kemSharedSecret);

            // Assert
            Assert.Equal(hybridKey1, hybridKey2);
        }

        [Fact]
        public void DeriveHybridKey_DifferentInputs_ReturnsDifferentOutputs()
        {
            // Arrange
            byte[] kek1 = RandomNumberGenerator.GetBytes(32);
            byte[] kek2 = RandomNumberGenerator.GetBytes(32);
            byte[] kemSharedSecret = RandomNumberGenerator.GetBytes(32);

            // Act
            byte[] hybridKey1 = HybridKeyDerivation.DeriveHybridKey(kek1, kemSharedSecret);
            byte[] hybridKey2 = HybridKeyDerivation.DeriveHybridKey(kek2, kemSharedSecret);

            // Assert
            Assert.NotEqual(hybridKey1, hybridKey2);
        }

        [Fact]
        public void DeriveHybridKey_NullKek_ThrowsArgumentNullException()
        {
            // Arrange
            byte[]? kek = null;
            byte[] kemSharedSecret = RandomNumberGenerator.GetBytes(32);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                HybridKeyDerivation.DeriveHybridKey(kek!, kemSharedSecret));
        }

        [Fact]
        public void DeriveHybridKey_NullSharedSecret_ThrowsArgumentNullException()
        {
            // Arrange
            byte[] kek = RandomNumberGenerator.GetBytes(32);
            byte[]? kemSharedSecret = null;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                HybridKeyDerivation.DeriveHybridKey(kek, kemSharedSecret!));
        }

        [Fact]
        public void DeriveHybridKey_InvalidKekLength_ThrowsArgumentException()
        {
            // Arrange - KEK must be 32 bytes
            byte[] kek = RandomNumberGenerator.GetBytes(16); // Wrong size
            byte[] kemSharedSecret = RandomNumberGenerator.GetBytes(32);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => 
                HybridKeyDerivation.DeriveHybridKey(kek, kemSharedSecret));
        }

        [Fact]
        public void DeriveHybridKey_InvalidSharedSecretLength_ThrowsArgumentException()
        {
            // Arrange - Shared secret must be 32 bytes
            byte[] kek = RandomNumberGenerator.GetBytes(32);
            byte[] kemSharedSecret = RandomNumberGenerator.GetBytes(16); // Wrong size

            // Act & Assert
            Assert.Throws<ArgumentException>(() => 
                HybridKeyDerivation.DeriveHybridKey(kek, kemSharedSecret));
        }

        #endregion

        #region SerializeEncryptionResult Tests

        [Fact]
        public void SerializeEncryptionResult_ValidInput_ReturnsCorrectFormat()
        {
            // Arrange
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            byte[] tag = RandomNumberGenerator.GetBytes(16);
            byte[] ciphertext = RandomNumberGenerator.GetBytes(100);
            var encryptionResult = new EncryptionResult(ciphertext, nonce, tag);

            // Act
            string serialized = HybridKeyDerivation.SerializeEncryptionResult(encryptionResult);

            // Assert
            Assert.NotNull(serialized);
            Assert.Contains("|", serialized); // Should have pipe separators
            
            // Verify format: nonce|tag|ciphertext
            string[] parts = serialized.Split('|');
            Assert.Equal(3, parts.Length);
            
            // Verify each part is valid Base64
            Assert.NotEmpty(parts[0]); // nonce
            Assert.NotEmpty(parts[1]); // tag
            Assert.NotEmpty(parts[2]); // ciphertext
        }

        [Fact]
        public void SerializeEncryptionResult_RoundTrip_PreservesData()
        {
            // Arrange
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            byte[] tag = RandomNumberGenerator.GetBytes(16);
            byte[] ciphertext = RandomNumberGenerator.GetBytes(100);
            var originalResult = new EncryptionResult(ciphertext, nonce, tag);

            // Act
            string serialized = HybridKeyDerivation.SerializeEncryptionResult(originalResult);
            var deserialized = HybridKeyDerivation.DeserializeEncryptionResult(serialized);

            // Assert
            Assert.Equal(originalResult.Nonce, deserialized.Nonce);
            Assert.Equal(originalResult.Tag, deserialized.Tag);
            Assert.Equal(originalResult.Ciphertext, deserialized.Ciphertext);
        }

        [Fact]
        public void SerializeEncryptionResult_EmptyCiphertext_HandlesCorrectly()
        {
            // Arrange
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            byte[] tag = RandomNumberGenerator.GetBytes(16);
            byte[] ciphertext = Array.Empty<byte>();
            var encryptionResult = new EncryptionResult(ciphertext, nonce, tag);

            // Act
            string serialized = HybridKeyDerivation.SerializeEncryptionResult(encryptionResult);
            var deserialized = HybridKeyDerivation.DeserializeEncryptionResult(serialized);

            // Assert
            Assert.Empty(deserialized.Ciphertext);
            Assert.Equal(nonce, deserialized.Nonce);
            Assert.Equal(tag, deserialized.Tag);
        }

        #endregion

        #region DeserializeEncryptionResult Tests

        [Fact]
        public void DeserializeEncryptionResult_ValidInput_ReturnsCorrectResult()
        {
            // Arrange
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            byte[] tag = RandomNumberGenerator.GetBytes(16);
            byte[] ciphertext = RandomNumberGenerator.GetBytes(100);
            
            string nonceBase64 = Convert.ToBase64String(nonce);
            string tagBase64 = Convert.ToBase64String(tag);
            string ciphertextBase64 = Convert.ToBase64String(ciphertext);
            string serialized = $"{nonceBase64}|{tagBase64}|{ciphertextBase64}";

            // Act
            var result = HybridKeyDerivation.DeserializeEncryptionResult(serialized);

            // Assert
            Assert.Equal(nonce, result.Nonce);
            Assert.Equal(tag, result.Tag);
            Assert.Equal(ciphertext, result.Ciphertext);
        }

        [Fact]
        public void DeserializeEncryptionResult_InvalidFormat_ThrowsFormatException()
        {
            // Arrange - Missing pipe separator
            string invalidSerialized = "invalidbase64string";

            // Act & Assert
            Assert.Throws<FormatException>(() => 
                HybridKeyDerivation.DeserializeEncryptionResult(invalidSerialized));
        }

        [Fact]
        public void DeserializeEncryptionResult_TooFewParts_ThrowsFormatException()
        {
            // Arrange - Only 2 parts instead of 3
            string invalidSerialized = "part1|part2";

            // Act & Assert
            Assert.Throws<FormatException>(() => 
                HybridKeyDerivation.DeserializeEncryptionResult(invalidSerialized));
        }

        [Fact]
        public void DeserializeEncryptionResult_InvalidBase64_ThrowsFormatException()
        {
            // Arrange - Invalid Base64 characters
            string invalidSerialized = "!!!|###|$$$";

            // Act & Assert
            Assert.Throws<FormatException>(() => 
                HybridKeyDerivation.DeserializeEncryptionResult(invalidSerialized));
        }

        [Fact]
        public void DeserializeEncryptionResult_NullInput_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => 
                HybridKeyDerivation.DeserializeEncryptionResult(null!));
        }

        [Fact]
        public void DeserializeEncryptionResult_EmptyString_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => 
                HybridKeyDerivation.DeserializeEncryptionResult(string.Empty));
        }

        #endregion

        #region ZeroMemory Tests

        [Fact]
        public void ZeroMemory_SingleArray_ZerosAllBytes()
        {
            // Arrange
            byte[] array = RandomNumberGenerator.GetBytes(32);
            byte[] original = (byte[])array.Clone();
            
            // Verify array has non-zero values
            Assert.Contains(array, b => b != 0);

            // Act
            HybridKeyDerivation.ZeroMemory(array);

            // Assert
            Assert.All(array, b => Assert.Equal(0, b));
            Assert.NotEqual(original, array);
        }

        [Fact]
        public void ZeroMemory_MultipleArrays_ZerosAllArrays()
        {
            // Arrange
            byte[] array1 = RandomNumberGenerator.GetBytes(32);
            byte[] array2 = RandomNumberGenerator.GetBytes(64);
            byte[] array3 = RandomNumberGenerator.GetBytes(16);

            // Act
            HybridKeyDerivation.ZeroMemory(array1, array2, array3);

            // Assert
            Assert.All(array1, b => Assert.Equal(0, b));
            Assert.All(array2, b => Assert.Equal(0, b));
            Assert.All(array3, b => Assert.Equal(0, b));
        }

        [Fact]
        public void ZeroMemory_EmptyArray_DoesNotThrow()
        {
            // Arrange
            byte[] array = Array.Empty<byte>();

            // Act & Assert - Should not throw
            HybridKeyDerivation.ZeroMemory(array);
            Assert.Empty(array);
        }

        [Fact]
        public void ZeroMemory_NullArray_HandlesGracefully()
        {
            // Arrange
            byte[]? array = null;

            // Act & Assert - Should not throw
            HybridKeyDerivation.ZeroMemory(array!);
        }

        [Fact]
        public void ZeroMemory_MixedNullAndValid_ZerosValidArrays()
        {
            // Arrange
            byte[]? array1 = null;
            byte[] array2 = RandomNumberGenerator.GetBytes(32);
            byte[]? array3 = null;
            byte[] array4 = RandomNumberGenerator.GetBytes(16);

            // Act
            HybridKeyDerivation.ZeroMemory(array1!, array2, array3!, array4);

            // Assert - Valid arrays should be zeroed
            Assert.All(array2, b => Assert.Equal(0, b));
            Assert.All(array4, b => Assert.Equal(0, b));
        }

        [Fact]
        public void ZeroMemory_LargeArray_ZerosEfficiently()
        {
            // Arrange - Test with large array (1 MB)
            byte[] largeArray = RandomNumberGenerator.GetBytes(1024 * 1024);

            // Act
            HybridKeyDerivation.ZeroMemory(largeArray);

            // Assert - Sample check (don't iterate all bytes for performance)
            Assert.Equal(0, largeArray[0]);
            Assert.Equal(0, largeArray[largeArray.Length / 2]);
            Assert.Equal(0, largeArray[largeArray.Length - 1]);
            
            // Verify all bytes are zero (this might be slow but ensures correctness)
            Assert.All(largeArray, b => Assert.Equal(0, b));
        }

        #endregion

        #region Integration Tests

        [Fact]
        public void HybridKeyDerivation_FullWorkflow_WorksCorrectly()
        {
            // This test simulates the full Phase 2 workflow

            // Arrange - Simulate vault creation
            var encryptionService = new EncryptionService();
            string password = "test-password";
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            
            // Step 1: Derive KEK from password
            byte[] kek = encryptionService.DeriveKey(password.AsSpan(), salt);
            
            // Step 2: Simulate KEM shared secret (would come from ML-KEM encapsulation)
            byte[] kemSharedSecret = RandomNumberGenerator.GetBytes(32);
            
            // Step 3: Derive hybrid DEK
            byte[] hybridDek = HybridKeyDerivation.DeriveHybridKey(kek, kemSharedSecret);
            
            // Step 4: Use DEK to encrypt something
            byte[] testData = System.Text.Encoding.UTF8.GetBytes("sensitive vault data");
            var encrypted = encryptionService.Encrypt(testData, hybridDek);
            
            // Step 5: Serialize for storage
            string serialized = HybridKeyDerivation.SerializeEncryptionResult(encrypted);
            
            // Step 6: Simulate vault unlock - deserialize
            var deserialized = HybridKeyDerivation.DeserializeEncryptionResult(serialized);
            
            // Step 7: Re-derive hybrid DEK (same inputs)
            byte[] hybridDek2 = HybridKeyDerivation.DeriveHybridKey(kek, kemSharedSecret);
            
            // Step 8: Decrypt with re-derived DEK
            byte[] decrypted = encryptionService.Decrypt(
                deserialized.Ciphertext,
                deserialized.Nonce,
                deserialized.Tag,
                hybridDek2);
            
            // Assert
            Assert.Equal(testData, decrypted);
            Assert.Equal(hybridDek, hybridDek2); // DEK should be deterministic
            
            // Cleanup
            HybridKeyDerivation.ZeroMemory(kek, kemSharedSecret, hybridDek, hybridDek2);
        }

        #endregion
    }
}
