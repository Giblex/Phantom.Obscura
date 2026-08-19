using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.UI.Services;
using Xunit;

namespace PhantomVault.UI.Tests;

public sealed class ObscuraCredentialIndexServiceTests
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Phantom.Obscura/SuiteCredentialIndex/v2");

    [Fact]
    public async Task ExportAsync_SealsMetadataAndRemovesLegacyPlaintext()
    {
        var mount = Path.Combine(Path.GetTempPath(), $"obscura-index-{Guid.NewGuid():N}");
        var service = new ObscuraCredentialIndexService();
        var legacy = Path.Combine(mount, "vaults", "obscura-search-index.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
            await File.WriteAllTextAsync(legacy, "legacy plaintext");
            using var credential = new Credential
            {
                Title = "Private Portal",
                Username = "private@example.test",
                Url = "https://private.example.test",
            };

            await service.ExportAsync(mount, "Test Vault", new[] { credential });

            var path = service.GetIndexPath(mount);
            Assert.EndsWith(".pidx", path, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(legacy));

            var sealedBytes = await File.ReadAllBytesAsync(path);
            Assert.DoesNotContain("Private Portal", Encoding.UTF8.GetString(sealedBytes), StringComparison.Ordinal);

            var plain = ProtectedData.Unprotect(sealedBytes, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                var index = JsonSerializer.Deserialize<ObscuraCredentialIndex>(plain);
                Assert.Single(index!.Entries);
                Assert.Equal("Private Portal", index.Entries[0].Title);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
                CryptographicOperations.ZeroMemory(sealedBytes);
            }
        }
        finally
        {
            if (Directory.Exists(mount))
                Directory.Delete(mount, recursive: true);
        }
    }

    [Fact]
    public async Task Delete_RemovesEncryptedLegacyAndTemporaryIndexes()
    {
        var mount = Path.Combine(Path.GetTempPath(), $"obscura-index-{Guid.NewGuid():N}");
        var vaults = Path.Combine(mount, "vaults");
        var service = new ObscuraCredentialIndexService();
        try
        {
            Directory.CreateDirectory(vaults);
            foreach (var name in new[] { "obscura-search-index.pidx", "obscura-search-index.json", "obscura-search-index.pidx.tmp" })
                await File.WriteAllTextAsync(Path.Combine(vaults, name), "probe");

            service.Delete(mount);

            Assert.Empty(Directory.GetFiles(vaults));
        }
        finally
        {
            if (Directory.Exists(mount))
                Directory.Delete(mount, recursive: true);
        }
    }
}
