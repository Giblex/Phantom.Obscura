using System;
using System.IO;
using System.Threading;
using PhantomVault.Core.Models;
using PhantomVault.Core.Models.Security;
using PhantomVault.Core.Options;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.Security;
using Xunit;

namespace PhantomVault.Core.Tests.Security
{
    /// <summary>
    /// Integration tests for IntrusionService including:
    /// - Failed attempt tracking
    /// - Cooldown/lockout enforcement
    /// - Self-destruct triggering
    /// - Reset after successful authentication
    /// - Integration with DefenceEngine
    /// </summary>
    public class IntrusionServiceIntegrationTests : IDisposable
    {
        private readonly string _testDirectory;
        private readonly EncryptionService _encryptionService;
        private readonly ManifestService _manifestService;
        private readonly VaultService _vaultService;

        public IntrusionServiceIntegrationTests()
        {
            // Create temporary test directory
            _testDirectory = Path.Combine(Path.GetTempPath(), "IntrusionTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDirectory);

            // Initialize services
            _encryptionService = new EncryptionService();
            _manifestService = new ManifestService(_encryptionService);
            _vaultService = new VaultService(new VaultOptions());
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

        #region Test Helpers

        private class TestDefenceEngine : IDefenceEngine
        {
            public int ThreatCount { get; private set; }
            public ThreatEvent? LastThreat { get; private set; }
            public int WarningThreats { get; private set; }
            public int CriticalThreats { get; private set; }

            public void RaiseThreat(ThreatEvent threat)
            {
                ThreatCount++;
                LastThreat = threat;

                if (threat.Level == ThreatLevel.Warning)
                    WarningThreats++;
                else if (threat.Level == ThreatLevel.Critical)
                    CriticalThreats++;
            }
        }

        private VaultManifest CreateTestManifest()
        {
            return new VaultManifest
            {
                VaultName = "TestVault",
                ContainerPath = "test_container.vc",
                ContainerSizeBytes = 10 * 1024 * 1024, // 10 MB
                Algorithm = "AES-256-GCM",
                CreatedUtc = DateTime.UtcNow,
                MaxFailedAttempts = 5,
                SelfDestructEnabled = false,
                FailedAttempts = 0
            };
        }

        #endregion

        #region Failed Attempt Tracking Tests

        [Fact]
        public void RegisterFailedAttempt_IncrementsCounter()
        {
            // Arrange
            string password = "TestPassword123!";
            string manifestPath = Path.Combine(_testDirectory, "vault.manifest");
            var defenceEngine = new TestDefenceEngine();
            var intrusionService = new IntrusionService(_manifestService, _vaultService, defenceEngine);

            var manifest = CreateTestManifest();
            _manifestService.WriteManifest(manifest, manifestPath, password);

            // Act
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);

            // Assert
            Assert.Equal(1, manifest.FailedAttempts);

            // Verify manifest was persisted
            var loadedManifest = _manifestService.ReadManifest(manifestPath, password);
            Assert.Equal(1, loadedManifest.FailedAttempts);
        }

        [Fact]
        public void RegisterFailedAttempt_MultipleTimes_IncrementsProperly()
        {
            // Arrange
            string password = "TestPassword123!";
            string manifestPath = Path.Combine(_testDirectory, "vault.manifest");
            var defenceEngine = new TestDefenceEngine();
            var intrusionService = new IntrusionService(_manifestService, _vaultService, defenceEngine);

            var manifest = CreateTestManifest();
            _manifestService.WriteManifest(manifest, manifestPath, password);

            // Act - Register 3 failed attempts
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);

            // Assert
            Assert.Equal(3, manifest.FailedAttempts);

            var loadedManifest = _manifestService.ReadManifest(manifestPath, password);
            Assert.Equal(3, loadedManifest.FailedAttempts);
        }

        [Fact]
        public void RegisterFailedAttempt_RaisesWarningThreatToDefenceEngine()
        {
            // Arrange
            string password = "TestPassword123!";
            string manifestPath = Path.Combine(_testDirectory, "vault.manifest");
            var defenceEngine = new TestDefenceEngine();
            var intrusionService = new IntrusionService(_manifestService, _vaultService, defenceEngine);

            var manifest = CreateTestManifest();
            _manifestService.WriteManifest(manifest, manifestPath, password);

            // Act
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);

