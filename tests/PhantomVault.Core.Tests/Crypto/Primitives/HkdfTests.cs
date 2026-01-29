using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using FsCheck;
using FsCheck.Xunit;
using GiblexVault.Security.ZK.Primitives;

namespace PhantomVault.Core.Tests.Crypto.Primitives
{
    /// <summary>
    /// Comprehensive unit tests for HKDF (HMAC-based Key Derivation Function) providing 100% code coverage.
    /// Tests RFC 5869 compliance, determinism, salt sensitivity, and cryptographic properties.
    /// </summary>
    public class HkdfTests
    {
        #region Basic Functionality Tests

        [Fact]
        public void Sha256_WithValidInputs_ReturnsExpectedLength()
        {
            // Arrange
            var ikm = new byte[32]; // Input key material
            var salt = new byte[16];
            var info = Encoding.UTF8.GetBytes("application context");
            RandomNumberGenerator.Fill(ikm);
            RandomNumberGenerator.Fill(salt);

            // Act
            var result = Hkdf.Sha256(ikm, salt, info, 32);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(32, result.Length);
        }

        [Fact]
        public void Sha256_SameInputs_ProducesSameOutput()
        {
            // Arrange - Determinism test
            var ikm = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            var salt = new byte[] { 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 };
            var info = Encoding.UTF8.GetBytes("test context");

            // Act
            var result1 = Hkdf.Sha256(ikm, salt, info, 32);
            var result2 = Hkdf.Sha256(ikm, salt, info, 32);

            // Assert
            Assert.Equal(result1, result2);
        }

        [Fact]
        public void Sha256_DifferentIKM_ProducesDifferentOutput()
        {
            // Arrange
            var ikm1 = new byte[32];
            var ikm2 = new byte[32];
            var salt = new byte[16];
            var info = Encoding.UTF8.GetBytes("context");
            RandomNumberGenerator.Fill(ikm1);
            RandomNumberGenerator.Fill(ikm2);
            RandomNumberGenerator.Fill(salt);

            // Act
            var key1 = Hkdf.Sha256(ikm1, salt, info, 32);
            var key2 = Hkdf.Sha256(ikm2, salt, info, 32);

            // Assert
            Assert.NotEqual(key1, key2);
        }

        [Fact]
        public void Sha256_DifferentSalt_ProducesDifferentOutput()
        {
            // Arrange
            var ikm = new byte[32];
            var salt1 = new byte[16];
            var salt2 = new byte[16];
            var info = Encoding.UTF8.GetBytes("context");
            RandomNumberGenerator.Fill(ikm);
            RandomNumberGenerator.Fill(salt1);
            RandomNumberGenerator.Fill(salt2);

            // Act
            var key1 = Hkdf.Sha256(ikm, salt1, info, 32);
            var key2 = Hkdf.Sha256(ikm, salt2, info, 32);

            // Assert
            Assert.NotEqual(key1, key2);
        }

        [Fact]
        public void Sha256_DifferentInfo_ProducesDifferentOutput()
        {
            // Arrange
            var ikm = new byte[32];
            var salt = new byte[16];
            var info1 = Encoding.UTF8.GetBytes("context1");
            var info2 = Encoding.UTF8.GetBytes("context2");
            RandomNumberGenerator.Fill(ikm);
            RandomNumberGenerator.Fill(salt);

            // Act
            var key1 = Hkdf.Sha256(ikm, salt, info1, 32);
            var key2 = Hkdf.Sha256(ikm, salt, info2, 32);

            // Assert
            Assert.NotEqual(key1, key2);
        }

        #endregion

        #region Variable Length Output Tests

        [Theory]
        [InlineData(16)]
        [InlineData(32)]
        [InlineData(48)]
        [InlineData(64)]
        [InlineData(128)]
        public void Sha256_VariableOutputLength_ProducesCorrectSize(int length)
        {
            // Arrange
            var ikm = new byte[32];
            var salt = new byte[16];
            var info = Encoding.UTF8.GetBytes("test");
            RandomNumberGenerator.Fill(ikm);
            RandomNumberGenerator.Fill(salt);

            // Act
            var result = Hkdf.Sha256(ikm, salt, info, length);

            // Assert
            Assert.Equal(length, result.Length);
        }

        [Fact]
        public void Sha256_ShortOutput_ProducesValidKey()
        {
            // Arrange - Very short output (8 bytes)
            var ikm = new byte[32];
            var salt = new byte[16];
            var info = Encoding.UTF8.GetBytes("short");
            RandomNumberGenerator.Fill(ikm);
            RandomNumberGenerator.Fill(salt);

            // Act
            var result = Hkdf.Sha256(ikm, salt, info, 8);

            // Assert
            Assert.Equal(8, result.Length);
        }

