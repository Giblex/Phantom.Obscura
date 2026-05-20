#nullable enable

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public sealed class PhantomKeyBridgeValidatorTests
    {
        [Fact]
        public void Validate_Succeeds_ForCompleteBridgeContract()
        {
            var fixture = new BridgeFixture();
            try
            {
                fixture.WriteBridgeArtifacts();

                var validator = new PhantomKeyBridgeValidator(fixture.ArtifactProtectionService);
                validator.Validate(fixture.Manifest, fixture.Passphrase, null);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Fact]
        public void Validate_Throws_WhenBridgeReceiptMissing()
        {
            var fixture = new BridgeFixture();
            try
            {
                fixture.WriteBridgeArtifacts();
                File.Delete(fixture.Manifest.PhantomKeyBridgeReceiptPath!);

                var validator = new PhantomKeyBridgeValidator(fixture.ArtifactProtectionService);
                var ex = Assert.Throws<FileNotFoundException>(() =>
                    validator.Validate(fixture.Manifest, fixture.Passphrase, null));

                Assert.Contains("receipt", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Fact]
        public void IntegritySignature_Changes_WhenBridgeFieldsChange()
        {
            byte[] key = new byte[32];
            RandomNumberGenerator.Fill(key);

            var manifest = new VaultManifest
            {
                ContainerPath = "vaults/vault.pvault",
                SaltBase64 = Convert.ToBase64String(new byte[16]),
                PhantomKeyBridgeEnabled = true,
                PhantomKeyBridgeWorkspacePath = PhantomKeyBridgeContract.WorkspaceRelativePath,
                PhantomKeyBridgeReceiptPath = PhantomKeyBridgeContract.BridgeReceiptRelativePath,
                PhantomKeyBridgePolicyPath = PhantomKeyBridgeContract.PolicyRelativePath,
                PhantomKeyBridgeContinuityPath = PhantomKeyBridgeContract.ContinuityRelativePath
            };

            try
            {
                string before = ManifestService.ComputeIntegritySignature(manifest, key);
                manifest.PhantomKeyBridgeReceiptPath = "root/altered.bridge.pmeta";
                string after = ManifestService.ComputeIntegritySignature(manifest, key);

                Assert.NotEqual(before, after);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        private sealed class BridgeFixture : IDisposable
        {
            private readonly string _rootPath;
            private readonly string _workspacePath;
            private readonly string _bindingId;

            public BridgeFixture()
            {
                EncryptionService = new EncryptionService();
                ArtifactProtectionService = new UsbArtifactProtectionService(EncryptionService);
                Passphrase = "Bridge-Test-Passphrase-123!";
                _bindingId = Guid.NewGuid().ToString("N");
                _rootPath = Path.Combine(Path.GetTempPath(), "PhantomKeyBridgeTests", Guid.NewGuid().ToString("N"));
                _workspacePath = Path.Combine(_rootPath, "vaults", "phantomkey");
                Directory.CreateDirectory(Path.Combine(_rootPath, "root"));
                Directory.CreateDirectory(Path.Combine(_rootPath, "vaults"));
                Directory.CreateDirectory(Path.Combine(_rootPath, "objects"));
                Directory.CreateDirectory(Path.Combine(_rootPath, "recovery", "vault"));
                Directory.CreateDirectory(_workspacePath);

                Manifest = new VaultManifest
                {
                    VaultName = "BridgeTestVault",
                    SaltBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("0123456789ABCDEF0123456789ABCDEF")),
                    UsbBindingId = _bindingId,
                    UsbBindingGuid = Guid.NewGuid().ToString("D"),
                    DeviceId = "DEVICE-TEST-01",
                    Guuid = "A1B2C3D4-E5F6-47A8-99AA-BBCCDDEEFF00",
                    ContainerPath = Path.Combine(_rootPath, "vaults", "vault.pvault"),
                    RootContainerPath = Path.Combine(_rootPath, "root", "root.pvault"),
                    ObjectContainerPath = Path.Combine(_rootPath, "objects", "objects.pvault"),
                    BindingRecordPath = Path.Combine(_rootPath, "root", "usb.binding.pmeta"),
                    ProvisioningRecordPath = Path.Combine(_rootPath, "root", "storage-tier.provisioning.pmeta"),
                    RecoveryContainerPath = Path.Combine(_rootPath, "recovery", "recovery.pvault"),
                    PhantomKeyBridgeEnabled = true,
                    PhantomKeyBridgeWorkspacePath = _workspacePath,
                    PhantomKeyBridgeManifestPath = Path.Combine(_rootPath, PhantomKeyBridgeContract.BridgeManifestRelativePath.Replace('/', Path.DirectorySeparatorChar)),
                    PhantomKeyBridgeContinuityPath = Path.Combine(_rootPath, PhantomKeyBridgeContract.ContinuityRelativePath.Replace('/', Path.DirectorySeparatorChar)),
                    PhantomKeyBridgePolicyPath = Path.Combine(_rootPath, PhantomKeyBridgeContract.PolicyRelativePath.Replace('/', Path.DirectorySeparatorChar)),
                    PhantomKeyBridgeConsumerMapPath = Path.Combine(_rootPath, PhantomKeyBridgeContract.ConsumerMapRelativePath.Replace('/', Path.DirectorySeparatorChar)),
                    PhantomKeyBridgeAuditLogPath = Path.Combine(_rootPath, PhantomKeyBridgeContract.AuditLogRelativePath.Replace('/', Path.DirectorySeparatorChar)),
                    PhantomKeyBridgeReceiptPath = Path.Combine(_rootPath, PhantomKeyBridgeContract.BridgeReceiptRelativePath.Replace('/', Path.DirectorySeparatorChar))
                };

                File.WriteAllText(Manifest.BindingRecordPath, "binding");
                File.WriteAllText(Manifest.ProvisioningRecordPath!, "provisioning");
                File.WriteAllText(Manifest.ContainerPath, "vault");
                File.WriteAllText(Manifest.RootContainerPath!, "root");
                File.WriteAllText(Manifest.ObjectContainerPath!, "objects");
                File.WriteAllText(Manifest.RecoveryContainerPath!, "recovery");
            }

            public EncryptionService EncryptionService { get; }
            public UsbArtifactProtectionService ArtifactProtectionService { get; }
            public VaultManifest Manifest { get; }
            public string Passphrase { get; }

            public void WriteBridgeArtifacts()
            {
                File.WriteAllText(
                    Manifest.PhantomKeyBridgeManifestPath!,
                    JsonSerializer.Serialize(new PhantomKeyBridgeManifestDocument
                    {
                        CreatedUtc = DateTimeOffset.UtcNow,
                        BridgeModel = PhantomKeyBridgeContract.BridgeManifestModel,
                        OwnerApp = PhantomKeyBridgeContract.ObscuraOwnerApp,
                        Consumers = PhantomKeyBridgeContract.DefaultConsumers,
                        WorkspacePath = PhantomKeyBridgeContract.WorkspaceRelativePath,
                        Notes = "test"
                    }));

                File.WriteAllText(Manifest.PhantomKeyBridgeAuditLogPath!, string.Empty);

                string bindingDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_bindingId)));

                ArtifactProtectionService.WriteEncryptedJsonAsync(
                    Manifest.PhantomKeyBridgeContinuityPath!,
                    new PhantomKeyContinuityDocument
                    {
                        CreatedUtc = DateTimeOffset.UtcNow,
                        VaultName = Manifest.VaultName,
                        ProtectionTier = Manifest.ProtectionTier.ToString(),
                        EffectiveTransport = Manifest.EffectiveStorageTransport.ToString(),
                        BridgeWorkspacePath = PhantomKeyBridgeContract.WorkspaceRelativePath,
                        Consumers = PhantomKeyBridgeContract.DefaultConsumers,
                        BindingDigest = bindingDigest,
                        RequiresPasskeyBridge = false,
                        Notes = "test"
                    },
                    Manifest,
                    Passphrase,
                    null,
                    PhantomKeyBridgeContract.ContinuityPurpose).GetAwaiter().GetResult();

                ArtifactProtectionService.WriteEncryptedJsonAsync(
                    Manifest.PhantomKeyBridgePolicyPath!,
                    new PhantomKeyPolicyWorkspaceDocument
                    {
                        CreatedUtc = DateTimeOffset.UtcNow,
                        OwnerApp = PhantomKeyBridgeContract.ObscuraOwnerApp,
                        StorageBoundary = "filesystem-sibling-workspace",
                        PrivateMaterialExportAllowed = false,
                        RequiresBridgeMediation = true,
                        AllowedConsumers = PhantomKeyBridgeContract.DefaultConsumers,
                        AllowedRecordClasses = PhantomKeyBridgeContract.DefaultRecordClasses,
                        Notes = "test"
                    },
                    Manifest,
                    Passphrase,
                    null,
                    PhantomKeyBridgeContract.PolicyPurpose).GetAwaiter().GetResult();

                ArtifactProtectionService.WriteEncryptedJsonAsync(
                    Manifest.PhantomKeyBridgeConsumerMapPath!,
                    new PhantomKeyConsumerMapDocument
                    {
                        CreatedUtc = DateTimeOffset.UtcNow,
                        OwnerApp = PhantomKeyBridgeContract.ObscuraOwnerApp,
                        WorkspacePath = PhantomKeyBridgeContract.WorkspaceRelativePath,
                        ObscuraBindingRecordPath = "root/usb.binding.pmeta",
                        ObscuraProvisioningRecordPath = "root/storage-tier.provisioning.pmeta",
                        RecoveryWorkspacePath = "recovery/vault",
                        ConsumerApps = PhantomKeyBridgeContract.DefaultConsumers,
                        Notes = "test"
                    },
                    Manifest,
                    Passphrase,
                    null,
                    PhantomKeyBridgeContract.ConsumerMapPurpose).GetAwaiter().GetResult();

                ArtifactProtectionService.WriteEncryptedJsonAsync(
                    Manifest.PhantomKeyBridgeReceiptPath!,
                    new PhantomKeyBridgeReceiptDocument
                    {
                        CreatedUtc = DateTimeOffset.UtcNow,
                        WorkspacePath = PhantomKeyBridgeContract.WorkspaceRelativePath,
                        ManifestPath = PhantomKeyBridgeContract.BridgeManifestRelativePath,
                        ContinuityPath = PhantomKeyBridgeContract.ContinuityRelativePath,
                        PolicyPath = PhantomKeyBridgeContract.PolicyRelativePath,
                        ConsumerMapPath = PhantomKeyBridgeContract.ConsumerMapRelativePath,
                        AuditLogPath = PhantomKeyBridgeContract.AuditLogRelativePath,
                        StorageBoundary = PhantomKeyBridgeContract.BridgeManifestModel,
                        PrivateMaterialExportAllowed = false,
                        Notes = "test"
                    },
                    Manifest,
                    Passphrase,
                    null,
                    PhantomKeyBridgeContract.BridgeReceiptPurpose).GetAwaiter().GetResult();
            }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(_rootPath))
                    {
                        Directory.Delete(_rootPath, true);
                    }
                }
                catch
                {
                }
            }
        }
    }
}
