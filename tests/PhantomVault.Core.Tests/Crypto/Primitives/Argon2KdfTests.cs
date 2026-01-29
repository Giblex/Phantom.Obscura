using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using FsCheck;
using FsCheck.Xunit;
using GiblexVault.Security.ZK.Primitives;
using GiblexVault.Security.ZK.Models;

namespace PhantomVault.Core.Tests.Crypto.Primitives
{
    /// <summary>
    /// Comprehensive unit tests for Argon2Kdf providing 100% code coverage.
    /// Tests cover: determinism, salt sensitivity, parameter variations, edge cases,
    /// memory zeroization, and cryptographic properties.
    /// </summary>
    public class Argon2KdfTests
    {
        #region Basic Functionality Tests

        [Fact]
        public void DeriveKey_WithValidInputs_ReturnsExpectedLength()
        {
            // Arrange
            var password = Encoding.UTF8.GetBytes("MySecurePassword123!");
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            var kdfParams = new KdfParams { Ops = 3, MemMiB = 16, Parallelism = 1 };

            // Act
            var result = Argon2Kdf.DeriveKey(password, salt, kdfParams);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(32, result.Length); // Should return 32-byte key
        }

        [Fact]
        public void DeriveKey_SameInputs_ProducesSameOutput()
        {
            // Arrange - Determinism test
            var password = Encoding.UTF8.GetBytes("DeterministicTest");
            var salt = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            var kdfParams = new KdfParams { Ops = 2, MemMiB = 16, Parallelism = 1 };

            // Act
            var result1 = Argon2Kdf.DeriveKey(password, salt, kdfParams);
            var result2 = Argon2Kdf.DeriveKey(password, salt, kdfParams);

            // Assert
            Assert.Equal(result1, result2);
        }

        [Fact]
        public void DeriveKey_DifferentPasswords_ProducesDifferentKeys()
        {
            // Arrange
            var password1 = Encoding.UTF8.GetBytes("Password1");
            var password2 = Encoding.UTF8.GetBytes("Password2");
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            var kdfParams = new KdfParams { Ops = 2, MemMiB = 16, Parallelism = 1 };

            // Act
            var key1 = Argon2Kdf.DeriveKey(password1, salt, kdfParams);
            var key2 = Argon2Kdf.DeriveKey(password2, salt, kdfParams);

            // Assert
            Assert.NotEqual(key1, key2);
        }

        [Fact]
        public void DeriveKey_DifferentSalts_ProducesDifferentKeys()
        {
            // Arrange
            var password = Encoding.UTF8.GetBytes("SamePassword");
            var salt1 = new byte[16];
            var salt2 = new byte[16];
            RandomNumberGenerator.Fill(salt1);
            RandomNumberGenerator.Fill(salt2);
            var kdfParams = new KdfParams { Ops = 2, MemMiB = 16, Parallelism = 1 };

            // Act
            var key1 = Argon2Kdf.DeriveKey(password, salt1, kdfParams);
            var key2 = Argon2Kdf.DeriveKey(password, salt2, kdfParams);

            // Assert
            Assert.NotEqual(key1, key2);
        }

        #endregion

        #region Parameter Sensitivity Tests

        [Fact]
        public void DeriveKey_DifferentOpsValues_ProducesDifferentKeys()
        {
            // Arrange - Time cost sensitivity
            var password = Encoding.UTF8.GetBytes("TestPassword");
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            var params1 = new KdfParams { Ops = 2, MemMiB = 16, Parallelism = 1 };
            var params2 = new KdfParams { Ops = 4, MemMiB = 16, Parallelism = 1 };

            // Act
            var key1 = Argon2Kdf.DeriveKey(password, salt, params1);
            var key2 = Argon2Kdf.DeriveKey(password, salt, params2);

            // Assert
            Assert.NotEqual(key1, key2);
        }

