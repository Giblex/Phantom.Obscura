using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.ZeroKnowledge;
using PhantomVault.Core.Utils;
using Xunit;

namespace PhantomVault.Core.Tests
{
    /// <summary>
    /// Integration tests for full vault lifecycle including:
    /// - Phase 2 vault creation with hybrid encryption
    /// - Vault unlock with hybrid DEK derivation
    /// - Data read/write operations
    /// - Lock/unlock cycles
    /// - Backward compatibility with Phase 1 vaults
    /// </summary>
    public class VaultLifecycleIntegrationTests : IDisposable
    {
        private readonly string _testDirectory;
        private readonly EncryptionService _encryptionService;
        private readonly ManifestService _manifestService;
        private readonly IZkVaultService _zkVaultService;

        public VaultLifecycleIntegrationTests()
        {
            // Create a temporary directory for test files
            _testDirectory = Path.Combine(Path.GetTempPath(), "PhantomVaultTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDirectory);

            // Initialize services
            _encryptionService = new EncryptionService();
            _manifestService = new ManifestService(_encryptionService);
            _zkVaultService = new ZkVaultService();
        }

        public void Dispose()
        {
            // Clean up test directory
            try
            {
                if (Directory.Exists(_testDirectory))
                {
                    Directory.Delete(_testDirectory, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        #region Phase 2 Vault Lifecycle Tests

        [Fact]
        public async Task Phase2_FullVaultLifecycle_CreatesUnlocksAndVerifiesData()
        {
            // Arrange
            string password = "TestPassword123!";
            string manifestPath = Path.Combine(_testDirectory, "vault.manifest");
            string vaultFilePath = Path.Combine(_testDirectory, "vault.zkf");
            string testData = "This is sensitive test data for Phase 2 vault";
            byte[] testDataBytes = Encoding.UTF8.GetBytes(testData);

            // Generate KEM keypair for Phase 2
            byte[] kemPublicKey = new byte[32];
            byte[] kemCiphertext = new byte[32];
            System.Security.Cryptography.RandomNumberGenerator.Fill(kemPublicKey);
            System.Security.Cryptography.RandomNumberGenerator.Fill(kemCiphertext);

            // Phase 2 manifest with ML-KEM support
            var manifest = new VaultManifest
            {
                ContainerPath = "test_container.vc",
                ContainerSizeBytes = 10 * 1024 * 1024, // 10 MB
                Algorithm = "AES-256-GCM",
                KemPublicKeyBase64 = Convert.ToBase64String(kemPublicKey), // Phase 2 indicator
                KemCiphertextBase64 = Convert.ToBase64String(kemCiphertext),
                CreatedUtc = DateTime.UtcNow
            };

            // Act - Phase 1: Create manifest
            _manifestService.WriteManifest(manifest, manifestPath, password);
            Assert.True(File.Exists(manifestPath), "Manifest file should be created");

            // Act - Phase 2: Read and decrypt manifest
            var loadedManifest = _manifestService.ReadManifest(manifestPath, password);
            Assert.NotNull(loadedManifest);
            Assert.NotNull(loadedManifest.KemPublicKeyBase64); // Phase 2 has KEM fields
            Assert.NotNull(loadedManifest.KemCiphertextBase64);
            Assert.NotNull(loadedManifest.SaltBase64);

            // Act - Phase 3: Derive hybrid DEK (KEK ⊕ KEM shared secret)
            byte[] salt = Convert.FromBase64String(loadedManifest.SaltBase64);
            byte[] kek = _encryptionService.DeriveKey(password.AsSpan(), salt);
            
            // Simulate KEM decapsulation (in real implementation, this uses ML-KEM)
            byte[] kemSharedSecret = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            
            byte[] hybridDek = HybridKeyDerivation.DeriveHybridKey(kek, kemSharedSecret);
            Assert.NotNull(hybridDek);
            Assert.Equal(32, hybridDek.Length);

            // Act - Phase 4: Unlock ZK vault service with hybrid DEK
            bool unlocked = await _zkVaultService.UnlockWithHybridKeyAsync(hybridDek);
            Assert.True(unlocked, "ZK vault should be unlocked with hybrid DEK");
            Assert.True(_zkVaultService.IsUnlocked, "ZK vault service should report as unlocked");

            // Act - Phase 5: Encrypt and write test data
            using (var plaintextStream = new MemoryStream(testDataBytes))
            {
                await _zkVaultService.EncryptStreamAsync(plaintextStream, vaultFilePath);
            }
            Assert.True(File.Exists(vaultFilePath), "Encrypted vault file should be created");

            // Act - Phase 6: Read and decrypt data
            using (var decryptedStream = await _zkVaultService.OpenFileStreamForViewingAsync(vaultFilePath))
            {
                using (var reader = new StreamReader(decryptedStream))
                {
                    string decryptedData = await reader.ReadToEndAsync();
                    Assert.Equal(testData, decryptedData);
                }
            }

            // Act - Phase 7: Lock vault and verify locked state
            await _zkVaultService.LockAndWipeKeysAsync();
            Assert.False(_zkVaultService.IsUnlocked, "ZK vault should be locked");

            // Act - Phase 8: Unlock again with same credentials
            byte[] kek2 = _encryptionService.DeriveKey(password.AsSpan(), salt);
            byte[] hybridDek2 = HybridKeyDerivation.DeriveHybridKey(kek2, kemSharedSecret);
            bool reUnlocked = await _zkVaultService.UnlockWithHybridKeyAsync(hybridDek2);
            Assert.True(reUnlocked, "ZK vault should unlock again with correct credentials");

            // Act - Phase 9: Verify data integrity after lock/unlock cycle
            using (var decryptedStream = await _zkVaultService.OpenFileStreamForViewingAsync(vaultFilePath))
            {
                using (var reader = new StreamReader(decryptedStream))
                {
                    string decryptedData = await reader.ReadToEndAsync();
                    Assert.Equal(testData, decryptedData);
                }
            }

            // Cleanup
            HybridKeyDerivation.ZeroMemory(kek, kemSharedSecret, hybridDek, kek2, hybridDek2);
            await _zkVaultService.LockAndWipeKeysAsync();
        }

        [Fact]
        public async Task Phase2_ManifestFields_ContainsAllRequiredPhase2Data()
        {
            // Arrange
            string password = "SecurePassword456!";
            string manifestPath = Path.Combine(_testDirectory, "phase2_manifest.json");
            
            byte[] kemPublicKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            byte[] kemCiphertext = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

            var manifest = new VaultManifest
            {
                ContainerPath = "phase2_vault.vc",
                ContainerSizeBytes = 50 * 1024 * 1024,
                Algorithm = "AES-256-GCM",
                KemPublicKeyBase64 = Convert.ToBase64String(kemPublicKey),
                KemCiphertextBase64 = Convert.ToBase64String(kemCiphertext),
                CreatedUtc = DateTime.UtcNow
            };

            // Act
            _manifestService.WriteManifest(manifest, manifestPath, password);
            var loaded = _manifestService.ReadManifest(manifestPath, password);

            // Assert - All Phase 2 fields present
            Assert.NotNull(loaded.KemPublicKeyBase64); // Presence indicates Phase 2
            Assert.NotNull(loaded.KemCiphertextBase64);
            Assert.Equal(manifest.KemPublicKeyBase64, loaded.KemPublicKeyBase64);
            Assert.Equal(manifest.KemCiphertextBase64, loaded.KemCiphertextBase64);
        }

        [Fact]
        public async Task Phase2_HybridDekDerivation_IsDeterministic()
        {
            // Arrange
            string password = "TestPassword789!";
            byte[] salt = _encryptionService.GenerateSalt();
            byte[] kemSharedSecret = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

            // Act - Derive twice with same inputs
            byte[] kek1 = _encryptionService.DeriveKey(password.AsSpan(), salt);
            byte[] hybridDek1 = HybridKeyDerivation.DeriveHybridKey(kek1, kemSharedSecret);

            byte[] kek2 = _encryptionService.DeriveKey(password.AsSpan(), salt);
            byte[] hybridDek2 = HybridKeyDerivation.DeriveHybridKey(kek2, kemSharedSecret);

            // Assert - Results should be identical
            Assert.Equal(hybridDek1, hybridDek2);

            // Cleanup
            HybridKeyDerivation.ZeroMemory(kek1, kek2, hybridDek1, hybridDek2, kemSharedSecret);
        }

        [Fact]
        public void Phase2_WrongPassword_FailsToUnlock()
        {
            // Arrange
            string correctPassword = "CorrectPassword123!";
            string wrongPassword = "WrongPassword456!";
            string manifestPath = Path.Combine(_testDirectory, "test_manifest.json");

            var manifest = new VaultManifest
            {
                ContainerPath = "test.vc",
                ContainerSizeBytes = 10 * 1024 * 1024,
                Algorithm = "AES-256-GCM",
                KemPublicKeyBase64 = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
                CreatedUtc = DateTime.UtcNow
            };

            _manifestService.WriteManifest(manifest, manifestPath, correctPassword);

            // Act & Assert - Wrong password should fail to decrypt manifest
            // AuthenticationTagMismatchException is a subtype of CryptographicException
            Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() =>
            {
                _manifestService.ReadManifest(manifestPath, wrongPassword);
            });
        }

        [Fact]
        public async Task Phase2_MultipleDataFiles_PreservesAllContent()
        {
            // Arrange
            string password = "MultiFileTest123!";
            byte[] salt = _encryptionService.GenerateSalt();
            byte[] kemSharedSecret = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            
            byte[] kek = _encryptionService.DeriveKey(password.AsSpan(), salt);
            byte[] hybridDek = HybridKeyDerivation.DeriveHybridKey(kek, kemSharedSecret);

            await _zkVaultService.UnlockWithHybridKeyAsync(hybridDek);

            string[] testFiles = new[]
            {
                "credentials1.txt",
                "credentials2.txt",
                "secrets.json"
            };

            string[] testContents = new[]
            {
                "Username: user1\nPassword: pass1",
                "Username: user2\nPassword: pass2",
                "{\"apiKey\": \"secret123\"}"
            };

            // Act - Create multiple encrypted files
            for (int i = 0; i < testFiles.Length; i++)
            {
                string filePath = Path.Combine(_testDirectory, testFiles[i] + ".zkf");
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(testContents[i])))
                {
                    await _zkVaultService.EncryptStreamAsync(stream, filePath);
                }
            }

            // Assert - Verify all files can be decrypted correctly
            for (int i = 0; i < testFiles.Length; i++)
            {
                string filePath = Path.Combine(_testDirectory, testFiles[i] + ".zkf");
                using (var stream = await _zkVaultService.OpenFileStreamForViewingAsync(filePath))
                using (var reader = new StreamReader(stream))
                {
                    string decrypted = await reader.ReadToEndAsync();
                    Assert.Equal(testContents[i], decrypted);
                }
            }

            // Cleanup
            HybridKeyDerivation.ZeroMemory(kek, kemSharedSecret, hybridDek);
            await _zkVaultService.LockAndWipeKeysAsync();
        }

        #endregion

        #region Backward Compatibility Tests

        [Fact]
        public void Phase1_BackwardCompatibility_CanReadPhase1Manifest()
        {
            // Arrange - Create Phase 1 manifest (no KEM fields = Phase 1)
            string password = "Phase1Password!";
            string manifestPath = Path.Combine(_testDirectory, "phase1_manifest.json");

            var phase1Manifest = new VaultManifest
            {
                ContainerPath = "phase1_vault.vc",
                ContainerSizeBytes = 20 * 1024 * 1024,
                Algorithm = "AES-256-GCM",
                CreatedUtc = DateTime.UtcNow
                // No KEM fields for Phase 1
            };

            // Act
            _manifestService.WriteManifest(phase1Manifest, manifestPath, password);
            var loaded = _manifestService.ReadManifest(manifestPath, password);

            // Assert - Phase 1 has no KEM fields
            Assert.Null(loaded.KemPublicKeyBase64);
            Assert.Null(loaded.KemCiphertextBase64);
            Assert.Null(loaded.KemPrivateKeyEncryptedBase64);
        }

        [Fact]
        public async Task Phase1_TraditionalDEK_WorksWithoutHybridDerivation()
        {
            // Arrange
            string password = "TraditionalTest123!";
            byte[] salt = _encryptionService.GenerateSalt();
            string vaultFilePath = Path.Combine(_testDirectory, "phase1_vault.zkf");
            string testData = "Phase 1 vault data";

            // Act - Phase 1 uses traditional KEK directly as master key
            byte[] kek = _encryptionService.DeriveKey(password.AsSpan(), salt);
            await _zkVaultService.UnlockWithHybridKeyAsync(kek); // Can accept KEK directly

            // Write data
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(testData)))
            {
                await _zkVaultService.EncryptStreamAsync(stream, vaultFilePath);
            }

            // Lock and unlock with traditional KEK
            await _zkVaultService.LockAndWipeKeysAsync();
            
            byte[] kek2 = _encryptionService.DeriveKey(password.AsSpan(), salt);
            await _zkVaultService.UnlockWithHybridKeyAsync(kek2);

            // Assert - Data should be intact
            using (var stream = await _zkVaultService.OpenFileStreamForViewingAsync(vaultFilePath))
            using (var reader = new StreamReader(stream))
            {
                string decrypted = await reader.ReadToEndAsync();
                Assert.Equal(testData, decrypted);
            }

            // Cleanup
            HybridKeyDerivation.ZeroMemory(kek, kek2);
            await _zkVaultService.LockAndWipeKeysAsync();
        }

