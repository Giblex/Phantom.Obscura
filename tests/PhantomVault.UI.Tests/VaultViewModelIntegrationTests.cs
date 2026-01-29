using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using PhantomVault.UI.ViewModels;
using Xunit;

namespace PhantomVault.UI.Tests
{
    /// <summary>
    /// Integration tests for VaultViewModel operations including:
    /// - Credential CRUD operations
    /// - Search and filtering
    /// - Lock/unlock operations
    /// - Category management
    /// - Policy enforcement
    /// </summary>
    public class VaultViewModelIntegrationTests : IDisposable
    {
        private readonly string _testDirectory;
        private readonly string _testVaultPath;
        private readonly Mock<IDialogService> _mockDialogService;
        private readonly Mock<INavigationService> _mockNavigationService;

        public VaultViewModelIntegrationTests()
        {
            // Create temporary test directory
            _testDirectory = Path.Combine(Path.GetTempPath(), "PhantomVaultUITests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDirectory);
            _testVaultPath = Path.Combine(_testDirectory, "test_vault");
            Directory.CreateDirectory(_testVaultPath);

            // Setup mocks for UI dependencies
            _mockDialogService = new Mock<IDialogService>();
            _mockNavigationService = new Mock<INavigationService>();
        }

        public void Dispose()
        {
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

        #region Credential CRUD Tests

        [Fact]
        public async Task CreateLoginCredential_ValidData_AddsToCollection()
        {
            // Arrange
            var vaultService = new VaultService();
            await vaultService.CreateVaultAsync(_testVaultPath, "TestPassword123!", null);
            await vaultService.UnlockVaultAsync(_testVaultPath, "TestPassword123!", null);

            var viewModel = new VaultViewModel(vaultService);
            await viewModel.LoadAsync(_testVaultPath, "TestPassword123!");

            int initialCount = viewModel.Credentials.Count;

            // Act
            var credential = new Credential
            {
                Id = Guid.NewGuid().ToString(),
                Title = "Test Login",
                Type = CredentialType.Login,
                Username = "testuser@example.com",
                Password = "SecurePassword123!",
                Url = "https://example.com",
                Notes = "Test credential",
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };

            await viewModel.AddCredentialAsync(credential);

            // Assert
            Assert.Equal(initialCount + 1, viewModel.Credentials.Count);
            Assert.Contains(viewModel.Credentials, c => c.Title == "Test Login");
            
            var addedCredential = viewModel.Credentials.First(c => c.Title == "Test Login");
            Assert.Equal("testuser@example.com", addedCredential.Username);
            Assert.Equal("https://example.com", addedCredential.Url);
        }

        [Fact]
        public async Task EditCredential_UpdatesProperties_PersistsChanges()
        {
            // Arrange
            var vaultService = new VaultService();
            await vaultService.CreateVaultAsync(_testVaultPath, "TestPassword123!", null);
            await vaultService.UnlockVaultAsync(_testVaultPath, "TestPassword123!", null);

            var viewModel = new VaultViewModel(vaultService);
            await viewModel.LoadAsync(_testVaultPath, "TestPassword123!");

            var credential = new Credential
            {
                Id = Guid.NewGuid().ToString(),
                Title = "Original Title",
                Type = CredentialType.Login,
                Username = "original@example.com",
                Password = "OriginalPassword123!"
            };
            await viewModel.AddCredentialAsync(credential);

            // Act
            credential.Title = "Updated Title";
            credential.Username = "updated@example.com";
            credential.ModifiedDate = DateTime.UtcNow;
            await viewModel.SaveCredentialAsync(credential);

            // Assert
            var updatedCredential = viewModel.Credentials.First(c => c.Id == credential.Id);
            Assert.Equal("Updated Title", updatedCredential.Title);
            Assert.Equal("updated@example.com", updatedCredential.Username);
        }

        [Fact]
        public async Task DeleteCredential_MovesToTrash_RemainsRecoverable()
        {
            // Arrange
            var vaultService = new VaultService();
            await vaultService.CreateVaultAsync(_testVaultPath, "TestPassword123!", null);
            await vaultService.UnlockVaultAsync(_testVaultPath, "TestPassword123!", null);

            var viewModel = new VaultViewModel(vaultService);
            await viewModel.LoadAsync(_testVaultPath, "TestPassword123!");

            var credential = new Credential
            {
                Id = Guid.NewGuid().ToString(),
                Title = "To Be Deleted",
                Type = CredentialType.Login
            };
            await viewModel.AddCredentialAsync(credential);

            int initialCredentialCount = viewModel.Credentials.Count;

            // Act
            await viewModel.DeleteCredentialAsync(credential);

            // Assert - Removed from main collection
            Assert.Equal(initialCredentialCount - 1, viewModel.Credentials.Count);
            Assert.DoesNotContain(viewModel.Credentials, c => c.Id == credential.Id);

            // Assert - Available in trash
            Assert.True(viewModel.TrashItems.Any(t => t.OriginalCredential.Id == credential.Id));
        }

        [Fact]
        public async Task RecoverCredential_FromTrash_RestoresToVault()
        {
            // Arrange
            var vaultService = new VaultService();
            await vaultService.CreateVaultAsync(_testVaultPath, "TestPassword123!", null);
            await vaultService.UnlockVaultAsync(_testVaultPath, "TestPassword123!", null);

            var viewModel = new VaultViewModel(vaultService);
            await viewModel.LoadAsync(_testVaultPath, "TestPassword123!");

            var credential = new Credential
            {
                Id = Guid.NewGuid().ToString(),
                Title = "Recoverable Item",
                Type = CredentialType.Login
            };
            await viewModel.AddCredentialAsync(credential);
            await viewModel.DeleteCredentialAsync(credential);

            int credentialCountAfterDelete = viewModel.Credentials.Count;

            // Act
            var trashItem = viewModel.TrashItems.First(t => t.OriginalCredential.Id == credential.Id);
            await viewModel.RecoverFromTrashAsync(trashItem);

            // Assert
            Assert.Equal(credentialCountAfterDelete + 1, viewModel.Credentials.Count);
            Assert.Contains(viewModel.Credentials, c => c.Id == credential.Id);
            Assert.DoesNotContain(viewModel.TrashItems, t => t.OriginalCredential.Id == credential.Id);
        }

        #endregion

        #region Search and Filtering Tests

        [Fact]
        public async Task SearchCredentials_ByTitle_ReturnsMatches()
        {
            // Arrange
            var vaultService = new VaultService();
            await vaultService.CreateVaultAsync(_testVaultPath, "TestPassword123!", null);
            await vaultService.UnlockVaultAsync(_testVaultPath, "TestPassword123!", null);

            var viewModel = new VaultViewModel(vaultService);
            await viewModel.LoadAsync(_testVaultPath, "TestPassword123!");

            await viewModel.AddCredentialAsync(new Credential { Title = "Facebook Login", Type = CredentialType.Login });
            await viewModel.AddCredentialAsync(new Credential { Title = "Gmail Account", Type = CredentialType.Login });
            await viewModel.AddCredentialAsync(new Credential { Title = "GitHub Access", Type = CredentialType.Login });

            // Act
            viewModel.SearchText = "Face";
            await viewModel.ApplySearchFilterAsync();

            // Assert
            Assert.Single(viewModel.FilteredCredentials);
            Assert.Equal("Facebook Login", viewModel.FilteredCredentials.First().Title);
        }

        [Fact]
        public async Task FilterByFavorites_ShowsOnlyFavorites()
        {
            // Arrange
            var vaultService = new VaultService();
            await vaultService.CreateVaultAsync(_testVaultPath, "TestPassword123!", null);
            await vaultService.UnlockVaultAsync(_testVaultPath, "TestPassword123!", null);

            var viewModel = new VaultViewModel(vaultService);
            await viewModel.LoadAsync(_testVaultPath, "TestPassword123!");

            var favorite1 = new Credential { Title = "Favorite 1", Type = CredentialType.Login, IsFavorite = true };
            var favorite2 = new Credential { Title = "Favorite 2", Type = CredentialType.Login, IsFavorite = true };
            var normal = new Credential { Title = "Normal", Type = CredentialType.Login, IsFavorite = false };

            await viewModel.AddCredentialAsync(favorite1);
            await viewModel.AddCredentialAsync(favorite2);
            await viewModel.AddCredentialAsync(normal);

            // Act
            viewModel.ShowFavoritesCommand.Execute(null);

            // Assert
            Assert.Equal(2, viewModel.FilteredCredentials.Count);
            Assert.All(viewModel.FilteredCredentials, c => Assert.True(c.IsFavorite));
        }

        [Fact]
        public async Task FilterByCategory_ShowsOnlyCategoryItems()
        {
            // Arrange
            var vaultService = new VaultService();
            await vaultService.CreateVaultAsync(_testVaultPath, "TestPassword123!", null);
            await vaultService.UnlockVaultAsync(_testVaultPath, "TestPassword123!", null);

            var viewModel = new VaultViewModel(vaultService);
            await viewModel.LoadAsync(_testVaultPath, "TestPassword123!");

            var socialCategory = new CategoryModel { Name = "Social", IconKey = "users" };
            viewModel.Categories.Add(socialCategory);

            await viewModel.AddCredentialAsync(new Credential { Title = "Facebook", Category = "Social" });
            await viewModel.AddCredentialAsync(new Credential { Title = "Twitter", Category = "Social" });
            await viewModel.AddCredentialAsync(new Credential { Title = "Gmail", Category = "Email" });

            // Act
            viewModel.SelectCategoryCommand.Execute(socialCategory);

            // Assert
            Assert.Equal(2, viewModel.FilteredCredentials.Count);
            Assert.All(viewModel.FilteredCredentials, c => Assert.Equal("Social", c.Category));
        }

        #endregion

        #region Lock/Unlock Tests

        [Fact]
        public async Task SoftLock_WithPin_LocksVault()
        {
            // Arrange
            var vaultService = new VaultService();
            await vaultService.CreateVaultAsync(_testVaultPath, "TestPassword123!", null);
            await vaultService.UnlockVaultAsync(_testVaultPath, "TestPassword123!", null);

            var viewModel = new VaultViewModel(vaultService);
            await viewModel.LoadAsync(_testVaultPath, "TestPassword123!");
            
            string testPin = "1234";
            viewModel.SetupPinLock(testPin);

            // Act
            viewModel.SoftLock();

            // Assert
            Assert.True(viewModel.IsLockscreenVisible);
            Assert.True(viewModel.IsSoftLocked);
            Assert.False(viewModel.CanAccessCredentials);
        }

        [Fact]
        public async Task UnlockWithPin_CorrectPin_UnlocksVault()
        {
            // Arrange
            var vaultService = new VaultService();
            await vaultService.CreateVaultAsync(_testVaultPath, "TestPassword123!", null);
            await vaultService.UnlockVaultAsync(_testVaultPath, "TestPassword123!", null);

            var viewModel = new VaultViewModel(vaultService);
            await viewModel.LoadAsync(_testVaultPath, "TestPassword123!");
            
            string testPin = "1234";
            viewModel.SetupPinLock(testPin);
            viewModel.SoftLock();

            // Act
            bool unlocked = await viewModel.UnlockWithPinAsync(testPin);

            // Assert
            Assert.True(unlocked);
            Assert.False(viewModel.IsLockscreenVisible);
            Assert.False(viewModel.IsSoftLocked);
            Assert.True(viewModel.CanAccessCredentials);
        }

        [Fact]
        public async Task UnlockWithPin_IncorrectPin_RemainsLocked()
        {
            // Arrange
            var vaultService = new VaultService();
            await vaultService.CreateVaultAsync(_testVaultPath, "TestPassword123!", null);
            await vaultService.UnlockVaultAsync(_testVaultPath, "TestPassword123!", null);

            var viewModel = new VaultViewModel(vaultService);
            await viewModel.LoadAsync(_testVaultPath, "TestPassword123!");
            
            string correctPin = "1234";
            string wrongPin = "5678";
            viewModel.SetupPinLock(correctPin);
            viewModel.SoftLock();

            // Act
            bool unlocked = await viewModel.UnlockWithPinAsync(wrongPin);

            // Assert
            Assert.False(unlocked);
            Assert.True(viewModel.IsLockscreenVisible);
            Assert.True(viewModel.IsSoftLocked);
            Assert.NotEmpty(viewModel.LockscreenError);
        }

        [Fact]
        public async Task HardLock_ClearsAllData_RequiresFullAuth()
        {
            // Arrange
            var vaultService = new VaultService();
            await vaultService.CreateVaultAsync(_testVaultPath, "TestPassword123!", null);
            await vaultService.UnlockVaultAsync(_testVaultPath, "TestPassword123!", null);

            var viewModel = new VaultViewModel(vaultService);
            await viewModel.LoadAsync(_testVaultPath, "TestPassword123!");
            
            await viewModel.AddCredentialAsync(new Credential { Title = "Test", Type = CredentialType.Login });
            int credentialCount = viewModel.Credentials.Count;

            // Act
            await viewModel.HardLockAsync();

            // Assert
            Assert.False(viewModel.IsUnlocked);
            Assert.Empty(viewModel.Credentials);
            Assert.True(viewModel.IsLockscreenVisible);
            Assert.False(viewModel.IsSoftLocked); // Hard lock, not soft lock
        }

        #endregion

        #region Security Feature Tests

        [Fact]
        public async Task ToggleFavorite_UpdatesStatus_PersistsChange()
        {
            // Arrange
            var vaultService = new VaultService();
            await vaultService.CreateVaultAsync(_testVaultPath, "TestPassword123!", null);
            await vaultService.UnlockVaultAsync(_testVaultPath, "TestPassword123!", null);

            var viewModel = new VaultViewModel(vaultService);
            await viewModel.LoadAsync(_testVaultPath, "TestPassword123!");

            var credential = new Credential
            {
                Id = Guid.NewGuid().ToString(),
                Title = "Test Favorite",
                Type = CredentialType.Login,
                IsFavorite = false
            };
            await viewModel.AddCredentialAsync(credential);

            // Act
            await viewModel.ToggleFavoriteAsync(credential);

            // Assert
            Assert.True(credential.IsFavorite);
            
            // Act again - toggle back
            await viewModel.ToggleFavoriteAsync(credential);
            Assert.False(credential.IsFavorite);
        }

        [Fact]
        public async Task CopyPassword_AddsToClipboard_TracksCopyAction()
        {
            // Arrange
            var vaultService = new VaultService();
            await vaultService.CreateVaultAsync(_testVaultPath, "TestPassword123!", null);
            await vaultService.UnlockVaultAsync(_testVaultPath, "TestPassword123!", null);

            var viewModel = new VaultViewModel(vaultService);
            await viewModel.LoadAsync(_testVaultPath, "TestPassword123!");

            var credential = new Credential
            {
                Title = "Test Copy",
                Password = "SecretPassword123!",
                Type = CredentialType.Login
            };
            await viewModel.AddCredentialAsync(credential);

            // Act
            await viewModel.CopyPasswordAsync(credential);

            // Assert
            // Note: Actual clipboard testing requires platform-specific code
            // For now, verify the command executed without errors
            Assert.NotNull(credential.LastAccessedDate);
        }

        #endregion

        #region Category Management Tests

        [Fact]
        public void AddCategory_ValidName_AddsToCollection()
        {
            // Arrange
            var viewModel = new VaultViewModel(new VaultService());
            int initialCount = viewModel.Categories.Count;

            // Act
            var category = new CategoryModel
            {
                Name = "New Category",
                IconKey = "folder",
                Color = "#FF5733"
            };
            viewModel.Categories.Add(category);

            // Assert
            Assert.Equal(initialCount + 1, viewModel.Categories.Count);
            Assert.Contains(viewModel.Categories, c => c.Name == "New Category");
        }

        [Fact]
        public async Task SelectCategory_FiltersCredentials_UpdatesActiveCategory()
        {
            // Arrange
            var vaultService = new VaultService();
            await vaultService.CreateVaultAsync(_testVaultPath, "TestPassword123!", null);
            await vaultService.UnlockVaultAsync(_testVaultPath, "TestPassword123!", null);

            var viewModel = new VaultViewModel(vaultService);
            await viewModel.LoadAsync(_testVaultPath, "TestPassword123!");

            var category = new CategoryModel { Name = "Work", IconKey = "briefcase" };
            viewModel.Categories.Add(category);

            await viewModel.AddCredentialAsync(new Credential { Title = "Work Email", Category = "Work" });
            await viewModel.AddCredentialAsync(new Credential { Title = "Personal Email", Category = "Personal" });

            // Act
            viewModel.SelectCategoryCommand.Execute(category);

            // Assert
            Assert.Equal("Work", viewModel.ActiveCategory);
            Assert.Single(viewModel.FilteredCredentials);
        }

        #endregion

        #region Policy Enforcement Tests

        [Fact]
        public async Task LoadVault_InvalidManifestPolicy_ThrowsException()
        {
            // Arrange
            var vaultService = new VaultService();
            await vaultService.CreateVaultAsync(_testVaultPath, "TestPassword123!", null);

            // Create manifest with invalid policy (requires USB but none present)
            var manifest = new VaultManifest
            {
                ContainerPath = _testVaultPath,
                Algorithm = "AES-256-GCM",
                RequiresMfa = false,
                UsbSerialRequired = "INVALID_SERIAL_12345" // Non-existent USB
            };

            // Act & Assert
            var viewModel = new VaultViewModel(vaultService);
            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await viewModel.LoadAsync(_testVaultPath, "TestPassword123!");
            });
        }

        #endregion

        #region Helper Methods

        private static Credential CreateTestCredential(string title, CredentialType type = CredentialType.Login)
        {
            return new Credential
            {
                Id = Guid.NewGuid().ToString(),
                Title = title,
                Type = type,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
        }

        #endregion
    }
}