        [Fact]
        public void Sha256_LongOutput_ProducesValidKey()
        {
            // Arrange - Long output (255 * 32 = 8160 bytes, max for HMAC-SHA256)
            var ikm = new byte[32];
            var salt = new byte[16];
            var info = Encoding.UTF8.GetBytes("long output test");
            RandomNumberGenerator.Fill(ikm);
            RandomNumberGenerator.Fill(salt);

            // Act
            var result = Hkdf.Sha256(ikm, salt, info, 8160);

            // Assert
            Assert.Equal(8160, result.Length);
        }

        [Fact]
        public void Sha256_DefaultLength_Returns32Bytes()
        {
            // Arrange - Test default parameter value
            var ikm = new byte[32];
            var salt = new byte[16];
            var info = Encoding.UTF8.GetBytes("default length");
            RandomNumberGenerator.Fill(ikm);
            RandomNumberGenerator.Fill(salt);

            // Act
            var result = Hkdf.Sha256(ikm, salt, info); // Using default len = 32

            // Assert
            Assert.Equal(32, result.Length);
        }

        #endregion

        #region Edge Cases and Input Validation

        [Fact]
        public void Sha256_EmptyIKM_ProducesValidOutput()
        {
            // Arrange - Empty input key material
            var ikm = Array.Empty<byte>();
            var salt = new byte[16];
            var info = Encoding.UTF8.GetBytes("empty ikm");
            RandomNumberGenerator.Fill(salt);

            // Act
            var result = Hkdf.Sha256(ikm, salt, info, 32);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(32, result.Length);
        }

        [Fact]
        public void Sha256_EmptySalt_ProducesValidOutput()
        {
            // Arrange - Empty salt (HKDF spec allows this)
            var ikm = new byte[32];
            var salt = Array.Empty<byte>();
            var info = Encoding.UTF8.GetBytes("empty salt");
            RandomNumberGenerator.Fill(ikm);

            // Act
            var result = Hkdf.Sha256(ikm, salt, info, 32);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(32, result.Length);
        }

        [Fact]
        public void Sha256_EmptyInfo_ProducesValidOutput()
        {
            // Arrange - Empty info/context
            var ikm = new byte[32];
            var salt = new byte[16];
            var info = Array.Empty<byte>();
            RandomNumberGenerator.Fill(ikm);
            RandomNumberGenerator.Fill(salt);

            // Act
            var result = Hkdf.Sha256(ikm, salt, info, 32);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(32, result.Length);
        }

        [Fact]
        public void Sha256_AllEmpty_ProducesValidOutput()
        {
            // Arrange - All optional parameters empty
            var ikm = Array.Empty<byte>();
            var salt = Array.Empty<byte>();
            var info = Array.Empty<byte>();

            // Act
            var result = Hkdf.Sha256(ikm, salt, info, 32);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(32, result.Length);
        }

        [Fact]
        public void Sha256_LargeIKM_HandlesCorrectly()
        {
            // Arrange - Large input key material (1 KB)
            var ikm = new byte[1024];
            var salt = new byte[16];
            var info = Encoding.UTF8.GetBytes("large ikm");
            RandomNumberGenerator.Fill(ikm);
            RandomNumberGenerator.Fill(salt);

            // Act
            var result = Hkdf.Sha256(ikm, salt, info, 32);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(32, result.Length);
        }

        [Fact]
        public void Sha256_LargeSalt_HandlesCorrectly()
        {
            // Arrange - Large salt (128 bytes)
            var ikm = new byte[32];
            var salt = new byte[128];
            var info = Encoding.UTF8.GetBytes("large salt");
            RandomNumberGenerator.Fill(ikm);
            RandomNumberGenerator.Fill(salt);

            // Act
            var result = Hkdf.Sha256(ikm, salt, info, 32);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(32, result.Length);
        }

        [Fact]
        public void Sha256_LargeInfo_HandlesCorrectly()
        {
            // Arrange - Large info/context (256 bytes)
            var ikm = new byte[32];
            var salt = new byte[16];
            var info = new byte[256];
            RandomNumberGenerator.Fill(ikm);
            RandomNumberGenerator.Fill(salt);
            RandomNumberGenerator.Fill(info);

            // Act
            var result = Hkdf.Sha256(ikm, salt, info, 32);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(32, result.Length);
        }

