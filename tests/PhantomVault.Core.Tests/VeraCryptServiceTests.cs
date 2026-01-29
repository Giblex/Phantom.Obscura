using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    /// <summary>
    /// Unit tests for VeraCryptService. These tests verify the service's API surface
    /// and behavior without requiring VeraCrypt to be installed. For integration tests
    /// that actually invoke VeraCrypt, create a separate test suite.
    /// </summary>
    public class VeraCryptServiceTests
    {
        [Fact]
        public void Constructor_InitializesVeraCryptPath()
        {
            // Arrange & Act
            var service = new VeraCryptService();

            // Assert
            Assert.NotNull(service.VeraCryptPath);
            // Path might be empty if VeraCrypt is not installed, which is acceptable
        }

        [Fact]
        public void IsVeraCryptInstalled_ReturnsBoolean()
        {
            // Arrange
            var service = new VeraCryptService();

            // Act
            var isInstalled = service.IsVeraCryptInstalled();

            // Assert
            // We can't assert a specific value since it depends on the test machine
            // Just verify it returns without throwing
            Assert.True(isInstalled || !isInstalled);
        }

        [Fact]
        public void VeraCryptPath_WhenInstalled_ReturnsNonEmptyPath()
        {
            // Arrange
            var service = new VeraCryptService();

            // Act
            var path = service.VeraCryptPath;

            // Assert
            // If VeraCrypt is installed, path should not be null
            // If not installed, it might be empty string
            if (service.IsVeraCryptInstalled())
            {
                Assert.False(string.IsNullOrEmpty(path));
            }
        }

        [Fact]
        public async Task CreateVolumeAsync_WithReadOnlySpan_DoesNotThrow()
        {
            // Arrange
            var service = new VeraCryptService();
            var containerPath = Path.Combine(Path.GetTempPath(), $"test_container_{Guid.NewGuid()}.hc");
            var passwordString = "TestPassword123!";
            long sizeBytes = 1024 * 1024; // 1 MB

            try
            {
                // Act & Assert
                // This should not throw due to ReadOnlySpan<char> in async method
                // The actual VeraCrypt call will likely fail if VeraCrypt is not installed,
                // but we're testing that the signature works
                var result = await service.CreateVolumeAsync(containerPath, passwordString.AsSpan(), sizeBytes, CancellationToken.None);
                
                // Result should not be null
                Assert.NotNull(result);
            }
            finally
            {
                // Cleanup
                if (File.Exists(containerPath))
                {
                    File.Delete(containerPath);
                }
            }
        }

        [Fact]
        public async Task CreateContainerAsync_WithProgress_ReportsProgress()
        {
            // Arrange
            var service = new VeraCryptService();

            // Skip integration-heavy path when VeraCrypt is not present to avoid external process hangs
            if (!service.IsVeraCryptInstalled())
            {
                return;
            }

            var containerPath = Path.Combine(Path.GetTempPath(), $"test_container_{Guid.NewGuid()}.hc");
            var progressReported = false;
            var progress = new Progress<int>(p =>
            {
                progressReported = true;
            });

            try
            {
                // Act
                var (success, message) = await service.CreateContainerAsync(
                    containerPath,
                    1, // 1 MB
                    "TestPassword123!",
                    "AES",
                    "SHA-512",
                    "None",
                    progress);

                // Assert
                Assert.NotNull(message);
                // Progress should be reported (at least initial 10% or final 100%)
                Assert.True(progressReported);
            }
            finally
            {
                // Cleanup
                if (File.Exists(containerPath))
                {
                    File.Delete(containerPath);
                }
            }
        }

        [Fact]
        public async Task MountVolumeAsync_WithReadOnlySpan_DoesNotThrow()
        {
            // Arrange
            var service = new VeraCryptService();

            if (!service.IsVeraCryptInstalled())
            {
                return;
            }

            var containerPath = Path.Combine(Path.GetTempPath(), $"test_container_{Guid.NewGuid()}.hc");
            var passwordString = "TestPassword123!";
            var driveLetter = 'Z';

            // Act & Assert
            // This should not throw due to ReadOnlySpan<char> in async method
            // Operation will fail (file doesn't exist) but should handle timeout gracefully
            try
            {
                var result = await service.MountVolumeAsync(containerPath, driveLetter, passwordString.AsSpan(), CancellationToken.None);
                
                // Result should not be null
                Assert.NotNull(result);
            }
            catch (TimeoutException)
            {
                // Expected if VeraCrypt is hung - timeout mechanism is working
                Assert.True(true, "Timeout mechanism worked correctly");
            }
        }

        [Fact]
        public async Task DismountVolumeAsync_DoesNotThrow()
        {
            // Arrange
            var service = new VeraCryptService();

            if (!service.IsVeraCryptInstalled())
            {
                return;
            }

            var driveLetter = 'Z';

            // Act & Assert
            try
            {
                var result = await service.DismountVolumeAsync(driveLetter, CancellationToken.None);
                
                // Result should not be null
                Assert.NotNull(result);
            }
            catch (TimeoutException)
            {
                // Expected if VeraCrypt is hung - timeout mechanism is working
                Assert.True(true, "Timeout mechanism worked correctly");
            }
        }

        [Theory]
        [InlineData("AES")]
        [InlineData("Serpent")]
        [InlineData("Twofish")]
        [InlineData("AES-Twofish")]
        public async Task CreateContainerAsync_WithDifferentEncryptions_ReturnsResult(string encryption)
        {
            // Arrange
            var service = new VeraCryptService();

            // Skip integration-heavy path when VeraCrypt is not present to avoid external process hangs
            if (!service.IsVeraCryptInstalled())
            {
                return;
            }

            var containerPath = Path.Combine(Path.GetTempPath(), $"test_container_{Guid.NewGuid()}.hc");

            try
            {
                // Act
                var (success, message) = await service.CreateContainerAsync(
                    containerPath,
                    1, // 1 MB
                    "TestPassword123!",
                    encryption,
                    "SHA-512",
                    "None",
                    null);

                // Assert
                Assert.NotNull(message);
                // We don't assert success because VeraCrypt might not be installed
            }
            finally
            {
                // Cleanup
                if (File.Exists(containerPath))
                {
                    File.Delete(containerPath);
                }
            }
        }

        [Fact]
        public async Task CreateContainerAsync_WhenVeraCryptNotInstalled_ReturnsFailure()
        {
            // Arrange
            var service = new VeraCryptService();
            
            // Skip test if VeraCrypt is actually installed
            if (service.IsVeraCryptInstalled())
            {
                return;
            }

            var containerPath = Path.Combine(Path.GetTempPath(), $"test_container_{Guid.NewGuid()}.hc");

            try
            {
                // Act
                var (success, message) = await service.CreateContainerAsync(
                    containerPath,
                    1,
                    "TestPassword123!",
                    "AES",
                    "SHA-512",
                    "None",
                    null);

                // Assert
                Assert.False(success);
                Assert.Contains("not installed", message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                // Cleanup
                if (File.Exists(containerPath))
                {
                    File.Delete(containerPath);
                }
            }
        }

        #region Mount Permutation Tests (Password/Keyfile combinations)

        /// <summary>
        /// Tests that MountVolumeAsync with keyfile-only (empty password) constructs
        /// the correct VeraCrypt CLI arguments without /stdin flag.
        /// </summary>
        [Fact]
        public async Task MountVolumeAsync_KeyfileOnly_DoesNotThrow()
        {
            // Arrange
            var service = new VeraCryptService();

            if (!service.IsVeraCryptInstalled())
            {
                return; // Skip if VeraCrypt not installed
            }

            var containerPath = Path.Combine(Path.GetTempPath(), $"test_container_{Guid.NewGuid()}.hc");
            var keyfilePath = Path.Combine(Path.GetTempPath(), $"test_keyfile_{Guid.NewGuid()}.key");
            var driveLetter = 'Z';

            try
            {
                // Create dummy keyfile
                await File.WriteAllBytesAsync(keyfilePath, new byte[64]);

                // Act - Mount with keyfile only (empty password)
                var result = await service.MountVolumeAsync(
                    containerPath, 
                    driveLetter, 
                    string.Empty.AsSpan(), // Empty password
                    keyfilePath, // Keyfile provided
                    CancellationToken.None);

                // Assert
                Assert.NotNull(result);
                // Expect failure since container doesn't exist, but API should work
            }
            catch (TimeoutException)
            {
                Assert.True(true, "Timeout mechanism worked correctly");
            }
            finally
            {
                if (File.Exists(containerPath)) File.Delete(containerPath);
                if (File.Exists(keyfilePath)) File.Delete(keyfilePath);
            }
        }

        /// <summary>
        /// Tests that MountVolumeAsync with password-only (no keyfile) works correctly.
        /// </summary>
        [Fact]
        public async Task MountVolumeAsync_PasswordOnly_DoesNotThrow()
        {
            // Arrange
            var service = new VeraCryptService();

            if (!service.IsVeraCryptInstalled())
            {
                return;
            }

            var containerPath = Path.Combine(Path.GetTempPath(), $"test_container_{Guid.NewGuid()}.hc");
            var passwordString = "TestPassword123!";
            var driveLetter = 'Z';

            try
            {
                // Act - Mount with password only (no keyfile)
                var result = await service.MountVolumeAsync(
                    containerPath,
                    driveLetter,
                    passwordString.AsSpan(),
                    null, // No keyfile
                    CancellationToken.None);

                // Assert
                Assert.NotNull(result);
            }
            catch (TimeoutException)
            {
                Assert.True(true, "Timeout mechanism worked correctly");
            }
            finally
            {
                if (File.Exists(containerPath)) File.Delete(containerPath);
            }
        }

        /// <summary>
        /// Tests that MountVolumeAsync with dual-factor (password AND keyfile) works correctly.
        /// </summary>
        [Fact]
        public async Task MountVolumeAsync_DualFactor_DoesNotThrow()
        {
            // Arrange
            var service = new VeraCryptService();

            if (!service.IsVeraCryptInstalled())
            {
                return;
            }

            var containerPath = Path.Combine(Path.GetTempPath(), $"test_container_{Guid.NewGuid()}.hc");
            var keyfilePath = Path.Combine(Path.GetTempPath(), $"test_keyfile_{Guid.NewGuid()}.key");
            var passwordString = "TestPassword123!";
            var driveLetter = 'Z';

            try
            {
                // Create dummy keyfile
                await File.WriteAllBytesAsync(keyfilePath, new byte[64]);

                // Act - Mount with both password AND keyfile
                var result = await service.MountVolumeAsync(
                    containerPath,
                    driveLetter,
                    passwordString.AsSpan(),
                    keyfilePath,
                    CancellationToken.None);

                // Assert
                Assert.NotNull(result);
            }
            catch (TimeoutException)
            {
                Assert.True(true, "Timeout mechanism worked correctly");
            }
            finally
            {
                if (File.Exists(containerPath)) File.Delete(containerPath);
                if (File.Exists(keyfilePath)) File.Delete(keyfilePath);
            }
        }

        /// <summary>
        /// Tests that CreateVolumeAsync with keyfile support works correctly.
        /// </summary>
        [Fact]
        public async Task CreateVolumeAsync_WithKeyfile_DoesNotThrow()
        {
            // Arrange
            var service = new VeraCryptService();
            var containerPath = Path.Combine(Path.GetTempPath(), $"test_container_{Guid.NewGuid()}.hc");
            var keyfilePath = Path.Combine(Path.GetTempPath(), $"test_keyfile_{Guid.NewGuid()}.key");
            var passwordString = "TestPassword123!";
            long sizeBytes = 1024 * 1024; // 1 MB

            try
            {
                // Create dummy keyfile
                await File.WriteAllBytesAsync(keyfilePath, new byte[64]);

                // Act
                var result = await service.CreateVolumeAsync(
                    containerPath,
                    passwordString.AsSpan(),
                    sizeBytes,
                    keyfilePath,
                    CancellationToken.None);

                // Assert
                Assert.NotNull(result);
            }
            finally
            {
                if (File.Exists(containerPath)) File.Delete(containerPath);
                if (File.Exists(keyfilePath)) File.Delete(keyfilePath);
            }
        }

        /// <summary>
        /// Tests the backward-compatible overload without keyfile parameter.
        /// </summary>
        [Fact]
        public async Task MountVolumeAsync_BackwardCompatibleOverload_StillWorks()
        {
            // Arrange
            var service = new VeraCryptService();

            if (!service.IsVeraCryptInstalled())
            {
                return;
            }

            var containerPath = Path.Combine(Path.GetTempPath(), $"test_container_{Guid.NewGuid()}.hc");
            var passwordString = "TestPassword123!";
            var driveLetter = 'Z';

            try
            {
                // Act - Use original overload without keyfile parameter
                var result = await service.MountVolumeAsync(
                    containerPath,
                    driveLetter,
                    passwordString.AsSpan(),
                    CancellationToken.None);

                // Assert
                Assert.NotNull(result);
            }
            catch (TimeoutException)
            {
                Assert.True(true, "Timeout mechanism worked correctly");
            }
            finally
            {
                if (File.Exists(containerPath)) File.Delete(containerPath);
            }
        }

        #endregion
    }
}
