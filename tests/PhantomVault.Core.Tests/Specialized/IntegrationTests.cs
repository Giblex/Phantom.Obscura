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
    /// Integration tests covering:
    /// - Memory zeroization verification across all operations
    /// - Backup/restore round-trip workflows
    /// - Import/export format compatibility
    /// - End-to-end encryption workflows
    /// </summary>
    public class IntegrationTests
    {
        #region Memory Zeroization Integration Tests

        private class ComprehensiveZeroizationObserver : IEncryptionObserver
        {
            public int PasswordZeroizedCount { get; private set; }
            public int TransientZeroizedCount { get; private set; }
            public bool AllPasswordBuffersZeroed { get; private set; } = true;
            public bool AllTransientBuffersZeroed { get; private set; } = true;

            public void OnPasswordBufferZeroized(byte[] buffer)
            {
                PasswordZeroizedCount++;
                if (buffer.Any(b => b != 0))
                {
                    AllPasswordBuffersZeroed = false;
                }
            }

            public void OnTransientBufferZeroized(byte[] buffer)
            {
                TransientZeroizedCount++;
                if (buffer.Any(b => b != 0))
                {
                    AllTransientBuffersZeroed = false;
                }
            }
        }

        [Fact]
        public void FullEncryptionWorkflow_ZeroizesAllSensitiveData()
        {
            // Arrange
            var observer = new ComprehensiveZeroizationObserver();
            var service = new EncryptionService(observer);
            var password = "UserPassword123!";
            var plaintext = Encoding.UTF8.GetBytes("Sensitive document content");

            // Act - Complete workflow
            var salt = service.GenerateSalt();
            var key = service.DeriveKey(password.AsSpan(), salt);
            var encrypted = service.Encrypt(plaintext, key);
            var decrypted = service.Decrypt(encrypted.Ciphertext, encrypted.Nonce, encrypted.Tag, key);

            // Assert
            Assert.Equal(plaintext, decrypted);
            Assert.True(observer.PasswordZeroizedCount > 0, "Password buffer should be zeroed");
            Assert.True(observer.TransientZeroizedCount > 0, "Transient buffers should be zeroed");
            Assert.True(observer.AllPasswordBuffersZeroed, "All password buffers should be fully zeroed");
            Assert.True(observer.AllTransientBuffersZeroed, "All transient buffers should be fully zeroed");
        }

        [Fact]
        public void MultipleOperations_ZeroizeEachTime()
        {
            // Arrange
            var observer = new ComprehensiveZeroizationObserver();
            var service = new EncryptionService(observer);
            var password1 = "Password1";
            var password2 = "Password2";
            var password3 = "Password3";

            // Act - Multiple key derivations
            var salt = service.GenerateSalt();
            service.DeriveKey(password1.AsSpan(), salt);
            service.DeriveKey(password2.AsSpan(), salt);
            service.DeriveKey(password3.AsSpan(), salt);

            // Assert - Each operation should trigger zeroization
            Assert.Equal(3, observer.PasswordZeroizedCount);
            Assert.True(observer.AllPasswordBuffersZeroed);
        }

        [Fact]
        public void EncryptionFailure_StillZeroizesBuffers()
        {
            // Arrange
            var observer = new ComprehensiveZeroizationObserver();
            var service = new EncryptionService(observer);
            var key = new byte[32];
            var wrongKey = new byte[32];
            RandomNumberGenerator.Fill(key);
            RandomNumberGenerator.Fill(wrongKey);
            var plaintext = Encoding.UTF8.GetBytes("Test data");

            // Act - Encrypt successfully, then fail decryption
            var encrypted = service.Encrypt(plaintext, key);

            try
            {
                service.Decrypt(encrypted.Ciphertext, encrypted.Nonce, encrypted.Tag, wrongKey);
            }
            catch (CryptographicException)
            {
                // Expected
            }

            // Assert - Buffers should still be zeroed despite failure
            Assert.True(observer.TransientZeroizedCount > 0);
            Assert.True(observer.AllTransientBuffersZeroed);
        }

        #endregion

        #region Backup/Restore Round-Trip Tests

        [Fact]
        public void BackupRestore_EncryptedData_RoundTrip()
        {
            // Arrange - Simulate backing up encrypted data
            var service = new EncryptionService();
            var password = "BackupPassword123!";
            var salt = service.GenerateSalt();
            var key = service.DeriveKey(password.AsSpan(), salt);
            var plaintext = Encoding.UTF8.GetBytes("Important data to backup");

            // Act - Encrypt (backup)
            var encrypted = service.Encrypt(plaintext, key);

            // Simulate saving to backup (serialize components)
            var backupCiphertext = (byte[])encrypted.Ciphertext.Clone();
            var backupNonce = (byte[])encrypted.Nonce.Clone();
            var backupTag = (byte[])encrypted.Tag.Clone();
            var backupSalt = (byte[])salt.Clone();

            // Simulate restore from backup
            var restoredKey = service.DeriveKey(password.AsSpan(), backupSalt);
            var restored = service.Decrypt(backupCiphertext, backupNonce, backupTag, restoredKey);

            // Assert
            Assert.Equal(plaintext, restored);
        }

        [Fact]
        public void BackupRestore_LayeredEncryption_RoundTrip()
        {
            // Arrange
            var service = new LayeredEncryptionService(new EncryptionService());
            var encryptionService = new EncryptionService();
            var password = "LayeredBackup456!";
            var salt = encryptionService.GenerateSalt();
            var masterKey = encryptionService.DeriveKey(password.AsSpan(), salt);
            var plaintext = Encoding.UTF8.GetBytes("Multi-layer backup data");

            // Act - Encrypt with maximum security for backup
            var encrypted = service.EncryptLayered(plaintext, masterKey, SecurityLevel.Maximum, salt);

            // Simulate backup (serialize all components)
            var backup = new
            {
                Data = (byte[])encrypted.Ciphertext.Clone(),
                Nonce1 = (byte[])encrypted.Nonce1.Clone(),
                Tag1 = (byte[])encrypted.Tag1.Clone(),
                Nonce2 = (byte[])encrypted.Nonce2.Clone(),
                Tag2 = (byte[])encrypted.Tag2.Clone(),
                // Nonce3 removed - Layer 3 only has IV3
                IV3 = (byte[])encrypted.IV3.Clone(),
                // Nonce4 removed - Layer 4 only has IV4
                IV4 = (byte[])encrypted.IV4.Clone(),
                // Nonce5 removed - Layer 5 only has IV5
                IV5 = (byte[])encrypted.IV5.Clone(),
                Salt = (byte[])salt.Clone()
            };

            // Simulate restore
            var restoredKey = encryptionService.DeriveKey(password.AsSpan(), backup.Salt);
            var restoredResult = new LayeredEncryptionResult
            {
                Ciphertext = backup.Data,
                Nonce1 = backup.Nonce1,
                Tag1 = backup.Tag1,
                Nonce2 = backup.Nonce2,
                Tag2 = backup.Tag2,
                // Nonce3 removed - Layer 3 only has IV3
                IV3 = backup.IV3,
                // Nonce4 removed - Layer 4 only has IV4
                IV4 = backup.IV4,
                // Nonce5 removed - Layer 5 only has IV5
                IV5 = backup.IV5,
                Level = SecurityLevel.Maximum
            };

            var restored = service.DecryptLayered(restoredResult, restoredKey, backup.Salt);

            // Assert
            Assert.Equal(plaintext, restored);
        }

        [Fact]
        public void BackupRestore_WithContextData_RoundTrip()
        {
            // Arrange
            var service = new EncryptionService();
            var password = "ContextBackup789!";
            var salt = service.GenerateSalt();
            var key = service.DeriveKey(password.AsSpan(), salt);
            var plaintext = Encoding.UTF8.GetBytes("Document with context");
            var contextData = Encoding.UTF8.GetBytes("document.pdf");

            // Act - Encrypt with context
            var encrypted = service.Encrypt(plaintext, key, contextData);

            // Backup
            var backup = new
            {
                Ciphertext = (byte[])encrypted.Ciphertext.Clone(),
                Nonce = (byte[])encrypted.Nonce.Clone(),
                Tag = (byte[])encrypted.Tag.Clone(),
                Context = (byte[])contextData.Clone(),
                Salt = (byte[])salt.Clone()
            };

            // Restore
            var restoredKey = service.DeriveKey(password.AsSpan(), backup.Salt);
            var restored = service.Decrypt(backup.Ciphertext, backup.Nonce, backup.Tag, restoredKey, backup.Context);

            // Assert
            Assert.Equal(plaintext, restored);
        }

        [Fact]
        public void BackupRestore_IncorrectPassword_Fails()
        {
            // Arrange
            var service = new EncryptionService();
            var password = "CorrectPassword";
            var wrongPassword = "WrongPassword";
            var salt = service.GenerateSalt();
            var key = service.DeriveKey(password.AsSpan(), salt);
            var plaintext = Encoding.UTF8.GetBytes("Protected data");

            var encrypted = service.Encrypt(plaintext, key);

            // Act & Assert - Restore with wrong password should fail
            var wrongKey = service.DeriveKey(wrongPassword.AsSpan(), salt);
            Assert.Throws<CryptographicException>(() =>
                service.Decrypt(encrypted.Ciphertext, encrypted.Nonce, encrypted.Tag, wrongKey));
        }

        #endregion

        #region Import/Export Format Compatibility Tests

        [Fact]
        public void ExportImport_StandardFormat_Compatible()
        {
            // Arrange - Simulate exporting to standard format
            var service = new EncryptionService();
            var password = "ExportTest";
            var salt = service.GenerateSalt();
            var key = service.DeriveKey(password.AsSpan(), salt);
            var plaintext = Encoding.UTF8.GetBytes("Data for export");

            var encrypted = service.Encrypt(plaintext, key);

            // Export format: Salt (16) | Nonce (12) | Tag (16) | Ciphertext (N)
            var exportData = new byte[salt.Length + encrypted.Nonce.Length + encrypted.Tag.Length + encrypted.Ciphertext.Length];
            var offset = 0;

            Array.Copy(salt, 0, exportData, offset, salt.Length);
            offset += salt.Length;

            Array.Copy(encrypted.Nonce, 0, exportData, offset, encrypted.Nonce.Length);
            offset += encrypted.Nonce.Length;

            Array.Copy(encrypted.Tag, 0, exportData, offset, encrypted.Tag.Length);
            offset += encrypted.Tag.Length;

            Array.Copy(encrypted.Ciphertext, 0, exportData, offset, encrypted.Ciphertext.Length);

            // Act - Import
            offset = 0;
            var importedSalt = new byte[16];
            Array.Copy(exportData, offset, importedSalt, 0, 16);
            offset += 16;

            var importedNonce = new byte[12];
            Array.Copy(exportData, offset, importedNonce, 0, 12);
            offset += 12;

            var importedTag = new byte[16];
            Array.Copy(exportData, offset, importedTag, 0, 16);
            offset += 16;

            var importedCiphertext = new byte[exportData.Length - offset];
            Array.Copy(exportData, offset, importedCiphertext, 0, importedCiphertext.Length);

            var importedKey = service.DeriveKey(password.AsSpan(), importedSalt);
            var decrypted = service.Decrypt(importedCiphertext, importedNonce, importedTag, importedKey);

            // Assert
            Assert.Equal(plaintext, decrypted);
        }

        [Fact]
        public void ExportImport_LayeredFormat_Compatible()
        {
            // Arrange
            var encryptionService = new EncryptionService();
            var layeredService = new LayeredEncryptionService(encryptionService);
            var password = "LayeredExport";
            var salt = encryptionService.GenerateSalt();
            var masterKey = encryptionService.DeriveKey(password.AsSpan(), salt);
            var plaintext = Encoding.UTF8.GetBytes("Layered export data");

            var encrypted = layeredService.EncryptLayered(plaintext, masterKey, SecurityLevel.Sensitive, salt);

            // Export format simulation (just verify round-trip through structure)
            var exported = new
            {
                Version = 1,
                Level = (int)encrypted.Level,
                Salt = Convert.ToBase64String(salt),
                Nonce1 = Convert.ToBase64String(encrypted.Nonce1),
                Tag1 = Convert.ToBase64String(encrypted.Tag1),
                Nonce2 = Convert.ToBase64String(encrypted.Nonce2),
                Tag2 = Convert.ToBase64String(encrypted.Tag2),
                // Nonce3 removed - Layer 3 only has IV3
                IV3 = Convert.ToBase64String(encrypted.IV3),
                Data = Convert.ToBase64String(encrypted.Ciphertext)
            };

            // Act - Import
            var importedSalt = Convert.FromBase64String(exported.Salt);
            var importedKey = encryptionService.DeriveKey(password.AsSpan(), importedSalt);
            var importedResult = new LayeredEncryptionResult
            {
                Ciphertext = Convert.FromBase64String(exported.Data),
                Nonce1 = Convert.FromBase64String(exported.Nonce1),
                Tag1 = Convert.FromBase64String(exported.Tag1),
                Nonce2 = Convert.FromBase64String(exported.Nonce2),
                Tag2 = Convert.FromBase64String(exported.Tag2),
                // Nonce3 removed - Layer 3 only has IV3
                IV3 = Convert.FromBase64String(exported.IV3),
                Level = (SecurityLevel)exported.Level
            };

            var decrypted = layeredService.DecryptLayered(importedResult, importedKey, importedSalt);

            // Assert
            Assert.Equal(plaintext, decrypted);
        }

        [Fact]
        public void ImportExport_Base64Encoding_PreservesData()
        {
            // Arrange
            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var plaintext = new byte[1000];
            RandomNumberGenerator.Fill(plaintext);

            var encrypted = service.Encrypt(plaintext, key);

            // Act - Export to Base64
            var exportedCiphertext = Convert.ToBase64String(encrypted.Ciphertext);
            var exportedNonce = Convert.ToBase64String(encrypted.Nonce);
            var exportedTag = Convert.ToBase64String(encrypted.Tag);

            // Import from Base64
            var importedCiphertext = Convert.FromBase64String(exportedCiphertext);
            var importedNonce = Convert.FromBase64String(exportedNonce);
            var importedTag = Convert.FromBase64String(exportedTag);

            var decrypted = service.Decrypt(importedCiphertext, importedNonce, importedTag, key);

            // Assert
            Assert.Equal(plaintext, decrypted);
        }

        #endregion

        #region End-to-End Workflow Tests

        [Fact]
        public void EndToEnd_VaultCreation_Complete()
        {
            // Arrange - Simulate complete vault creation workflow
            var encryptionService = new EncryptionService();
            var layeredService = new LayeredEncryptionService(encryptionService);

            var userPassword = "MyVaultPassword123!";
            var vaultData = Encoding.UTF8.GetBytes("Secret vault contents");

            // Act - Complete workflow
            // Step 1: Generate salt
            var salt = encryptionService.GenerateSalt();

            // Step 2: Derive master key from password
            var masterKey = encryptionService.DeriveKey(userPassword.AsSpan(), salt);

            // Step 3: Encrypt data with layered encryption
            var encrypted = layeredService.EncryptLayered(vaultData, masterKey, SecurityLevel.Maximum, salt);

            // Step 4: Simulate storage (would write to disk in real scenario)
            var stored = new
            {
                Salt = salt,
                Encrypted = encrypted
            };

            // Step 5: Simulate retrieval and decryption
            var retrievedMasterKey = encryptionService.DeriveKey(userPassword.AsSpan(), stored.Salt);
            var decrypted = layeredService.DecryptLayered(stored.Encrypted, retrievedMasterKey, stored.Salt);

            // Assert
            Assert.Equal(vaultData, decrypted);
        }

        [Fact]
        public void EndToEnd_MultipleFiles_IndependentEncryption()
        {
            // Arrange
            var encryptionService = new EncryptionService();
            var password = "FileVaultPassword";
            var salt = encryptionService.GenerateSalt();
            var masterKey = encryptionService.DeriveKey(password.AsSpan(), salt);

            var files = new[]
            {
                new { Name = "document1.txt", Content = Encoding.UTF8.GetBytes("Content of document 1") },
                new { Name = "document2.txt", Content = Encoding.UTF8.GetBytes("Content of document 2") },
                new { Name = "document3.txt", Content = Encoding.UTF8.GetBytes("Content of document 3") }
            };

            // Act - Encrypt each file with its filename as context
            var encryptedFiles = files.Select(f => new
            {
                f.Name,
                Context = Encoding.UTF8.GetBytes(f.Name),
                Encrypted = encryptionService.Encrypt(f.Content, masterKey, Encoding.UTF8.GetBytes(f.Name))
            }).ToArray();

            // Decrypt each file
            var decryptedFiles = encryptedFiles.Select(f =>
                encryptionService.Decrypt(f.Encrypted.Ciphertext, f.Encrypted.Nonce, f.Encrypted.Tag, masterKey, f.Context)
            ).ToArray();

            // Assert
            for (int i = 0; i < files.Length; i++)
            {
                Assert.Equal(files[i].Content, decryptedFiles[i]);
            }
        }

        [Fact]
        public void EndToEnd_PasswordChange_Reencryption()
        {
            // Arrange - Simulate password change workflow
            var encryptionService = new EncryptionService();
            var oldPassword = "OldPassword123";
            var newPassword = "NewPassword456";

            var salt = encryptionService.GenerateSalt();
            var oldKey = encryptionService.DeriveKey(oldPassword.AsSpan(), salt);
            var data = Encoding.UTF8.GetBytes("Data to re-encrypt");

            // Encrypt with old password
            var encrypted = encryptionService.Encrypt(data, oldKey);

            // Act - Password change workflow
            // 1. Decrypt with old password
            var decrypted = encryptionService.Decrypt(encrypted.Ciphertext, encrypted.Nonce, encrypted.Tag, oldKey);

            // 2. Generate new salt and key
            var newSalt = encryptionService.GenerateSalt();
            var newKey = encryptionService.DeriveKey(newPassword.AsSpan(), newSalt);

            // 3. Re-encrypt with new password
            var reencrypted = encryptionService.Encrypt(decrypted, newKey);

            // 4. Verify decryption with new password
            var finalDecrypted = encryptionService.Decrypt(reencrypted.Ciphertext, reencrypted.Nonce, reencrypted.Tag, newKey);

            // Assert
            Assert.Equal(data, finalDecrypted);

            // Old password should no longer work
            Assert.Throws<CryptographicException>(() =>
                encryptionService.Decrypt(reencrypted.Ciphertext, reencrypted.Nonce, reencrypted.Tag, oldKey));
        }

        #endregion

        #region Cross-Platform Compatibility Tests

        [Fact]
        public void CrossPlatform_EncryptionFormat_Compatible()
        {
            // Arrange - Test that encryption format is platform-independent
            var service = new EncryptionService();
            var key = new byte[32];
            var plaintext = Encoding.UTF8.GetBytes("Cross-platform data");

            // Use deterministic key for cross-platform testing
            using (var sha = SHA256.Create())
            {
                var keyMaterial = Encoding.UTF8.GetBytes("DeterministicKeyMaterial");
                Array.Copy(sha.ComputeHash(keyMaterial), key, 32);
            }

            // Act
            var encrypted = service.Encrypt(plaintext, key);
            var decrypted = service.Decrypt(encrypted.Ciphertext, encrypted.Nonce, encrypted.Tag, key);

            // Assert
            Assert.Equal(plaintext, decrypted);
        }

        #endregion

        #region Performance Integration Tests

        [Fact]
        public void Performance_LargeFileEncryption_CompletesSuccessfully()
        {
            // Arrange - 50 MB file
            var service = new EncryptionService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var largeFile = new byte[50 * 1024 * 1024];
            RandomNumberGenerator.Fill(largeFile);

            // Act
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var encrypted = service.Encrypt(largeFile, key);
            sw.Stop();
            var encryptTime = sw.ElapsedMilliseconds;

            sw.Restart();
            var decrypted = service.Decrypt(encrypted.Ciphertext, encrypted.Nonce, encrypted.Tag, key);
            sw.Stop();
            var decryptTime = sw.ElapsedMilliseconds;

            // Assert
            Assert.Equal(largeFile, decrypted);
            // Performance expectation: Should complete in reasonable time (< 5 seconds each)
            Assert.True(encryptTime < 5000, $"Encryption took {encryptTime}ms");
            Assert.True(decryptTime < 5000, $"Decryption took {decryptTime}ms");
        }

        #endregion
    }
}