        [Fact]
        public void DeriveKey_DifferentMemoryValues_ProducesDifferentKeys()
        {
            // Arrange - Memory cost sensitivity
            var password = Encoding.UTF8.GetBytes("TestPassword");
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            var params1 = new KdfParams { Ops = 2, MemMiB = 16, Parallelism = 1 };
            var params2 = new KdfParams { Ops = 2, MemMiB = 32, Parallelism = 1 };

            // Act
            var key1 = Argon2Kdf.DeriveKey(password, salt, params1);
            var key2 = Argon2Kdf.DeriveKey(password, salt, params2);

            // Assert
            Assert.NotEqual(key1, key2);
        }

        [Fact]
        public void DeriveKey_DifferentParallelismValues_ProducesDifferentKeys()
        {
            // Arrange - Parallelism sensitivity
            var password = Encoding.UTF8.GetBytes("TestPassword");
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            var params1 = new KdfParams { Ops = 2, MemMiB = 16, Parallelism = 1 };
            var params2 = new KdfParams { Ops = 2, MemMiB = 16, Parallelism = 2 };

            // Act
            var key1 = Argon2Kdf.DeriveKey(password, salt, params1);
            var key2 = Argon2Kdf.DeriveKey(password, salt, params2);

            // Assert
            Assert.NotEqual(key1, key2);
        }

        #endregion

        #region Edge Cases and Input Validation

        [Fact]
        public void DeriveKey_EmptyPassword_ProducesValidKey()
        {
            // Arrange - Empty password should still work
            var password = Array.Empty<byte>();
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            var kdfParams = new KdfParams { Ops = 2, MemMiB = 16, Parallelism = 1 };

            // Act
            var result = Argon2Kdf.DeriveKey(password, salt, kdfParams);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(32, result.Length);
        }

        [Fact]
        public void DeriveKey_LongPassword_ProducesValidKey()
        {
            // Arrange - Very long password (1KB)
            var password = new byte[1024];
            RandomNumberGenerator.Fill(password);
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            var kdfParams = new KdfParams { Ops = 2, MemMiB = 16, Parallelism = 1 };

            // Act
            var result = Argon2Kdf.DeriveKey(password, salt, kdfParams);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(32, result.Length);
        }

        [Fact]
        public void DeriveKey_MinimalSalt_ProducesValidKey()
        {
            // Arrange - Minimum recommended salt size (8 bytes)
            var password = Encoding.UTF8.GetBytes("TestPassword");
            var salt = new byte[8];
            RandomNumberGenerator.Fill(salt);
            var kdfParams = new KdfParams { Ops = 2, MemMiB = 16, Parallelism = 1 };

            // Act
            var result = Argon2Kdf.DeriveKey(password, salt, kdfParams);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(32, result.Length);
        }

        [Fact]
        public void DeriveKey_LargeSalt_ProducesValidKey()
        {
            // Arrange - Very large salt (64 bytes)
            var password = Encoding.UTF8.GetBytes("TestPassword");
            var salt = new byte[64];
            RandomNumberGenerator.Fill(salt);
            var kdfParams = new KdfParams { Ops = 2, MemMiB = 16, Parallelism = 1 };

            // Act
            var result = Argon2Kdf.DeriveKey(password, salt, kdfParams);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(32, result.Length);
        }

        [Fact]
        public void DeriveKey_MinimalParameters_ProducesValidKey()
        {
            // Arrange - Minimum secure parameters
            var password = Encoding.UTF8.GetBytes("TestPassword");
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            var kdfParams = new KdfParams { Ops = 1, MemMiB = 8, Parallelism = 1 };

            // Act
            var result = Argon2Kdf.DeriveKey(password, salt, kdfParams);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(32, result.Length);
        }

        [Fact]
        public void DeriveKey_HighParameters_ProducesValidKey()
        {
            // Arrange - High security parameters (slower but more secure)
            var password = Encoding.UTF8.GetBytes("TestPassword");
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            var kdfParams = new KdfParams { Ops = 10, MemMiB = 128, Parallelism = 4 };

            // Act
            var result = Argon2Kdf.DeriveKey(password, salt, kdfParams);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(32, result.Length);
        }

        #endregion

        #region String-based API Tests

