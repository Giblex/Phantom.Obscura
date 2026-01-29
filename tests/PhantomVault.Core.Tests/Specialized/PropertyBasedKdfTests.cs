using System;
using System.Linq;
using System.Security.Cryptography;
using FsCheck;
using FsCheck.Xunit;
using PhantomVault.Core.Services;

namespace PhantomVault.Core.Tests.Specialized
{
    /// <summary>
    /// Property-based testing for KDF parameters using FsCheck.
    /// Tests invariants, parameter sensitivity, and security properties across random inputs.
    /// </summary>
    public class PropertyBasedKdfTests
    {
        #region Argon2 KDF Parameter Properties

        [Property(MaxTest = 100)]
        public Property Argon2_DifferentPasswords_ProduceDifferentKeys(string password1, string password2)
        {
            // Arrange
            if (string.IsNullOrEmpty(password1) || string.IsNullOrEmpty(password2) || password1 == password2)
                return true.ToProperty();

            var service = new EncryptionService();
            var salt = service.GenerateSalt();

            // Act
            var key1 = service.DeriveKey(password1.AsSpan(), salt);
            var key2 = service.DeriveKey(password2.AsSpan(), salt);

            // Assert
            return (!key1.SequenceEqual(key2)).ToProperty();
        }

        [Property(MaxTest = 100)]
        public Property Argon2_DifferentSalts_ProduceDifferentKeys(string password, byte[] salt1, byte[] salt2)
        {
            // Arrange
            if (string.IsNullOrEmpty(password) || salt1 == null || salt2 == null ||
                salt1.Length < 8 || salt2.Length < 8 || salt1.SequenceEqual(salt2))
                return true.ToProperty();

            var service = new EncryptionService();

            // Act
            var key1 = service.DeriveKey(password.AsSpan(), salt1);
            var key2 = service.DeriveKey(password.AsSpan(), salt2);

            // Assert
            return (!key1.SequenceEqual(key2)).ToProperty();
        }

        [Property(MaxTest = 100)]
        public Property Argon2_AlwaysProduces32ByteKey(string password, byte[] salt)
        {
            // Arrange
            if (string.IsNullOrEmpty(password) || salt == null || salt.Length < 8)
                return true.ToProperty();

            var service = new EncryptionService();

            // Act
            var key = service.DeriveKey(password.AsSpan(), salt);

            // Assert
            return (key.Length == 32).ToProperty();
        }

        [Property(MaxTest = 100)]
        public Property Argon2_IsDeterministic(string password, byte[] salt)
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

        [Property(MaxTest = 50)]
        public Property Argon2_VariableMemoryCost_StillProducesValidKey(string password, byte memoryCostMultiplier)
        {
            // Arrange
            if (string.IsNullOrEmpty(password))
                return true.ToProperty();

            var service = new EncryptionService();
            var salt = service.GenerateSalt();
            var memoryCostKb = Math.Max(8 * 1024, (memoryCostMultiplier % 128 + 8) * 1024); // 8-135 MB

            // Act
            var key = service.DeriveKey(password.AsSpan(), salt, memoryCostKb: memoryCostKb);

            // Assert
            return (key != null && key.Length == 32).ToProperty();
        }

        [Property(MaxTest = 50)]
        public Property Argon2_VariableIterations_StillProducesValidKey(string password, byte iterations)
        {
            // Arrange
            if (string.IsNullOrEmpty(password))
                return true.ToProperty();

            var service = new EncryptionService();
            var salt = service.GenerateSalt();
            var validIterations = Math.Max(1, iterations % 10 + 1); // 1-10

            // Act
            var key = service.DeriveKey(password.AsSpan(), salt, iterations: validIterations);

            // Assert
            return (key != null && key.Length == 32).ToProperty();
        }

        [Property(MaxTest = 50)]
        public Property Argon2_VariableParallelism_StillProducesValidKey(string password, byte parallelism)
        {
            // Arrange
            if (string.IsNullOrEmpty(password))
                return true.ToProperty();

            var service = new EncryptionService();
            var salt = service.GenerateSalt();
            var validParallelism = Math.Max(1, parallelism % 8 + 1); // 1-8

            // Act
            var key = service.DeriveKey(password.AsSpan(), salt, parallelism: validParallelism);

            // Assert
            return (key != null && key.Length == 32).ToProperty();
        }

        #endregion

        #region Output Distribution Properties

