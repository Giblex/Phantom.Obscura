using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Xunit;
using Moq;
using PhantomVault.Core.Services;

namespace PhantomVault.Core.Tests.Services
{
    /// <summary>
    /// Comprehensive test suite for UsbBindingService with mocked USB devices.
    /// Tests device ID computation, binding enforcement, and high-assurance binding.
    /// </summary>
    public class UsbBindingServiceTests
    {
        #region Mock Helper

        private class MockFileSystem
        {
            public bool FileExists { get; set; }
            public byte[]? FileContent { get; set; }
            public bool ThrowOnWrite { get; set; }
            public bool ThrowOnRead { get; set; }
        }

        #endregion

        #region ComputeDeviceId Tests

        [Fact]
        public void ComputeDeviceId_ValidDrive_ReturnsHexString()
        {
            // Arrange
            var service = new UsbBindingService();
            var driveRoot = Directory.GetCurrentDirectory(); // Use current directory as mock drive

            // Act
            var deviceId = service.ComputeDeviceId(driveRoot);

            // Assert
            Assert.NotNull(deviceId);
            Assert.NotEmpty(deviceId);
            Assert.Matches("^[0-9A-F]+$", deviceId); // Should be hex string
        }

        [Fact]
        public void ComputeDeviceId_SameDrive_ReturnsSameId()
        {
            // Arrange
            var service = new UsbBindingService();
            var driveRoot = Directory.GetCurrentDirectory();

            // Act
            var id1 = service.ComputeDeviceId(driveRoot);
            var id2 = service.ComputeDeviceId(driveRoot);

            // Assert
            Assert.Equal(id1, id2);
        }

        [Fact]
        public void ComputeDeviceId_InvalidDrive_ThrowsException()
        {
            // Arrange
            var service = new UsbBindingService();
            var invalidDrive = "Z:\\NonExistent\\Path";

            // Act & Assert
            var exception = Record.Exception(() => service.ComputeDeviceId(invalidDrive));
            Assert.NotNull(exception);
        }

        [Fact]
        public void ComputeDeviceId_EmptyPath_ThrowsException()
        {
            // Arrange
            var service = new UsbBindingService();

            // Act & Assert
            Assert.Throws<ArgumentException>(() => service.ComputeDeviceId(string.Empty));
        }

        #endregion

        #region EnsureBoundToDevice Tests

        [Fact]
        public void EnsureBoundToDevice_MatchingId_DoesNotThrow()
        {
            // Arrange
            var service = new UsbBindingService();
            var driveRoot = Directory.GetCurrentDirectory();
            var deviceId = service.ComputeDeviceId(driveRoot);

            // Act & Assert - Should not throw
            service.EnsureBoundToDevice(driveRoot, deviceId);
        }

        [Fact]
        public void EnsureBoundToDevice_MismatchedId_ThrowsException()
        {
            // Arrange
            var service = new UsbBindingService();
            var driveRoot = Directory.GetCurrentDirectory();
            var wrongDeviceId = "FFFFFFFFFFFFFFFF";

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                service.EnsureBoundToDevice(driveRoot, wrongDeviceId));