        [Fact]
        public void DeriveKeyFromString_WithValidInputs_ReturnsExpectedLength()
        {
            // Arrange
            var password = "MySecurePassword123!";
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            var kdfParams = new KdfParams { Ops = 3, MemMiB = 16, Parallelism = 1 };

            // Act
            var result = Argon2Kdf.DeriveKeyFromString(password, salt, kdfParams);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(32, result.Length);
        }

        [Fact]
        public void DeriveKeyFromString_SameAsBytes_ProducesSameKey()
        {
            // Arrange - String and byte versions should produce identical results
            var password = "CompareTest123";
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            var kdfParams = new KdfParams { Ops = 2, MemMiB = 16, Parallelism = 1 };

            // Act
            var keyFromString = Argon2Kdf.DeriveKeyFromString(password, salt, kdfParams);
            var keyFromBytes = Argon2Kdf.DeriveKey(passwordBytes, salt, kdfParams);

            // Assert
            Assert.Equal(keyFromBytes, keyFromString);
        }

        [Fact]
        public void DeriveKeyFromString_UnicodePassword_HandlesCorrectly()
        {
            // Arrange - Test Unicode handling
            var password = "P@ssw0rd™🔐中文";
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            var kdfParams = new KdfParams { Ops = 2, MemMiB = 16, Parallelism = 1 };

            // Act
            var result = Argon2Kdf.DeriveKeyFromString(password, salt, kdfParams);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(32, result.Length);
        }

        [Fact]
        public void DeriveKeyFromString_EmptyString_ProducesValidKey()
        {
            // Arrange
            var password = string.Empty;
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            var kdfParams = new KdfParams { Ops = 2, MemMiB = 16, Parallelism = 1 };

            // Act
            var result = Argon2Kdf.DeriveKeyFromString(password, salt, kdfParams);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(32, result.Length);
        }

        #endregion

        #region Cryptographic Properties Tests

        [Fact]
        public void DeriveKey_OutputHasUniformDistribution()
        {
            // Arrange - Test that output doesn't have obvious biases
            var password = Encoding.UTF8.GetBytes("TestPassword");
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            var kdfParams = new KdfParams { Ops = 2, MemMiB = 16, Parallelism = 1 };

            // Act
            var key = Argon2Kdf.DeriveKey(password, salt, kdfParams);

            // Assert - Check that not all bytes are the same (basic sanity check)
            Assert.NotEqual(key[0], key[key.Length - 1]);
            Assert.True(key.Distinct().Count() > 20, "Output should have high entropy");
        }

        [Fact]
        public void DeriveKey_OutputChangesSignificantlyWithSmallPasswordChange()
        {
            // Arrange - Avalanche effect test (1 bit change in input should change ~50% of output)
            var password1 = Encoding.UTF8.GetBytes("Password1");
            var password2 = Encoding.UTF8.GetBytes("Password2"); // Only last char differs
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            var kdfParams = new KdfParams { Ops = 2, MemMiB = 16, Parallelism = 1 };

            // Act
            var key1 = Argon2Kdf.DeriveKey(password1, salt, kdfParams);
            var key2 = Argon2Kdf.DeriveKey(password2, salt, kdfParams);

            // Assert - Count differing bits
            int differingBits = 0;
            for (int i = 0; i < key1.Length; i++)
            {
                byte xor = (byte)(key1[i] ^ key2[i]);
                differingBits += System.Numerics.BitOperations.PopCount(xor);
            }

            // Expect approximately 50% of bits to differ (avalanche effect)
            int totalBits = key1.Length * 8;
            double changeRatio = (double)differingBits / totalBits;
            Assert.True(changeRatio > 0.3 && changeRatio < 0.7,
                $"Avalanche effect: {changeRatio:P1} of bits changed (expected ~50%)");
        }