        [Property(MaxTest = 50)]
        public Property Argon2_OutputHasHighEntropy(string password)
        {
            // Arrange
            if (string.IsNullOrEmpty(password))
                return true.ToProperty();

            var service = new EncryptionService();
            var salt = service.GenerateSalt();

            // Act
            var key = service.DeriveKey(password.AsSpan(), salt);

            // Assert - Key should have at least 20 distinct byte values
            return (key.Distinct().Count() >= 20).ToProperty();
        }

        [Property(MaxTest = 50)]
        public Property Argon2_OutputNotAllZeros(string password, byte[] salt)
        {
            // Arrange
            if (string.IsNullOrEmpty(password) || salt == null || salt.Length < 8)
                return true.ToProperty();

            var service = new EncryptionService();

            // Act
            var key = service.DeriveKey(password.AsSpan(), salt);

            // Assert - Key should not be all zeros
            return key.Any(b => b != 0).ToProperty();
        }

        [Property(MaxTest = 50)]
        public Property Argon2_OutputNotAllSameValue(string password, byte[] salt)
        {
            // Arrange
            if (string.IsNullOrEmpty(password) || salt == null || salt.Length < 8)
                return true.ToProperty();

            var service = new EncryptionService();

            // Act
            var key = service.DeriveKey(password.AsSpan(), salt);

            // Assert - Key should have variety (not all same byte)
            return (key.Distinct().Count() > 1).ToProperty();
        }

        #endregion

        #region Avalanche Effect Properties

        [Property(MaxTest = 30)]
        public Property Argon2_SingleBitChange_CausesSignificantDifference(string basePassword)
        {
            // Arrange
            if (string.IsNullOrEmpty(basePassword))
                return true.ToProperty();

            var service = new EncryptionService();
            var salt = service.GenerateSalt();

            // Create password with single bit flipped in last character
            var password2 = basePassword.Substring(0, Math.Max(0, basePassword.Length - 1)) +
                           (char)(basePassword[basePassword.Length - 1] ^ 1);

            // Act
            var key1 = service.DeriveKey(basePassword.AsSpan(), salt);
            var key2 = service.DeriveKey(password2.AsSpan(), salt);

            // Assert - Count differing bits
            int differingBits = 0;
            for (int i = 0; i < key1.Length; i++)
            {
                byte xor = (byte)(key1[i] ^ key2[i]);
                differingBits += System.Numerics.BitOperations.PopCount(xor);
            }

            int totalBits = key1.Length * 8;
            double changeRatio = (double)differingBits / totalBits;

            // At least 20% of bits should change (avalanche effect)
            return (changeRatio >= 0.20).ToProperty();
        }

        #endregion

        #region Parameter Sensitivity Properties

        [Property(MaxTest = 30)]
        public Property Argon2_MemoryCostSensitivity_ProducesDifferentKeys(string password)
        {
            // Arrange
            if (string.IsNullOrEmpty(password))
                return true.ToProperty();

            var service = new EncryptionService();
            var salt = service.GenerateSalt();

            // Act - Different memory costs
            var key1 = service.DeriveKey(password.AsSpan(), salt, memoryCostKb: 16 * 1024);
            var key2 = service.DeriveKey(password.AsSpan(), salt, memoryCostKb: 64 * 1024);

            // Assert
            return (!key1.SequenceEqual(key2)).ToProperty();
        }

        [Property(MaxTest = 30)]
        public Property Argon2_IterationsSensitivity_ProducesDifferentKeys(string password)
        {
            // Arrange
            if (string.IsNullOrEmpty(password))
                return true.ToProperty();

            var service = new EncryptionService();
            var salt = service.GenerateSalt();

            // Act - Different iterations
            var key1 = service.DeriveKey(password.AsSpan(), salt, iterations: 2);
            var key2 = service.DeriveKey(password.AsSpan(), salt, iterations: 5);

            // Assert
            return (!key1.SequenceEqual(key2)).ToProperty();
        }

        [Property(MaxTest = 30)]
        public Property Argon2_ParallelismSensitivity_ProducesDifferentKeys(string password)
        {
            // Arrange
            if (string.IsNullOrEmpty(password))
                return true.ToProperty();

            var service = new EncryptionService();
            var salt = service.GenerateSalt();

            // Act - Different parallelism
            var key1 = service.DeriveKey(password.AsSpan(), salt, parallelism: 1);
            var key2 = service.DeriveKey(password.AsSpan(), salt, parallelism: 4);

            // Assert
            return (!key1.SequenceEqual(key2)).ToProperty();
        }