        [Fact]
        public void LegacyVault_NoKemFields_DefaultsToPhase1Behavior()
        {
            // Arrange - Simulate legacy vault with no KEM fields
            string password = "LegacyPassword!";
            string manifestPath = Path.Combine(_testDirectory, "legacy_manifest.json");

            var legacyManifest = new VaultManifest
            {
                ContainerPath = "legacy_vault.vc",
                ContainerSizeBytes = 15 * 1024 * 1024,
                Algorithm = "AES-256-GCM",
                CreatedUtc = DateTime.UtcNow
                // No KEM fields = legacy/Phase 1
            };

            // Act
            _manifestService.WriteManifest(legacyManifest, manifestPath, password);
            var loaded = _manifestService.ReadManifest(manifestPath, password);

            // Assert - Should work with traditional encryption (no KEM fields)
            Assert.Null(loaded.KemPublicKeyBase64);
            Assert.Null(loaded.KemCiphertextBase64);
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public async Task VaultService_UnlockedAfterLock_ThrowsException()
        {
            // Arrange
            string password = "TestPassword!";
            byte[] salt = _encryptionService.GenerateSalt();
            byte[] kek = _encryptionService.DeriveKey(password.AsSpan(), salt);

            await _zkVaultService.UnlockWithHybridKeyAsync(kek);
            await _zkVaultService.LockAndWipeKeysAsync();

            // Act & Assert
            string testFile = Path.Combine(_testDirectory, "locked_test.zkf");
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("test")))
                {
                    await _zkVaultService.EncryptStreamAsync(stream, testFile);
                }
            });

            // Cleanup
            HybridKeyDerivation.ZeroMemory(kek);
        }

        [Fact]
        public async Task VaultService_CorruptedFile_ThrowsCryptographicException()
        {
            // Arrange
            string password = "TestPassword!";
            byte[] salt = _encryptionService.GenerateSalt();
            byte[] kek = _encryptionService.DeriveKey(password.AsSpan(), salt);
            await _zkVaultService.UnlockWithHybridKeyAsync(kek);

            string vaultFile = Path.Combine(_testDirectory, "corrupted_vault.zkf");
            
            // Create encrypted file
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("original data")))
            {
                await _zkVaultService.EncryptStreamAsync(stream, vaultFile);
            }

            // Corrupt the file
            byte[] fileBytes = File.ReadAllBytes(vaultFile);
            if (fileBytes.Length > 100)
            {
                fileBytes[50] ^= 0xFF; // Flip bits in the middle
                File.WriteAllBytes(vaultFile, fileBytes);
            }

            // Act & Assert
            // Corrupted files can throw either CryptographicException (auth tag failure)
            // or JsonException (corrupted header) depending on where corruption occurs
            var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                using (var stream = await _zkVaultService.OpenFileStreamForViewingAsync(vaultFile))
                {
                    await stream.ReadAsync(new byte[1024], 0, 1024);
                }
            });

            Assert.True(ex is System.Security.Cryptography.CryptographicException || ex is System.Text.Json.JsonException,
                $"Unexpected exception type for corrupted vault data: {ex.GetType().FullName}");

            // Cleanup
            HybridKeyDerivation.ZeroMemory(kek);
            await _zkVaultService.LockAndWipeKeysAsync();
        }

        [Fact]
        public async Task VaultService_NonExistentFile_ThrowsFileNotFoundException()
        {
            // Arrange
            string password = "TestPassword!";
            byte[] salt = _encryptionService.GenerateSalt();
            byte[] kek = _encryptionService.DeriveKey(password.AsSpan(), salt);
            await _zkVaultService.UnlockWithHybridKeyAsync(kek);

            string nonExistentFile = Path.Combine(_testDirectory, "does_not_exist.zkf");

            // Act & Assert
            await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            {
                using (var stream = await _zkVaultService.OpenFileStreamForViewingAsync(nonExistentFile))
                {
                    // Should not reach here
                }
            });

            // Cleanup
            HybridKeyDerivation.ZeroMemory(kek);
            await _zkVaultService.LockAndWipeKeysAsync();
        }

        #endregion

        #region Security Tests

        [Fact]
        public async Task SecurityTest_MemoryWipe_KeysNotAccessibleAfterLock()
        {
            // Arrange
            string password = "SecurePassword!";
            byte[] salt = _encryptionService.GenerateSalt();
            byte[] kemSharedSecret = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            
            byte[] kek = _encryptionService.DeriveKey(password.AsSpan(), salt);
            byte[] hybridDek = HybridKeyDerivation.DeriveHybridKey(kek, kemSharedSecret);

            // Act - Unlock and then lock
            await _zkVaultService.UnlockWithHybridKeyAsync(hybridDek);
            Assert.True(_zkVaultService.IsUnlocked);

            await _zkVaultService.LockAndWipeKeysAsync();
            Assert.False(_zkVaultService.IsUnlocked);

            // Assert - Attempting operations after lock should fail
            string testFile = Path.Combine(_testDirectory, "security_test.zkf");
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                using (var stream = await _zkVaultService.OpenFileStreamForViewingAsync(testFile))
                {
                    // Should not reach here
                }
            });

            // Cleanup
            HybridKeyDerivation.ZeroMemory(kek, kemSharedSecret, hybridDek);
        }

        [Fact]
        public async Task SecurityTest_DifferentPasswords_ProduceDifferentKeys()
        {
            // Arrange
            string password1 = "Password1!";
            string password2 = "Password2!";
            byte[] salt = _encryptionService.GenerateSalt();

            // Act
            byte[] kek1 = _encryptionService.DeriveKey(password1.AsSpan(), salt);
            byte[] kek2 = _encryptionService.DeriveKey(password2.AsSpan(), salt);

            // Assert
            Assert.NotEqual(kek1, kek2);
            Assert.Equal(32, kek1.Length);
            Assert.Equal(32, kek2.Length);

            // Cleanup
            HybridKeyDerivation.ZeroMemory(kek1, kek2);
        }

        [Fact]
        public async Task SecurityTest_LargeFile_EncryptsAndDecryptsCorrectly()
        {
            // Arrange
            string password = "LargeFileTest!";
            byte[] salt = _encryptionService.GenerateSalt();
            byte[] kek = _encryptionService.DeriveKey(password.AsSpan(), salt);
            await _zkVaultService.UnlockWithHybridKeyAsync(kek);

            // Create 1 MB of random data
            byte[] largeData = new byte[1024 * 1024];
            System.Security.Cryptography.RandomNumberGenerator.Fill(largeData);
            
            string vaultFile = Path.Combine(_testDirectory, "large_file.zkf");

            // Act - Encrypt
            using (var stream = new MemoryStream(largeData))
            {
                await _zkVaultService.EncryptStreamAsync(stream, vaultFile);
            }

            // Act - Decrypt and verify
            using (var decryptedStream = await _zkVaultService.OpenFileStreamForViewingAsync(vaultFile))
            {
                byte[] decryptedData = new byte[largeData.Length];
                int bytesRead = await decryptedStream.ReadAsync(decryptedData, 0, decryptedData.Length);
                
                Assert.Equal(largeData.Length, bytesRead);
                Assert.Equal(largeData, decryptedData);
            }

            // Cleanup
            HybridKeyDerivation.ZeroMemory(kek);
            await _zkVaultService.LockAndWipeKeysAsync();
        }

        #endregion
    }
}