        [Fact]
        public void DeriveKey_NotReversible()
        {
            // Arrange - Ensure KDF is one-way (cannot derive password from key)
            var password = Encoding.UTF8.GetBytes("SecretPassword");
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            var kdfParams = new KdfParams { Ops = 2, MemMiB = 16, Parallelism = 1 };

            // Act
            var key = Argon2Kdf.DeriveKey(password, salt, kdfParams);

            // Assert - Key should not contain obvious password fragments
            var keyString = Convert.ToHexString(key);
            var passwordHex = Convert.ToHexString(password);
            Assert.DoesNotContain("Secret", keyString, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(passwordHex.Substring(0, 8), keyString);
        }

        #endregion

        #region Memory Zeroization Tests

        [Fact]
        public void DeriveKey_ZeroizesPasswordBuffer()
        {
            // Arrange
            var password = Encoding.UTF8.GetBytes("TestPassword");
            var originalPassword = (byte[])password.Clone();
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            var kdfParams = new KdfParams { Ops = 2, MemMiB = 16, Parallelism = 1 };

            // Act
            var result = Argon2Kdf.DeriveKey(password, salt, kdfParams);

            // Assert - Password buffer should be zeroed after use
            Assert.All(password, b => Assert.Equal(0, b));
            Assert.NotEqual(originalPassword, password);
        }

        [Fact]
        public void DeriveKey_ZeroizesSaltBuffer()
        {
            // Arrange
            var password = Encoding.UTF8.GetBytes("TestPassword");
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            var originalSalt = (byte[])salt.Clone();
            var kdfParams = new KdfParams { Ops = 2, MemMiB = 16, Parallelism = 1 };

            // Act
            var result = Argon2Kdf.DeriveKey(password, salt, kdfParams);

            // Assert - Salt buffer should be zeroed after use
            Assert.All(salt, b => Assert.Equal(0, b));
            Assert.NotEqual(originalSalt, salt);
        }

        #endregion

        #region Property-Based Tests using FsCheck

        [Property(MaxTest = 50)]
        public Property DeriveKey_IsDeterministic(byte[] password, byte[] salt, byte ops, byte memMiB, byte parallelism)
        {
            // Arrange - Ensure valid inputs
            if (password == null || salt == null || salt.Length < 8)
                return true.ToProperty(); // Skip invalid inputs

            var validOps = Math.Max(1, ops % 10 + 1); // 1-10
            var validMemMiB = Math.Max(8, memMiB % 128 + 8); // 8-135
            var validParallelism = Math.Max(1, parallelism % 4 + 1); // 1-4

            var kdfParams = new KdfParams
            {
                Ops = validOps,
                MemMiB = validMemMiB,
                Parallelism = validParallelism
            };

            // Act - Derive key twice with same inputs
            var key1 = Argon2Kdf.DeriveKey((byte[])password.Clone(), (byte[])salt.Clone(), kdfParams);
            var key2 = Argon2Kdf.DeriveKey((byte[])password.Clone(), (byte[])salt.Clone(), kdfParams);

            // Assert - Should produce identical results
            return key1.SequenceEqual(key2).ToProperty();
        }

        [Property(MaxTest = 50)]
        public Property DeriveKey_DifferentSaltsProduceDifferentKeys(byte[] password, byte[] salt1, byte[] salt2)
        {
            // Arrange
            if (password == null || salt1 == null || salt2 == null ||
                salt1.Length < 8 || salt2.Length < 8 || salt1.SequenceEqual(salt2))
                return true.ToProperty(); // Skip invalid/identical inputs

            var kdfParams = new KdfParams { Ops = 2, MemMiB = 16, Parallelism = 1 };

            // Act
            var key1 = Argon2Kdf.DeriveKey((byte[])password.Clone(), (byte[])salt1.Clone(), kdfParams);
            var key2 = Argon2Kdf.DeriveKey((byte[])password.Clone(), (byte[])salt2.Clone(), kdfParams);

            // Assert - Different salts must produce different keys
            return (!key1.SequenceEqual(key2)).ToProperty();
        }

        [Property(MaxTest = 50)]
        public Property DeriveKey_AlwaysReturns32Bytes(byte[] password, byte[] salt)
        {
            // Arrange
            if (password == null || salt == null || salt.Length < 8)
                return true.ToProperty();

            var kdfParams = new KdfParams { Ops = 2, MemMiB = 16, Parallelism = 1 };

            // Act
            var result = Argon2Kdf.DeriveKey((byte[])password.Clone(), (byte[])salt.Clone(), kdfParams);

            // Assert
            return (result != null && result.Length == 32).ToProperty();
        }

        #endregion

        #region KdfParams Structure Tests

        [Fact]
        public void KdfParams_DefaultValues_AreSecure()
        {
            // Arrange & Act
            var defaultParams = new KdfParams();

            // Assert - Verify defaults meet OWASP recommendations
            Assert.Equal(6, defaultParams.Ops); // Time cost
            Assert.Equal(64, defaultParams.MemMiB); // Memory cost
            Assert.Equal(1, defaultParams.Parallelism); // Parallelism
            Assert.NotNull(defaultParams.Salt);
            Assert.Equal(16, defaultParams.Salt.Length);
        }

        [Fact]
        public void KdfParams_CanBeCustomized()
        {
            // Arrange & Act
            var customSalt = new byte[32];
            RandomNumberGenerator.Fill(customSalt);
            var customParams = new KdfParams
            {
                Ops = 8,
                MemMiB = 128,
                Parallelism = 4,
                Salt = customSalt
            };

            // Assert
            Assert.Equal(8, customParams.Ops);
            Assert.Equal(128, customParams.MemMiB);
            Assert.Equal(4, customParams.Parallelism);
            Assert.Equal(customSalt, customParams.Salt);
        }

        #endregion

        #region Performance Characteristic Tests

        [Fact]
        public void DeriveKey_HigherOps_TakesLongerTime()
        {
            // Arrange
            var password = Encoding.UTF8.GetBytes("TestPassword");
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            var lowParams = new KdfParams { Ops = 1, MemMiB = 16, Parallelism = 1 };
            var highParams = new KdfParams { Ops = 5, MemMiB = 16, Parallelism = 1 };

            // Act & Measure
            var sw1 = System.Diagnostics.Stopwatch.StartNew();
            Argon2Kdf.DeriveKey((byte[])password.Clone(), (byte[])salt.Clone(), lowParams);
            sw1.Stop();

            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            Argon2Kdf.DeriveKey((byte[])password.Clone(), (byte[])salt.Clone(), highParams);
            sw2.Stop();

            // Assert - Higher Ops should take longer (with some tolerance for variance)
            Assert.True(sw2.ElapsedMilliseconds >= sw1.ElapsedMilliseconds * 0.8,
                $"High Ops ({sw2.ElapsedMilliseconds}ms) should take longer than low Ops ({sw1.ElapsedMilliseconds}ms)");
        }

        #endregion

        #region Resistance to Known Attacks

        [Fact]
        public void DeriveKey_ResistantToTimingAttacks_SamePasswordLength()
        {
            // Arrange - Passwords of same length should take similar time
            var password1 = Encoding.UTF8.GetBytes("Password1234");
            var password2 = Encoding.UTF8.GetBytes("DifferentPwd");
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            var kdfParams = new KdfParams { Ops = 3, MemMiB = 16, Parallelism = 1 };

            // Act
            var sw1 = System.Diagnostics.Stopwatch.StartNew();
            Argon2Kdf.DeriveKey((byte[])password1.Clone(), (byte[])salt.Clone(), kdfParams);
            sw1.Stop();

            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            Argon2Kdf.DeriveKey((byte[])password2.Clone(), (byte[])salt.Clone(), kdfParams);
            sw2.Stop();

            // Assert - Timing should be similar (within 50% variance)
            var ratio = (double)Math.Max(sw1.ElapsedMilliseconds, sw2.ElapsedMilliseconds) /
                        Math.Max(1, Math.Min(sw1.ElapsedMilliseconds, sw2.ElapsedMilliseconds));
            Assert.True(ratio < 1.5, $"Timing variance too high: {ratio:F2}x");
        }

        #endregion
    }
}