            // Assert
            Assert.Equal(1, defenceEngine.ThreatCount);
            Assert.Equal(1, defenceEngine.WarningThreats);
            Assert.NotNull(defenceEngine.LastThreat);
            Assert.Equal(ThreatType.FailedLoginBurst, defenceEngine.LastThreat.Type);
            Assert.Equal(ThreatLevel.Warning, defenceEngine.LastThreat.Level);
        }

        #endregion

        #region Lockout Tests

        [Fact]
        public void RegisterFailedAttempt_AtMaxAttempts_EnforcesLockout()
        {
            // Arrange
            string password = "TestPassword123!";
            string manifestPath = Path.Combine(_testDirectory, "vault.manifest");
            var defenceEngine = new TestDefenceEngine();
            var intrusionService = new IntrusionService(_manifestService, _vaultService, defenceEngine);

            var manifest = CreateTestManifest();
            manifest.MaxFailedAttempts = 3;
            manifest.SelfDestructEnabled = false;
            _manifestService.WriteManifest(manifest, manifestPath, password);

            // Act - Hit max attempts
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);

            // Assert
            Assert.Equal(3, manifest.FailedAttempts);
            Assert.NotNull(manifest.LockedUntilUtc);
            Assert.True(manifest.LockedUntilUtc.Value > DateTimeOffset.UtcNow);