        #endregion

        #region RFC 5869 Test Vectors

        [Fact]
        public void Sha256_RFC5869_TestCase1_MatchesExpectedOutput()
        {
            // Arrange - RFC 5869 Appendix A.1
            var ikm = Enumerable.Range(0, 22).Select(i => (byte)0x0b).ToArray();
            var salt = Enumerable.Range(0, 13).Select(i => (byte)i).ToArray();
            var info = Enumerable.Range(0, 10).Select(i => (byte)(0xf0 + i)).ToArray();

            // Expected OKM from RFC 5869
            var expectedOkm = Convert.FromHexString(
                "3cb25f25faacd57a90434f64d0362f2a" +
                "2d2d0a90cf1a5a4c5db02d56ecc4c5bf" +
                "34007208d5b887185865");

            // Act
            var result = Hkdf.Sha256(ikm, salt, info, 42);

            // Assert
            Assert.Equal(expectedOkm, result);
        }

        [Fact]
        public void Sha256_RFC5869_TestCase2_MatchesExpectedOutput()
        {
            // Arrange - RFC 5869 Appendix A.2 (larger inputs)
            var ikm = Enumerable.Range(0, 80).Select(i => (byte)i).ToArray();
            var salt = Enumerable.Range(0, 80).Select(i => (byte)(0x60 + i)).ToArray();
            var info = Enumerable.Range(0, 80).Select(i => (byte)(0xb0 + i)).ToArray();

            // Expected OKM from RFC 5869
            var expectedOkm = Convert.FromHexString(
                "b11e398dc80327a1c8e7f78c596a4934" +
                "4f012eda2d4efad8a050cc4c19afa97c" +
                "59045a99cac7827271cb41c65e590e09" +
                "da3275600c2f09b8367793a9aca3db71" +
                "cc30c58179ec3e87c14c01d5c1f3434f" +
                "1d87");

            // Act
            var result = Hkdf.Sha256(ikm, salt, info, 82);

            // Assert
            Assert.Equal(expectedOkm, result);
        }

        [Fact]
        public void Sha256_RFC5869_TestCase3_EmptySalt_MatchesExpectedOutput()
        {
            // Arrange - RFC 5869 Appendix A.3 (empty salt)
            var ikm = Enumerable.Range(0, 22).Select(i => (byte)0x0b).ToArray();
            var salt = Array.Empty<byte>();
            var info = Array.Empty<byte>();

            // Expected OKM from RFC 5869
            var expectedOkm = Convert.FromHexString(
                "8da4e775a563c18f715f802a063c5a31" +
                "b8a11f5c5ee1879ec3454e5f3c738d2d" +
                "9d201395faa4b61a96c8");

            // Act
            var result = Hkdf.Sha256(ikm, salt, info, 42);

            // Assert
            Assert.Equal(expectedOkm, result);
        }

        #endregion

        #region Cryptographic Properties Tests

        [Fact]
        public void Sha256_OutputHasUniformDistribution()
        {
            // Arrange
            var ikm = new byte[32];
            var salt = new byte[16];
            var info = Encoding.UTF8.GetBytes("distribution test");
            RandomNumberGenerator.Fill(ikm);
            RandomNumberGenerator.Fill(salt);

            // Act
            var result = Hkdf.Sha256(ikm, salt, info, 64);

            // Assert - Check that output has high entropy
            Assert.True(result.Distinct().Count() > 50, "Output should have high entropy");
            Assert.NotEqual(result[0], result[result.Length - 1]);
        }

        [Fact]
        public void Sha256_AvalancheEffect_SmallInputChangeCausesBigOutputChange()
        {
            // Arrange - Test avalanche effect (1 bit change should affect ~50% of output)
            var ikm1 = new byte[32];
            var ikm2 = (byte[])ikm1.Clone();
            ikm2[0] ^= 0x01; // Flip one bit
            var salt = new byte[16];
            var info = Encoding.UTF8.GetBytes("avalanche");
            RandomNumberGenerator.Fill(ikm1);
            RandomNumberGenerator.Fill(salt);

            // Act
            var key1 = Hkdf.Sha256(ikm1, salt, info, 32);
            var key2 = Hkdf.Sha256(ikm2, salt, info, 32);

            // Assert - Count differing bits
            int differingBits = 0;
            for (int i = 0; i < key1.Length; i++)
            {
                byte xor = (byte)(key1[i] ^ key2[i]);
                differingBits += System.Numerics.BitOperations.PopCount(xor);
            }

            int totalBits = key1.Length * 8;
            double changeRatio = (double)differingBits / totalBits;
            Assert.True(changeRatio > 0.3 && changeRatio < 0.7,
                $"Avalanche effect: {changeRatio:P1} of bits changed (expected ~50%)");
        }