        #endregion

        #region Edge Case Properties

        [Property(MaxTest = 50)]
        public Property Argon2_VeryLongPassword_StillWorks(byte passwordLength)
        {
            // Arrange - Passwords up to 256 characters
            var length = Math.Max(1, passwordLength % 256 + 1);
            var password = new string('A', length);

            var service = new EncryptionService();
            var salt = service.GenerateSalt();

            // Act
            var key = service.DeriveKey(password.AsSpan(), salt);

            // Assert
            return (key != null && key.Length == 32).ToProperty();
        }

        [Property(MaxTest = 50)]
        public Property Argon2_VariableSaltLength_StillWorks(byte saltLength)
        {
            // Arrange - Salts from 8 to 64 bytes
            var length = Math.Max(8, saltLength % 64 + 8);
            var salt = new byte[length];
            RandomNumberGenerator.Fill(salt);

            var service = new EncryptionService();

            // Act
            var key = service.DeriveKey("password".AsSpan(), salt);

            // Assert
            return (key != null && key.Length == 32).ToProperty();
        }

        #endregion

        #region PBKDF2 Fallback Properties

        [Property(MaxTest = 50)]
        public Property Pbkdf2Fallback_IsDeterministic(string password, byte[] salt)
        {
            // Arrange
            if (string.IsNullOrEmpty(password) || salt == null || salt.Length < 8)
                return true.ToProperty();

            var service = new EncryptionService(forceArgon2FailureForTests: true);

            // Act
            var key1 = service.DeriveKey(password.AsSpan(), salt);
            var key2 = service.DeriveKey(password.AsSpan(), salt);

            // Assert
            return key1.SequenceEqual(key2).ToProperty();
        }

        [Property(MaxTest = 50)]
        public Property Pbkdf2Fallback_ProducesValidKey(string password)
        {
            // Arrange
            if (string.IsNullOrEmpty(password))
                return true.ToProperty();

            var service = new EncryptionService(forceArgon2FailureForTests: true);
            var salt = service.GenerateSalt();

            // Act
            var key = service.DeriveKey(password.AsSpan(), salt);

            // Assert
            return (key != null && key.Length == 32).ToProperty();
        }

        #endregion

        #region Security Invariants

        [Property(MaxTest = 50)]
        public Property Argon2_OutputDoesNotContainPasswordFragments(string password)
        {
            // Arrange
            if (string.IsNullOrEmpty(password) || password.Length < 4)
                return true.ToProperty();

            var service = new EncryptionService();
            var salt = service.GenerateSalt();

            // Act
            var key = service.DeriveKey(password.AsSpan(), salt);
            var keyHex = Convert.ToHexString(key);
            var passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
            var passwordHex = Convert.ToHexString(passwordBytes);

            // Assert - Key should not contain obvious password fragments (first 8 hex chars)
            if (passwordHex.Length >= 8)
            {
                return (!keyHex.Contains(passwordHex.Substring(0, 8))).ToProperty();
            }

            return true.ToProperty();
        }

        [Property(MaxTest = 50)]
        public Property Argon2_OutputDoesNotContainSaltFragments(string password, byte[] salt)
        {
            // Arrange
            if (string.IsNullOrEmpty(password) || salt == null || salt.Length < 8)
                return true.ToProperty();

            var service = new EncryptionService();

            // Act
            var key = service.DeriveKey(password.AsSpan(), salt);
            var keyHex = Convert.ToHexString(key);
            var saltHex = Convert.ToHexString(salt);

            // Assert - Key should not contain obvious salt fragments
            if (saltHex.Length >= 8)
            {
                return (!keyHex.Contains(saltHex.Substring(0, 8))).ToProperty();
            }

            return true.ToProperty();
        }

        #endregion

        #region Integration Properties

        [Property(MaxTest = 30)]
        public Property KdfKey_CanBeUsedForEncryption(string password, byte[] plaintext)
        {
            // Arrange
            if (string.IsNullOrEmpty(password) || plaintext == null)
                return true.ToProperty();

            var service = new EncryptionService();
            var salt = service.GenerateSalt();

            // Act
            var key = service.DeriveKey(password.AsSpan(), salt);
            var encrypted = service.Encrypt(plaintext, key);
            var decrypted = service.Decrypt(encrypted.Ciphertext, encrypted.Nonce, encrypted.Tag, key);

            // Assert
            return plaintext.SequenceEqual(decrypted).ToProperty();
        }

        #endregion
    }
}