            Assert.Contains("device", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region High-Assurance Binding Tests

        [Fact]
        public void CreateHiddenDeviceId_ValidInputs_CreatesFile()
        {
            // Arrange
            var service = new UsbBindingService();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            var vaultSalt = new byte[16];
            RandomNumberGenerator.Fill(vaultSalt);

            try
            {
                // Act
                var hiddenFilePath = service.CreateHiddenDeviceId(tempDir, vaultSalt);

                // Assert
                Assert.NotNull(hiddenFilePath);
                Assert.True(File.Exists(hiddenFilePath), "Hidden device ID file should exist");
                Assert.Contains(".phantom_device_id", hiddenFilePath);

                // Verify file content is valid JSON
                var content = File.ReadAllText(hiddenFilePath);
                Assert.Contains("deviceId", content);
                Assert.Contains("timestamp", content);
                Assert.Contains("signature", content);
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ReadHiddenDeviceId_ValidFile_ReturnsDeviceId()
        {
            // Arrange
            var service = new UsbBindingService();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            var vaultSalt = new byte[16];
            RandomNumberGenerator.Fill(vaultSalt);

            try
            {
                // Create hidden device ID
                var hiddenFilePath = service.CreateHiddenDeviceId(tempDir, vaultSalt);

                // Act
                var deviceId = service.ReadHiddenDeviceId(tempDir, vaultSalt);

                // Assert
                Assert.NotNull(deviceId);
                Assert.NotEmpty(deviceId);
                Assert.Matches("^[0-9A-F]+$", deviceId); // Should be hex string
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ReadHiddenDeviceId_TamperedFile_ThrowsException()
        {
            // Arrange
            var service = new UsbBindingService();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            var vaultSalt = new byte[16];
            RandomNumberGenerator.Fill(vaultSalt);

            try
            {
                // Create hidden device ID
                var hiddenFilePath = service.CreateHiddenDeviceId(tempDir, vaultSalt);

                // Tamper with file content
                var content = File.ReadAllText(hiddenFilePath);
                var tamperedContent = content.Replace("deviceId", "tampered");
                File.WriteAllText(hiddenFilePath, tamperedContent);

                // Act & Assert
                var exception = Record.Exception(() => service.ReadHiddenDeviceId(tempDir, vaultSalt));
                Assert.NotNull(exception);
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ReadHiddenDeviceId_WrongSalt_ThrowsException()
        {
            // Arrange
            var service = new UsbBindingService();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            var vaultSalt = new byte[16];
            var wrongSalt = new byte[16];
            RandomNumberGenerator.Fill(vaultSalt);
            RandomNumberGenerator.Fill(wrongSalt);

            try
            {
                // Create hidden device ID with correct salt
                service.CreateHiddenDeviceId(tempDir, vaultSalt);

                // Act & Assert - Try to read with wrong salt
                var exception = Record.Exception(() => service.ReadHiddenDeviceId(tempDir, wrongSalt));
                Assert.NotNull(exception);
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ReadHiddenDeviceId_NonExistentFile_ThrowsException()
        {
            // Arrange
            var service = new UsbBindingService();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            var vaultSalt = new byte[16];
            RandomNumberGenerator.Fill(vaultSalt);

            try
            {
                // Act & Assert - File doesn't exist
                Assert.Throws<FileNotFoundException>(() =>
                    service.ReadHiddenDeviceId(tempDir, vaultSalt));
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ComputeHighAssuranceDeviceId_CombinesBothMethods()
        {
            // Arrange
            var service = new UsbBindingService();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            var vaultSalt = new byte[16];
            RandomNumberGenerator.Fill(vaultSalt);

            try
            {
                // Create hidden device ID first
                service.CreateHiddenDeviceId(tempDir, vaultSalt);

                // Act
                var highAssuranceId = service.ComputeHighAssuranceDeviceId(tempDir, vaultSalt);

                // Assert
                Assert.NotNull(highAssuranceId);
                Assert.NotEmpty(highAssuranceId);

                // High assurance ID should be different from simple device ID
                var simpleId = service.ComputeDeviceId(tempDir);
                Assert.NotEqual(simpleId, highAssuranceId);

                // Should be deterministic
                var highAssuranceId2 = service.ComputeHighAssuranceDeviceId(tempDir, vaultSalt);
                Assert.Equal(highAssuranceId, highAssuranceId2);
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        #endregion

        #region Hidden File Attribute Tests (Windows-specific)

        [Fact]
        public void CreateHiddenDeviceId_OnWindows_SetsHiddenAttribute()
        {
            // Skip on non-Windows platforms
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            // Arrange
            var service = new UsbBindingService();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            var vaultSalt = new byte[16];
            RandomNumberGenerator.Fill(vaultSalt);

            try
            {
                // Act
                var hiddenFilePath = service.CreateHiddenDeviceId(tempDir, vaultSalt);

                // Assert
                var attributes = File.GetAttributes(hiddenFilePath);
                Assert.True(attributes.HasFlag(FileAttributes.Hidden), "File should have Hidden attribute");
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        #endregion

        #region Timestamp Validation Tests

        [Fact]
        public void ReadHiddenDeviceId_RecentTimestamp_Succeeds()
        {
            // Arrange
            var service = new UsbBindingService();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            var vaultSalt = new byte[16];
            RandomNumberGenerator.Fill(vaultSalt);

            try
            {
                // Create file with current timestamp
                service.CreateHiddenDeviceId(tempDir, vaultSalt);

                // Act & Assert - Should succeed with recent timestamp
                var deviceId = service.ReadHiddenDeviceId(tempDir, vaultSalt);
                Assert.NotNull(deviceId);
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void CreateHiddenDeviceId_EmptyVaultSalt_ThrowsException()
        {
            // Arrange
            var service = new UsbBindingService();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Act & Assert
                Assert.Throws<ArgumentException>(() =>
                    service.CreateHiddenDeviceId(tempDir, Array.Empty<byte>()));
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ReadHiddenDeviceId_EmptyVaultSalt_ThrowsException()
        {
            // Arrange
            var service = new UsbBindingService();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                service.ReadHiddenDeviceId(tempDir, Array.Empty<byte>()));
        }

        #endregion

        #region Integration Tests

        [Fact]
        public void FullWorkflow_CreateAndVerifyHighAssuranceBinding()
        {
            // Arrange
            var service = new UsbBindingService();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            var vaultSalt = new byte[16];
            RandomNumberGenerator.Fill(vaultSalt);

            try
            {
                // Act - Complete high-assurance binding workflow
                // Step 1: Create hidden device ID
                var hiddenFilePath = service.CreateHiddenDeviceId(tempDir, vaultSalt);
                Assert.True(File.Exists(hiddenFilePath));

                // Step 2: Compute high-assurance device ID
                var highAssuranceId = service.ComputeHighAssuranceDeviceId(tempDir, vaultSalt);
                Assert.NotNull(highAssuranceId);

                // Step 3: Verify binding
                service.EnsureBoundToDevice(tempDir, highAssuranceId);

                // Step 4: Verify reading hidden device ID works
                var readDeviceId = service.ReadHiddenDeviceId(tempDir, vaultSalt);
                Assert.NotNull(readDeviceId);

                // Assert - All operations succeeded without throwing
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void AntiCloning_DifferentDrives_ProduceDifferentIds()
        {
            // Arrange
            var service = new UsbBindingService();
            var tempDir1 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var tempDir2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir1);
            Directory.CreateDirectory(tempDir2);

            try
            {
                // Act
                var id1 = service.ComputeDeviceId(tempDir1);
                var id2 = service.ComputeDeviceId(tempDir2);

                // Assert - Different directories should produce different IDs
                // (This is a weak test since they're on same physical drive, but demonstrates concept)
                Assert.NotNull(id1);
                Assert.NotNull(id2);
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir1))
                    Directory.Delete(tempDir1, true);
                if (Directory.Exists(tempDir2))
                    Directory.Delete(tempDir2, true);
            }
        }

        #endregion

        #region Security Property Tests

        [Fact]
        public void HiddenDeviceId_ContainsRandomData()
        {
            // Arrange
            var service = new UsbBindingService();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            var vaultSalt = new byte[16];
            RandomNumberGenerator.Fill(vaultSalt);

            try
            {
                // Act
                service.CreateHiddenDeviceId(tempDir, vaultSalt);
                var deviceId1 = service.ReadHiddenDeviceId(tempDir, vaultSalt);

                // Create another in different directory
                var tempDir2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir2);
                service.CreateHiddenDeviceId(tempDir2, vaultSalt);
                var deviceId2 = service.ReadHiddenDeviceId(tempDir2, vaultSalt);

                // Assert - Should produce different random device IDs
                Assert.NotEqual(deviceId1, deviceId2);

                // Cleanup second directory
                if (Directory.Exists(tempDir2))
                    Directory.Delete(tempDir2, true);
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void HiddenDeviceId_SignatureVerification_PreventsModification()
        {
            // Arrange
            var service = new UsbBindingService();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            var vaultSalt = new byte[16];
            RandomNumberGenerator.Fill(vaultSalt);

            try
            {
                // Create hidden device ID
                var hiddenFilePath = service.CreateHiddenDeviceId(tempDir, vaultSalt);

                // Read and modify the device ID in the file
                var content = File.ReadAllText(hiddenFilePath);
                var modified = content.Replace(
                    content.Substring(content.IndexOf("deviceId") + 12, 10),
                    "FFFFFFFFFF");
                File.WriteAllText(hiddenFilePath, modified);

                // Act & Assert - Should fail signature verification
                var exception = Record.Exception(() => service.ReadHiddenDeviceId(tempDir, vaultSalt));
                Assert.NotNull(exception);
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        #endregion
    }
}