            // Verify lockout is at least 10 minutes
            var lockoutDuration = manifest.LockedUntilUtc.Value - DateTimeOffset.UtcNow;
            Assert.True(lockoutDuration.TotalMinutes >= 9.9, // Allow small timing variance
                $"Lockout duration should be at least 10 minutes, but was {lockoutDuration.TotalMinutes} minutes");
        }

        [Fact]
        public void RegisterFailedAttempt_BeyondMaxAttempts_IncreasesLockoutDuration()
        {
            // Arrange
            string password = "TestPassword123!";
            string manifestPath = Path.Combine(_testDirectory, "vault.manifest");
            var defenceEngine = new TestDefenceEngine();
            var intrusionService = new IntrusionService(_manifestService, _vaultService, defenceEngine);

            var manifest = CreateTestManifest();
            manifest.MaxFailedAttempts = 3;
            manifest.SelfDestructEnabled = false;
            _manifestService.WriteManifest(manifest, manifestPath, password);

            // Act - Hit max attempts
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);
            var firstLockout = manifest.LockedUntilUtc;

            // Act - Go beyond max attempts
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);
            var secondLockout = manifest.LockedUntilUtc;

            // Assert - Second lockout should be longer (20 minutes total)
            Assert.NotNull(firstLockout);
            Assert.NotNull(secondLockout);
            Assert.True(secondLockout.Value > firstLockout.Value,
                "Second lockout should be longer than first");

            var firstDuration = firstLockout.Value - DateTimeOffset.UtcNow;
            var secondDuration = secondLockout.Value - DateTimeOffset.UtcNow;
            Assert.True(secondDuration > firstDuration);
        }

        [Fact]
        public void RegisterFailedAttempt_AtMaxAttempts_RaisesCriticalThreat()
        {
            // Arrange
            string password = "TestPassword123!";
            string manifestPath = Path.Combine(_testDirectory, "vault.manifest");
            var defenceEngine = new TestDefenceEngine();
            var intrusionService = new IntrusionService(_manifestService, _vaultService, defenceEngine);

            var manifest = CreateTestManifest();
            manifest.MaxFailedAttempts = 2;
            manifest.SelfDestructEnabled = false;
            _manifestService.WriteManifest(manifest, manifestPath, password);

            // Act
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);
            Assert.Equal(1, defenceEngine.WarningThreats);
            Assert.Equal(0, defenceEngine.CriticalThreats);

            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);

            // Assert - Should raise Critical threat at max attempts
            Assert.Equal(2, defenceEngine.WarningThreats);
            Assert.Equal(1, defenceEngine.CriticalThreats);
            Assert.Equal(ThreatLevel.Critical, defenceEngine.LastThreat.Level);
        }

        #endregion

        #region Self-Destruct Tests

        [Fact]
        public void RegisterFailedAttempt_WithSelfDestructEnabled_DestroyFilesAtMaxAttempts()
        {
            // Arrange
            string password = "TestPassword123!";
            string manifestPath = Path.Combine(_testDirectory, "vault.manifest");
            string containerPath = Path.Combine(_testDirectory, "test_container.vc");

            var defenceEngine = new TestDefenceEngine();
            var intrusionService = new IntrusionService(_manifestService, _vaultService, defenceEngine);

            var manifest = CreateTestManifest();
            manifest.MaxFailedAttempts = 2;
            manifest.SelfDestructEnabled = true; // Enable self-destruct
            manifest.ContainerPath = "test_container.vc";

            // Create dummy container file
            File.WriteAllText(containerPath, "dummy container data");

            _manifestService.WriteManifest(manifest, manifestPath, password);

            // Verify files exist before
            Assert.True(File.Exists(manifestPath));
            Assert.True(File.Exists(containerPath));

            // Act - Hit max attempts
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);

            // Give file system operations time to complete
            Thread.Sleep(200);

            // Assert - Files should be deleted
            Assert.False(File.Exists(manifestPath), "Manifest should be deleted after self-destruct");
            Assert.False(File.Exists(containerPath), "Container should be deleted after self-destruct");
        }

        [Fact]
        public void RegisterFailedAttempt_SelfDestruct_RaisesCriticalThreat()
        {
            // Arrange
            string password = "TestPassword123!";
            string manifestPath = Path.Combine(_testDirectory, "vault.manifest");
            string containerPath = Path.Combine(_testDirectory, "test_container.vc");

            var defenceEngine = new TestDefenceEngine();
            var intrusionService = new IntrusionService(_manifestService, _vaultService, defenceEngine);

            var manifest = CreateTestManifest();
            manifest.MaxFailedAttempts = 1;
            manifest.SelfDestructEnabled = true;
            manifest.ContainerPath = "test_container.vc";

            File.WriteAllText(containerPath, "dummy data");
            _manifestService.WriteManifest(manifest, manifestPath, password);

            // Act
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);
            Thread.Sleep(200);

            // Assert - Should raise Critical threat before self-destruct
            Assert.True(defenceEngine.CriticalThreats >= 1);
            Assert.Contains("Self-destruct triggered", defenceEngine.LastThreat.Details);
        }

        #endregion

        #region Reset Tests

        [Fact]
        public void ResetAttempts_ClearsFailedAttemptsCounter()
        {
            // Arrange
            string password = "TestPassword123!";
            string manifestPath = Path.Combine(_testDirectory, "vault.manifest");
            var defenceEngine = new TestDefenceEngine();
            var intrusionService = new IntrusionService(_manifestService, _vaultService, defenceEngine);

            var manifest = CreateTestManifest();
            _manifestService.WriteManifest(manifest, manifestPath, password);

            // Register some failed attempts
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);
            Assert.Equal(2, manifest.FailedAttempts);

            // Act - Reset
            intrusionService.ResetAttempts(manifest, manifestPath, password, null);

            // Assert
            Assert.Equal(0, manifest.FailedAttempts);
            Assert.Null(manifest.LockedUntilUtc);

            var loadedManifest = _manifestService.ReadManifest(manifestPath, password);
            Assert.Equal(0, loadedManifest.FailedAttempts);
            Assert.Null(loadedManifest.LockedUntilUtc);
        }

        [Fact]
        public void ResetAttempts_ClearsLockout()
        {
            // Arrange
            string password = "TestPassword123!";
            string manifestPath = Path.Combine(_testDirectory, "vault.manifest");
            var defenceEngine = new TestDefenceEngine();
            var intrusionService = new IntrusionService(_manifestService, _vaultService, defenceEngine);

            var manifest = CreateTestManifest();
            manifest.MaxFailedAttempts = 2;
            _manifestService.WriteManifest(manifest, manifestPath, password);

            // Trigger lockout
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);
            Assert.NotNull(manifest.LockedUntilUtc);

            // Act - Reset
            intrusionService.ResetAttempts(manifest, manifestPath, password, null);

            // Assert
            Assert.Equal(0, manifest.FailedAttempts);
            Assert.Null(manifest.LockedUntilUtc);
        }

        #endregion

        #region Keyfile Integration Tests

        [Fact]
        public void RegisterFailedAttempt_WithKeyfile_WorksCorrectly()
        {
            // Arrange
            string password = "TestPassword123!";
            string keyfilePath = Path.Combine(_testDirectory, "keyfile.key");
            string manifestPath = Path.Combine(_testDirectory, "vault.manifest");

            // Create keyfile
            File.WriteAllBytes(keyfilePath, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

            var defenceEngine = new TestDefenceEngine();
            var intrusionService = new IntrusionService(_manifestService, _vaultService, defenceEngine);

            var manifest = CreateTestManifest();
            _manifestService.WriteManifest(manifest, manifestPath, password, keyfilePath);

            // Act
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, keyfilePath, _testDirectory);

            // Assert
            Assert.Equal(1, manifest.FailedAttempts);

            // Verify with keyfile
            var loadedManifest = _manifestService.ReadManifest(manifestPath, password, keyfilePath);
            Assert.Equal(1, loadedManifest.FailedAttempts);
        }

        [Fact]
        public void ResetAttempts_WithKeyfile_WorksCorrectly()
        {
            // Arrange
            string password = "TestPassword123!";
            string keyfilePath = Path.Combine(_testDirectory, "keyfile.key");
            string manifestPath = Path.Combine(_testDirectory, "vault.manifest");

            File.WriteAllBytes(keyfilePath, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

            var defenceEngine = new TestDefenceEngine();
            var intrusionService = new IntrusionService(_manifestService, _vaultService, defenceEngine);

            var manifest = CreateTestManifest();
            _manifestService.WriteManifest(manifest, manifestPath, password, keyfilePath);

            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, keyfilePath, _testDirectory);
            Assert.Equal(1, manifest.FailedAttempts);

            // Act
            intrusionService.ResetAttempts(manifest, manifestPath, password, keyfilePath);

            // Assert
            Assert.Equal(0, manifest.FailedAttempts);

            var loadedManifest = _manifestService.ReadManifest(manifestPath, password, keyfilePath);
            Assert.Equal(0, loadedManifest.FailedAttempts);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void RegisterFailedAttempt_WithZeroMaxAttempts_ImmediatelyEnforcesLockout()
        {
            // Arrange
            string password = "TestPassword123!";
            string manifestPath = Path.Combine(_testDirectory, "vault.manifest");
            var defenceEngine = new TestDefenceEngine();
            var intrusionService = new IntrusionService(_manifestService, _vaultService, defenceEngine);

            var manifest = CreateTestManifest();
            manifest.MaxFailedAttempts = 0; // Zero tolerance
            _manifestService.WriteManifest(manifest, manifestPath, password);

            // Act
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);

            // Assert
            Assert.NotNull(manifest.LockedUntilUtc);
        }

        [Fact]
        public void RegisterFailedAttempt_WithVeryHighMaxAttempts_DoesNotLockout()
        {
            // Arrange
            string password = "TestPassword123!";
            string manifestPath = Path.Combine(_testDirectory, "vault.manifest");
            var defenceEngine = new TestDefenceEngine();
            var intrusionService = new IntrusionService(_manifestService, _vaultService, defenceEngine);

            var manifest = CreateTestManifest();
            manifest.MaxFailedAttempts = 1000; // Very high threshold
            _manifestService.WriteManifest(manifest, manifestPath, password);

            // Act - Only a few attempts
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);
            intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, _testDirectory);

            // Assert - Should not lock out
            Assert.Null(manifest.LockedUntilUtc);
            Assert.Equal(3, manifest.FailedAttempts);
        }

        #endregion
    }
}