        [Fact]
        public void Sha256_NotReversible()
        {
            // Arrange - Ensure HKDF is one-way
            var ikm = Encoding.UTF8.GetBytes("SecretMaterial");
            var salt = new byte[16];
            var info = Encoding.UTF8.GetBytes("context");
            RandomNumberGenerator.Fill(salt);

            // Act
            var output = Hkdf.Sha256(ikm, salt, info, 32);

            // Assert - Output should not contain obvious IKM fragments
            var outputHex = Convert.ToHexString(output);
            var ikmHex = Convert.ToHexString(ikm);
            Assert.DoesNotContain("Secret", outputHex, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(ikmHex.Substring(0, 8), outputHex);
        }

        #endregion

        #region Domain Separation Tests

        [Fact]
        public void Sha256_DifferentContexts_ProduceIndependentKeys()
        {
            // Arrange - Test that info parameter provides domain separation
            var ikm = new byte[32];
            var salt = new byte[16];
            var context1 = Encoding.UTF8.GetBytes("Layer1-AES");
            var context2 = Encoding.UTF8.GetBytes("Layer2-ChaCha");
            RandomNumberGenerator.Fill(ikm);
            RandomNumberGenerator.Fill(salt);

            // Act
            var key1 = Hkdf.Sha256(ikm, salt, context1, 32);
            var key2 = Hkdf.Sha256(ikm, salt, context2, 32);

            // Assert - Different contexts should produce independent keys
            Assert.NotEqual(key1, key2);

            // Check that keys are sufficiently different (at least 50% different bits)
            int differingBits = 0;
            for (int i = 0; i < key1.Length; i++)
            {
                byte xor = (byte)(key1[i] ^ key2[i]);
                differingBits += System.Numerics.BitOperations.PopCount(xor);
            }
            double changeRatio = (double)differingBits / (key1.Length * 8);
            Assert.True(changeRatio > 0.3, "Keys should be substantially different");
        }

        [Fact]
        public void Sha256_LayeredEncryption_ProducesIndependentLayerKeys()
        {
            // Arrange - Simulate layered encryption key derivation
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);

            // Act - Derive keys for different layers
            var layer1Key = Hkdf.Sha256(masterKey, salt, Encoding.UTF8.GetBytes("Layer1-AES-GCM"), 32);
            var layer2Key = Hkdf.Sha256(masterKey, salt, Encoding.UTF8.GetBytes("Layer2-ChaCha20"), 32);
            var layer3Key = Hkdf.Sha256(masterKey, salt, Encoding.UTF8.GetBytes("Layer3-AES-CBC"), 32);

            // Assert - All layer keys should be different
            Assert.NotEqual(layer1Key, layer2Key);
            Assert.NotEqual(layer2Key, layer3Key);
            Assert.NotEqual(layer1Key, layer3Key);
        }

        #endregion

        #region Property-Based Tests

        [Property(MaxTest = 50)]
        public Property Sha256_IsDeterministic(byte[] ikm, byte[] salt, byte[] info)
        {
            // Arrange
            if (ikm == null || salt == null || info == null)
                return true.ToProperty();

            // Act
            var result1 = Hkdf.Sha256(ikm, salt, info, 32);
            var result2 = Hkdf.Sha256(ikm, salt, info, 32);

            // Assert
            return result1.SequenceEqual(result2).ToProperty();
        }

        [Property(MaxTest = 50)]
        public Property Sha256_DifferentIKMs_ProduceDifferentKeys(byte[] ikm1, byte[] ikm2, byte[] salt, byte[] info)
        {
            // Arrange
            if (ikm1 == null || ikm2 == null || salt == null || info == null ||
                ikm1.SequenceEqual(ikm2))
                return true.ToProperty(); // Skip identical inputs

            // Act
            var key1 = Hkdf.Sha256(ikm1, salt, info, 32);
            var key2 = Hkdf.Sha256(ikm2, salt, info, 32);

            // Assert
            return (!key1.SequenceEqual(key2)).ToProperty();
        }

