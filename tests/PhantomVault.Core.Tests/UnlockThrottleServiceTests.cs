using System;
using System.IO;
using System.Runtime.Versioning;
using Xunit;
using PhantomVault.Core.Services;

namespace PhantomVault.Core.Tests
{
    /// <summary>
    /// Unit tests for UnlockThrottleService testing throttling and lockout behavior.
    /// These tests use a temporary directory to avoid polluting the real throttle data.
    /// Note: These tests require Windows due to DPAPI dependency.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class UnlockThrottleServiceTests : IDisposable
    {
        private readonly string _tempManifestPath;
        private readonly string _tempThrottleDataPath;
        private readonly UnlockThrottleService _sut;

        public UnlockThrottleServiceTests()
        {
            // Create temporary paths for testing
            var tempDir = Path.Combine(Path.GetTempPath(), $"PhantomVault_Test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            _tempManifestPath = Path.Combine(tempDir, "test.manifest");
            _tempThrottleDataPath = tempDir;
            
            // Create a dummy manifest file
            File.WriteAllText(_tempManifestPath, "test manifest content");
            
            _sut = new UnlockThrottleService();
        }

        public void Dispose()
        {
            // Clean up temporary files
            try
            {
                var dir = Path.GetDirectoryName(_tempManifestPath);
                if (dir != null && Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch { /* Best effort cleanup */ }
        }

        #region IsThrottled Tests

        [Fact]
        [SupportedOSPlatform("windows")]
        public void IsThrottled_NoFailedAttempts_ReturnsFalse()
        {
            // Act
            var isThrottled = _sut.IsThrottled(_tempManifestPath, out var remainingLockout);

            // Assert
            Assert.False(isThrottled);
            Assert.Equal(TimeSpan.Zero, remainingLockout);
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public void IsThrottled_FewFailedAttempts_ReturnsFalse()
        {
            // Arrange - register 4 failed attempts (threshold is 5)
            for (int i = 0; i < 4; i++)
            {
                _sut.RegisterFailedAttempt(_tempManifestPath);
            }

            // Act
            var isThrottled = _sut.IsThrottled(_tempManifestPath, out var remainingLockout);

            // Assert
            Assert.False(isThrottled);
            Assert.Equal(TimeSpan.Zero, remainingLockout);
        }

        #endregion

        #region RegisterFailedAttempt Tests

        [Fact]
        [SupportedOSPlatform("windows")]
        public void RegisterFailedAttempt_IncrementsCounter()
        {
            // Arrange
            Assert.Equal(0, _sut.GetFailedAttemptCount(_tempManifestPath));

            // Act
            _sut.RegisterFailedAttempt(_tempManifestPath);

            // Assert
            Assert.Equal(1, _sut.GetFailedAttemptCount(_tempManifestPath));
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public void RegisterFailedAttempt_MultipleTimes_CountsCorrectly()
        {
            // Act
            for (int i = 1; i <= 3; i++)
            {
                _sut.RegisterFailedAttempt(_tempManifestPath);
                Assert.Equal(i, _sut.GetFailedAttemptCount(_tempManifestPath));
            }
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public void RegisterFailedAttempt_AtThreshold_TriggersLockout()
        {
            // Arrange - register 5 failed attempts to trigger lockout
            for (int i = 0; i < 5; i++)
            {
                _sut.RegisterFailedAttempt(_tempManifestPath);
            }

            // Act
            var isThrottled = _sut.IsThrottled(_tempManifestPath, out var remainingLockout);

            // Assert
            Assert.True(isThrottled);
            Assert.True(remainingLockout.TotalMinutes > 0 && remainingLockout.TotalMinutes <= 10);
        }

        #endregion

        #region ResetAttempts Tests

        [Fact]
        [SupportedOSPlatform("windows")]
        public void ResetAttempts_ClearsFailedCount()
        {
            // Arrange
            for (int i = 0; i < 3; i++)
            {
                _sut.RegisterFailedAttempt(_tempManifestPath);
            }
            Assert.Equal(3, _sut.GetFailedAttemptCount(_tempManifestPath));

            // Act
            _sut.ResetAttempts(_tempManifestPath);

            // Assert
            Assert.Equal(0, _sut.GetFailedAttemptCount(_tempManifestPath));
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public void ResetAttempts_ClearsLockout()
        {
            // Arrange - trigger lockout
            for (int i = 0; i < 5; i++)
            {
                _sut.RegisterFailedAttempt(_tempManifestPath);
            }
            Assert.True(_sut.IsThrottled(_tempManifestPath, out _));

            // Act
            _sut.ResetAttempts(_tempManifestPath);

            // Assert
            Assert.False(_sut.IsThrottled(_tempManifestPath, out _));
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public void ResetAttempts_NonexistentManifest_DoesNotThrow()
        {
            // Arrange
            var nonexistentPath = Path.Combine(_tempThrottleDataPath, "nonexistent.manifest");

            // Act & Assert - should not throw
            _sut.ResetAttempts(nonexistentPath);
        }

        #endregion

        #region GetFailedAttemptCount Tests

        [Fact]
        [SupportedOSPlatform("windows")]
        public void GetFailedAttemptCount_NoAttempts_ReturnsZero()
        {
            var count = _sut.GetFailedAttemptCount(_tempManifestPath);
            Assert.Equal(0, count);
        }

        #endregion

        #region Multiple Manifest Tests

        [Fact]
        [SupportedOSPlatform("windows")]
        public void ThrottleService_IsolatesManifests()
        {
            // Arrange - create second manifest
            var secondManifestPath = Path.Combine(Path.GetDirectoryName(_tempManifestPath)!, "second.manifest");
            File.WriteAllText(secondManifestPath, "second manifest");

            // Act - fail first manifest, not second
            for (int i = 0; i < 5; i++)
            {
                _sut.RegisterFailedAttempt(_tempManifestPath);
            }

            // Assert - first is locked, second is not
            Assert.True(_sut.IsThrottled(_tempManifestPath, out _));
            Assert.False(_sut.IsThrottled(secondManifestPath, out _));
        }

        #endregion
    }
}
