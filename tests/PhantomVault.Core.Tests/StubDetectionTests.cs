using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using PhantomVault.Core.Services.Security;
using Microsoft.Extensions.Logging;

namespace PhantomVault.Core.Tests
{
    /// <summary>
    /// Tests that detect when security-critical code paths are using stub implementations.
    /// These tests are designed to FAIL if stubs are called in production, alerting developers
    /// that real implementations need to be wired.
    /// </summary>
    public class StubDetectionTests
    {
        #region VaultController Decoy Implementation Tests

        [Fact]
        public async Task SwitchToDecoyVault_IsImplemented_ActivatesDecoy()
        {
            // Arrange
            var controller = new VaultController();

            // Act - This should now work (no longer throws NotImplementedException)
            await controller.SwitchToDecoyVaultAsync();

            // Assert - Decoy should be active
            Assert.True(controller.IsDecoyActive);
            Assert.NotNull(controller.ActiveDecoyDatabase);
            Assert.NotNull(controller.ActiveDecoyDatabase.Groups);
            Assert.NotEmpty(controller.ActiveDecoyDatabase.Groups);
        }

        [Fact]
        public async Task SwitchToDecoyVault_GeneratesRealisticCredentials()
        {
            // Arrange
            var controller = new VaultController();

            // Act
            await controller.SwitchToDecoyVaultAsync();

            // Assert - Verify decoy credentials look realistic
            var decoyDb = controller.ActiveDecoyDatabase;
            Assert.NotNull(decoyDb);
            Assert.NotNull(decoyDb.Groups);

            // Should have a reasonable number of credentials across all groups (15-30 total)
            int totalCredentials = decoyDb.Groups.Sum(g => g.Entries?.Count ?? 0);
            Assert.InRange(totalCredentials, 10, 50);

            // Should have multiple groups
            Assert.InRange(decoyDb.Groups.Count, 2, 10);

            // Should have different entry types
            var allEntries = decoyDb.Groups.SelectMany(g => g.Entries ?? Enumerable.Empty<Models.Credential>()).ToList();
            var hasPassword = allEntries.Any(c => c.EntryType == Models.EntryType.Password);
            Assert.True(hasPassword, "Decoy should contain password entries");

            // Credentials should have realistic data
            foreach (var cred in allEntries)
            {
                Assert.NotEmpty(cred.Title);
                // At least one identifying field should be set
                Assert.True(
                    !string.IsNullOrEmpty(cred.Username) ||
                    !string.IsNullOrEmpty(cred.Password) ||
                    !string.IsNullOrEmpty(cred.ApiKeyValue) ||
                    !string.IsNullOrEmpty(cred.WiFiSSID));
            }
        }

        [Fact]
        public async Task SwitchToDecoyVault_EntersReadOnlyMode()
        {
            // Arrange
            var controller = new VaultController();
            Assert.False(controller.IsReadOnly);

            // Act
            await controller.SwitchToDecoyVaultAsync();

            // Assert - Should automatically enter read-only mode
            Assert.True(controller.IsReadOnly);
        }

        #endregion

        #region VaultController Read-Only Mode Tests

        [Fact]
        public void EnterReadOnlyMode_SetsIsReadOnlyTrue()
        {
            // Arrange
            var controller = new VaultController();

            // Act
            controller.EnterReadOnlyMode();

            // Assert - This behavior IS implemented
            Assert.True(controller.IsReadOnly);
        }

        [Fact]
        public void ExitReadOnlyMode_SetsIsReadOnlyFalse()
        {
            // Arrange
            var controller = new VaultController();
            controller.EnterReadOnlyMode();
            Assert.True(controller.IsReadOnly);

            // Act
            controller.ExitReadOnlyMode();

            // Assert - This behavior IS implemented
            Assert.False(controller.IsReadOnly);
        }

        #endregion
    }
}
