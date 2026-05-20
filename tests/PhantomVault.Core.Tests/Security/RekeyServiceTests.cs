using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.Security;
using PhantomVault.Core.Utils;
using Xunit;

namespace PhantomVault.Core.Tests.Security
{
    public sealed class RekeyServiceTests : IDisposable
    {
        private readonly string _tempDirectory;

        public RekeyServiceTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "phantom-rekey-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        [Fact]
        public async Task PerformRekeyAsync_ReencryptsContainerAndManifest_WithNewCredentialsOnly()
        {
            var encryptionService = new EncryptionService();
            using var containerService = new PhantomContainerService(encryptionService);
            var manifestService = new ManifestService(encryptionService);
            var keyfileGenerator = new KeyfileGeneratorService();
            var rekeyService = new RekeyService(
                encryptionService,
                manifestService,
                new LayeredEncryptionService(encryptionService),
                keyfileGenerator);

            string oldPassword = "OldPassword!123456";
            string newPassword = "NewPassword!654321";
            string containerPath = Path.Combine(_tempDirectory, "vault.pcv");
            string manifestPath = Path.Combine(_tempDirectory, "vault.manifest");
            string oldKeyfilePath = Path.Combine(_tempDirectory, "vault.old.key");
            string newKeyfilePath = Path.Combine(_tempDirectory, "vault.new.key");
            byte[] payload = Encoding.UTF8.GetBytes("sensitive payload survives streaming rekey");

            keyfileGenerator.GenerateKeyfile(oldKeyfilePath, 64);
            await using (var payloadStream = new MemoryStream(payload))
            {
                await containerService.CreateContainerFromStreamAsync(
                    containerPath,
                    payloadStream,
                    payload.Length,
                    oldPassword,
                    oldKeyfilePath,
                    manifest: null);
            }

            var manifest = new VaultManifest
            {
                VaultName = "Rekey Test",
                ContainerPath = "vault.pcv",
                KeyfilePath = oldKeyfilePath,
                LastKeyRotation = DateTimeOffset.UtcNow.AddDays(-90),
                KeyRotationPending = true
            };
            using (var secureOldPassword = SecurePassword.FromString(oldPassword))
            {
                manifestService.WriteManifestSecure(manifest, manifestPath, secureOldPassword, oldKeyfilePath);
            }

            var result = await rekeyService.PerformRekeyAsync(
                containerPath,
                manifestPath,
                oldKeyfilePath,
                oldPassword,
                newPassword,
                newKeyfilePath,
                usbSerial: null);

            Assert.True(result.Success, result.Error?.ToString());
            Assert.Equal(newKeyfilePath, result.NewKeyfilePath);
            Assert.True(File.Exists(newKeyfilePath));

            VaultManifest rotatedManifest;
            using (var secureNewPassword = SecurePassword.FromString(newPassword))
            {
                rotatedManifest = manifestService.ReadManifestSecure(manifestPath, secureNewPassword, newKeyfilePath);
            }
            Assert.Equal(newKeyfilePath, rotatedManifest.KeyfilePath);
            Assert.False(rotatedManifest.KeyRotationPending);
            Assert.True(rotatedManifest.KeyRotationCount > 0);

            await using var decrypted = new MemoryStream();
            await containerService.OpenContainerToStreamAsync(
                containerPath,
                decrypted,
                newPassword,
                newKeyfilePath);

            Assert.Equal(payload, decrypted.ToArray());

            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await using var oldCredentialStream = new MemoryStream();
                await containerService.OpenContainerToStreamAsync(
                    containerPath,
                    oldCredentialStream,
                    oldPassword,
                    oldKeyfilePath);
            });
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDirectory))
                {
                    foreach (var path in Directory.EnumerateFiles(_tempDirectory, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
                    }

                    Directory.Delete(_tempDirectory, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup for locked antivirus-scanned test artifacts.
            }
        }
    }
}