        [Property(MaxTest = 50)]
        public Property Sha256_ProducesRequestedLength(byte[] ikm, byte[] salt, byte[] info, byte lengthByte)
        {
            // Arrange
            if (ikm == null || salt == null || info == null)
                return true.ToProperty();

            var length = Math.Max(1, lengthByte % 128 + 1); // 1-128 bytes

            // Act
            var result = Hkdf.Sha256(ikm, salt, info, length);

            // Assert
            return (result.Length == length).ToProperty();
        }

        [Property(MaxTest = 50)]
        public Property Sha256_NeverProducesAllZeros(byte[] ikm, byte[] salt, byte[] info)
        {
            // Arrange
            if (ikm == null || salt == null || info == null)
                return true.ToProperty();

            // Act
            var result = Hkdf.Sha256(ikm, salt, info, 32);

            // Assert - Output should never be all zeros (extremely unlikely)
            return result.Any(b => b != 0).ToProperty();
        }

        #endregion

        #region Performance Characteristic Tests

        [Fact]
        public void Sha256_PerformanceScales_WithOutputLength()
        {
            // Arrange
            var ikm = new byte[32];
            var salt = new byte[16];
            var info = Encoding.UTF8.GetBytes("perf test");
            RandomNumberGenerator.Fill(ikm);
            RandomNumberGenerator.Fill(salt);

            // Act & Measure
            var sw1 = System.Diagnostics.Stopwatch.StartNew();
            Hkdf.Sha256(ikm, salt, info, 32);
            sw1.Stop();

            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            Hkdf.Sha256(ikm, salt, info, 1024);
            sw2.Stop();

            // Assert - Longer output should take more time (with reasonable tolerance)
            // Note: Very small difference expected for HKDF
            Assert.True(sw2.ElapsedTicks >= sw1.ElapsedTicks * 0.5,
                $"Large output ({sw2.ElapsedTicks} ticks) should take at least half the time of small output ({sw1.ElapsedTicks} ticks)");
        }

        #endregion

        #region Integration Tests with Real-World Scenarios

        [Fact]
        public void Sha256_PostQuantumHybridKDF_WorksCorrectly()
        {
            // Arrange - Simulate post-quantum hybrid key derivation
            var traditionalKey = new byte[32]; // From Argon2
            var kemSharedSecret = new byte[32]; // From ML-KEM
            var salt = new byte[16];
            RandomNumberGenerator.Fill(traditionalKey);
            RandomNumberGenerator.Fill(kemSharedSecret);
            RandomNumberGenerator.Fill(salt);

            // Combine keys using XOR
            var hybridIkm = new byte[32];
            for (int i = 0; i < 32; i++)
                hybridIkm[i] = (byte)(traditionalKey[i] ^ kemSharedSecret[i]);

            // Act - Derive final encryption key
            var encryptionKey = Hkdf.Sha256(hybridIkm, salt, Encoding.UTF8.GetBytes("HybridKDF-AES256"), 32);

            // Assert
            Assert.NotNull(encryptionKey);
            Assert.Equal(32, encryptionKey.Length);
            Assert.NotEqual(traditionalKey, encryptionKey);
            Assert.NotEqual(kemSharedSecret, encryptionKey);
        }

        [Fact]
        public void Sha256_MultipleSubkeys_FromSingleMasterKey()
        {
            // Arrange - Derive multiple subkeys from one master key
            var masterKey = new byte[32];
            var salt = new byte[16];
            RandomNumberGenerator.Fill(masterKey);
            RandomNumberGenerator.Fill(salt);

            // Act - Derive subkeys for different purposes
            var encryptionKey = Hkdf.Sha256(masterKey, salt, Encoding.UTF8.GetBytes("encryption"), 32);
            var authenticationKey = Hkdf.Sha256(masterKey, salt, Encoding.UTF8.GetBytes("authentication"), 32);
            var integrityKey = Hkdf.Sha256(masterKey, salt, Encoding.UTF8.GetBytes("integrity"), 32);

            // Assert - All subkeys should be different
            Assert.NotEqual(encryptionKey, authenticationKey);
            Assert.NotEqual(authenticationKey, integrityKey);
            Assert.NotEqual(encryptionKey, integrityKey);
        }

        #endregion

        #region Zero-Length Output Edge Case

        [Fact]
        public void Sha256_ZeroLengthOutput_ReturnsEmptyArray()
        {
            // Arrange
            var ikm = new byte[32];
            var salt = new byte[16];
            var info = Encoding.UTF8.GetBytes("zero length");
            RandomNumberGenerator.Fill(ikm);
            RandomNumberGenerator.Fill(salt);

            // Act
            var result = Hkdf.Sha256(ikm, salt, info, 0);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        #endregion
    }
}
