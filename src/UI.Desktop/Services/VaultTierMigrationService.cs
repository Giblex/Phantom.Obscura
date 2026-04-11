using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.Services
{
    public sealed record VaultTierMigrationContext(
        string ManifestPath,
        string Password,
        string? KeyfilePath,
        string ActiveDevicePath,
        string TransportLayoutRoot,
        VaultProtectionTier ProtectionTier,
        VaultStorageTransport EffectiveStorageTransport,
        VaultStorageTransport? RequestedStorageTransport,
        string? MasterVolumePath);

    public sealed record VaultTierMigrationPlan(
        bool CanProceed,
        VaultProtectionTier CurrentTier,
        VaultStorageTransport CurrentTransport,
        VaultProtectionTier TargetTier,
        VaultStorageTransport TargetTransport,
        string Summary,
        string? ActiveDevicePath,
        string? TargetFilesystemRoot,
        string? RollbackDirectory);

    public sealed record VaultTierMigrationResult(
        VaultProtectionTier ProtectionTier,
        VaultStorageTransport EffectiveStorageTransport,
        VaultStorageTransport? RequestedStorageTransport,
        string ActiveDevicePath,
        string? MasterVolumePath,
        string TransportLayoutRoot,
        string RollbackDirectory,
        string StatusMessage);

    public sealed class VaultTierMigrationService
    {
        private const string RootContainerRelativePath = "root/root.pvault";
        private const string VaultContainerRelativePath = "vaults/vault.pvault";
        private const string ObjectContainerRelativePath = "objects/objects.pvault";
        private const string RecoveryContainerRelativePath = "recovery/recovery.pvault";
        private const string BindingRecordRelativePath = "root/usb.binding.pmeta";
        private const string RecoveryRecordRelativePath = "recovery/recovery.record.pmeta";
        private const string ProvisioningRecordRelativePath = "root/storage-tier.provisioning.pmeta";

        private readonly ManifestService _manifestService;
        private readonly UsbArtifactProtectionService _usbArtifactProtectionService;
        private readonly UsbBindingService _usbBindingService;
        private readonly BlackSecureRawVolumeService _blackSecureRawVolumeService;
        private readonly ObscuraVolumeService _obscuraVolumeService;

        public VaultTierMigrationService(
            ManifestService manifestService,
            UsbArtifactProtectionService usbArtifactProtectionService,
            UsbBindingService usbBindingService,
            BlackSecureRawVolumeService blackSecureRawVolumeService,
            ObscuraVolumeService obscuraVolumeService)
        {
            _manifestService = manifestService ?? throw new ArgumentNullException(nameof(manifestService));
            _usbArtifactProtectionService = usbArtifactProtectionService ?? throw new ArgumentNullException(nameof(usbArtifactProtectionService));
            _usbBindingService = usbBindingService ?? throw new ArgumentNullException(nameof(usbBindingService));
            _blackSecureRawVolumeService = blackSecureRawVolumeService ?? throw new ArgumentNullException(nameof(blackSecureRawVolumeService));
            _obscuraVolumeService = obscuraVolumeService ?? throw new ArgumentNullException(nameof(obscuraVolumeService));
        }

        public VaultTierMigrationPlan ValidateMigration(
            VaultTierMigrationContext context,
            VaultProtectionTier targetTier,
            string? targetFilesystemRoot = null)
        {
            var currentManifest = _manifestService.ReadManifest(context.ManifestPath, context.Password, context.KeyfilePath);
            var warnings = new List<string>();

            if (!currentManifest.SupportsReversibleTierMigration)
                warnings.Add("This vault does not advertise reversible tier migration metadata.");

            if (!Directory.Exists(context.TransportLayoutRoot))
                warnings.Add("The current transport layout root is missing.");

            foreach (var requiredRelativePath in GetCanonicalPaths(TryReadProvisioningRecord(currentManifest, context) ?? new StorageTierProvisioningRecord()))
            {
                string absolutePath = Path.Combine(context.TransportLayoutRoot, requiredRelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(absolutePath))
                    warnings.Add($"Required vault artifact missing: {requiredRelativePath}");
            }

            VaultStorageTransport targetTransport = targetTier switch
            {
                VaultProtectionTier.BlackSecure => VaultStorageTransport.RawDevice,
                VaultProtectionTier.StealthSecure => VaultStorageTransport.PackedVolume,
                _ => VaultStorageTransport.FileSystem
            };

            string? activeDevicePath = null;
            if (targetTier == VaultProtectionTier.BlackSecure)
            {
                activeDevicePath = context.ActiveDevicePath.StartsWith(@"\\.\PHYSICALDRIVE", StringComparison.OrdinalIgnoreCase)
                    ? context.ActiveDevicePath
                    : ResolvePhysicalDrivePath(context.ActiveDevicePath);

                if (string.IsNullOrWhiteSpace(activeDevicePath))
                    warnings.Add("Unable to resolve the current vault USB to a physical device for Black Secure migration.");
            }
            else if (targetTier == VaultProtectionTier.StealthSecure)
            {
                if (string.IsNullOrWhiteSpace(targetFilesystemRoot))
                {
                    warnings.Add("Select a mounted removable drive as the Stealth Secure target.");
                }
                else
                {
                    targetFilesystemRoot = NormalizeDriveRoot(targetFilesystemRoot);
                    if (!Directory.Exists(targetFilesystemRoot))
                        warnings.Add("The selected Stealth Secure target path is not available.");
                    activeDevicePath = targetFilesystemRoot;
                }
            }

            string rollbackDirectory = PrepareRollbackDirectoryPath();
            string summary = warnings.Count == 0
                ? $"Ready to migrate from {context.ProtectionTier} ({context.EffectiveStorageTransport}) to {targetTier} ({targetTransport}). A rollback snapshot will be created at {rollbackDirectory}."
                : string.Join(Environment.NewLine, warnings);

            return new VaultTierMigrationPlan(
                warnings.Count == 0 && targetTier != context.ProtectionTier,
                context.ProtectionTier,
                context.EffectiveStorageTransport,
                targetTier,
                targetTransport,
                summary,
                activeDevicePath,
                targetFilesystemRoot,
                rollbackDirectory);
        }

        public async Task<VaultTierMigrationResult> MigrateAsync(
            VaultTierMigrationContext context,
            VaultProtectionTier targetTier,
            string? targetFilesystemRoot = null,
            CancellationToken cancellationToken = default)
        {
            var plan = ValidateMigration(context, targetTier, targetFilesystemRoot);
            if (!plan.CanProceed)
                throw new InvalidOperationException(plan.Summary);

            var manifest = _manifestService.ReadManifest(context.ManifestPath, context.Password, context.KeyfilePath);
            var provisioningRecord = TryReadProvisioningRecord(manifest, context) ?? new StorageTierProvisioningRecord();

            string rollbackDirectory = plan.RollbackDirectory!;
            Directory.CreateDirectory(rollbackDirectory);
            await CreateRollbackSnapshotAsync(context, rollbackDirectory, cancellationToken).ConfigureAwait(false);

            string targetDevicePath = plan.ActiveDevicePath!;
            UpdateManifestForTarget(manifest, provisioningRecord, targetTier, plan.TargetTransport);
            UpdateBindingArtifacts(manifest, provisioningRecord, context, targetDevicePath);
            UpdateRecoveryArtifacts(manifest, context, targetTier, plan.TargetTransport);
            WriteProvisioningRecord(manifest, provisioningRecord, context, targetTier, plan.TargetTransport, targetDevicePath);

            _manifestService.WriteManifest(manifest, context.ManifestPath, context.Password, context.KeyfilePath);

            string? masterVolumePath = null;
            if (targetTier == VaultProtectionTier.BlackSecure)
            {
                await _blackSecureRawVolumeService.CreateVolumeFromDirectoryAsync(targetDevicePath, context.TransportLayoutRoot, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                string targetRoot = NormalizeDriveRoot(plan.TargetFilesystemRoot!);
                masterVolumePath = Path.Combine(targetRoot, "system.bin");
                await _obscuraVolumeService.CreateVolumeFromDirectoryAsync(masterVolumePath, context.TransportLayoutRoot, cancellationToken).ConfigureAwait(false);
            }

            return new VaultTierMigrationResult(
                targetTier,
                plan.TargetTransport,
                targetTier == VaultProtectionTier.BlackSecure ? VaultStorageTransport.RawDevice : null,
                targetDevicePath,
                masterVolumePath,
                context.TransportLayoutRoot,
                rollbackDirectory,
                $"Vault migrated to {targetTier}. Rollback snapshot saved to {rollbackDirectory}");
        }

        private async Task CreateRollbackSnapshotAsync(
            VaultTierMigrationContext context,
            string rollbackDirectory,
            CancellationToken cancellationToken)
        {
            string metadataPath = Path.Combine(rollbackDirectory, "migration.json");
            string artifactPath;

            if (context.EffectiveStorageTransport == VaultStorageTransport.PackedVolume &&
                !string.IsNullOrWhiteSpace(context.MasterVolumePath) &&
                File.Exists(context.MasterVolumePath))
            {
                artifactPath = Path.Combine(rollbackDirectory, "rollback-system.bin");
                File.Copy(context.MasterVolumePath, artifactPath, overwrite: true);
            }
            else
            {
                artifactPath = Path.Combine(rollbackDirectory, "rollback.obscura");
                await _obscuraVolumeService.CreateVolumeFromDirectoryAsync(artifactPath, context.TransportLayoutRoot, cancellationToken).ConfigureAwait(false);
            }

            var metadata = new
            {
                createdUtc = DateTimeOffset.UtcNow,
                sourceTier = context.ProtectionTier.ToString(),
                sourceTransport = context.EffectiveStorageTransport.ToString(),
                sourceDevicePath = context.ActiveDevicePath,
                artifactPath
            };

            await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), cancellationToken).ConfigureAwait(false);
        }

        private void UpdateManifestForTarget(
            VaultManifest manifest,
            StorageTierProvisioningRecord provisioningRecord,
            VaultProtectionTier targetTier,
            VaultStorageTransport targetTransport)
        {
            manifest.ProtectionTier = targetTier;
            manifest.EffectiveStorageTransport = targetTransport;
            manifest.RequestedStorageTransport = targetTier == VaultProtectionTier.BlackSecure ? VaultStorageTransport.RawDevice : null;
            manifest.MasterVolumePath = targetTransport == VaultStorageTransport.PackedVolume ? "system.bin" : null;
            manifest.RootContainerPath = NormalizeCanonicalPath(provisioningRecord.CurrentRootContainerPath, RootContainerRelativePath);
            manifest.ContainerPath = NormalizeCanonicalPath(provisioningRecord.CurrentVaultContainerPath, VaultContainerRelativePath);
            manifest.ObjectContainerPath = NormalizeCanonicalPath(provisioningRecord.CurrentObjectContainerPath, ObjectContainerRelativePath);
            manifest.RecoveryContainerPath = NormalizeCanonicalPath(provisioningRecord.CurrentRecoveryContainerPath, RecoveryContainerRelativePath);
            manifest.BindingRecordPath = NormalizeCanonicalPath(manifest.BindingRecordPath, BindingRecordRelativePath);
            manifest.RecoveryRecordPath = NormalizeCanonicalPath(manifest.RecoveryRecordPath, RecoveryRecordRelativePath);
            manifest.ProvisioningRecordPath = NormalizeCanonicalPath(manifest.ProvisioningRecordPath, ProvisioningRecordRelativePath);
        }

        private void UpdateBindingArtifacts(
            VaultManifest manifest,
            StorageTierProvisioningRecord provisioningRecord,
            VaultTierMigrationContext context,
            string targetDevicePath)
        {
            string targetDeviceId = _usbBindingService.ComputeDeviceId(targetDevicePath);
            manifest.DeviceId = targetDeviceId;

            var bindingRecord = TryReadBindingRecord(manifest, context) ?? new UsbBindingRecord
            {
                BindingId = manifest.UsbBindingId ?? Guid.NewGuid().ToString("N"),
                BindingGuid = manifest.UsbBindingGuid ?? Guid.NewGuid().ToString("D")
            };

            bindingRecord.DeviceId = targetDeviceId;
            bindingRecord.Guuid = manifest.Guuid;
            bindingRecord.DriveRoot = targetDevicePath;
            bindingRecord.RootContainerPath = NormalizeCanonicalPath(provisioningRecord.CurrentRootContainerPath, RootContainerRelativePath);
            bindingRecord.VaultContainerPath = NormalizeCanonicalPath(provisioningRecord.CurrentVaultContainerPath, VaultContainerRelativePath);
            bindingRecord.ObjectContainerPath = NormalizeCanonicalPath(provisioningRecord.CurrentObjectContainerPath, ObjectContainerRelativePath);

            string bindingRecordPath = Path.Combine(context.TransportLayoutRoot, NormalizeCanonicalPath(manifest.BindingRecordPath, BindingRecordRelativePath).Replace('/', Path.DirectorySeparatorChar));
            _usbArtifactProtectionService.WriteEncryptedJsonAsync(
                bindingRecordPath,
                bindingRecord,
                manifest,
                context.Password,
                context.KeyfilePath,
                "usb-binding").GetAwaiter().GetResult();
        }

        private void UpdateRecoveryArtifacts(
            VaultManifest manifest,
            VaultTierMigrationContext context,
            VaultProtectionTier targetTier,
            VaultStorageTransport targetTransport)
        {
            if (string.IsNullOrWhiteSpace(manifest.RecoveryRecordPath))
                return;

            var recoveryRecord = TryReadRecoveryRecord(manifest, context);
            if (recoveryRecord == null)
                return;

            recoveryRecord.ProtectionTier = targetTier;
            recoveryRecord.EffectiveStorageTransport = targetTransport;
            recoveryRecord.RecoveryContainerPath = NormalizeCanonicalPath(recoveryRecord.RecoveryContainerPath, RecoveryContainerRelativePath);

            string recoveryRecordPath = Path.Combine(context.TransportLayoutRoot, NormalizeCanonicalPath(manifest.RecoveryRecordPath, RecoveryRecordRelativePath).Replace('/', Path.DirectorySeparatorChar));
            _usbArtifactProtectionService.WriteEncryptedJsonAsync(
                recoveryRecordPath,
                recoveryRecord,
                manifest,
                context.Password,
                context.KeyfilePath,
                "recovery-record").GetAwaiter().GetResult();
        }

        private void WriteProvisioningRecord(
            VaultManifest manifest,
            StorageTierProvisioningRecord provisioningRecord,
            VaultTierMigrationContext context,
            VaultProtectionTier targetTier,
            VaultStorageTransport targetTransport,
            string targetDevicePath)
        {
            provisioningRecord.ProtectionTier = targetTier;
            provisioningRecord.EffectiveTransport = targetTransport;
            provisioningRecord.RequestedTransport = targetTier == VaultProtectionTier.BlackSecure ? VaultStorageTransport.RawDevice : null;
            provisioningRecord.CurrentRootContainerPath = NormalizeCanonicalPath(provisioningRecord.CurrentRootContainerPath, RootContainerRelativePath);
            provisioningRecord.CurrentVaultContainerPath = NormalizeCanonicalPath(provisioningRecord.CurrentVaultContainerPath, VaultContainerRelativePath);
            provisioningRecord.CurrentObjectContainerPath = NormalizeCanonicalPath(provisioningRecord.CurrentObjectContainerPath, ObjectContainerRelativePath);
            provisioningRecord.CurrentRecoveryContainerPath = NormalizeCanonicalPath(provisioningRecord.CurrentRecoveryContainerPath, RecoveryContainerRelativePath);
            provisioningRecord.CurrentMasterVolumePath = targetTransport == VaultStorageTransport.PackedVolume ? "system.bin" : null;
            provisioningRecord.RecommendedRollbackTransport = targetTier == VaultProtectionTier.BlackSecure
                ? VaultStorageTransport.PackedVolume
                : VaultStorageTransport.RawDevice;
            provisioningRecord.Notes = $"Migrated on {DateTimeOffset.UtcNow:u} to {targetTier} using {targetDevicePath}.";

            string provisioningRecordPath = Path.Combine(context.TransportLayoutRoot, NormalizeCanonicalPath(manifest.ProvisioningRecordPath, ProvisioningRecordRelativePath).Replace('/', Path.DirectorySeparatorChar));
            _usbArtifactProtectionService.WriteEncryptedJsonAsync(
                provisioningRecordPath,
                provisioningRecord,
                manifest,
                context.Password,
                context.KeyfilePath,
                "storage-tier-provisioning").GetAwaiter().GetResult();
        }

        private StorageTierProvisioningRecord? TryReadProvisioningRecord(VaultManifest manifest, VaultTierMigrationContext context)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(manifest.ProvisioningRecordPath))
                    return null;

                string path = Path.Combine(context.TransportLayoutRoot, manifest.ProvisioningRecordPath.Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(path)
                    ? _usbArtifactProtectionService.ReadEncryptedJson<StorageTierProvisioningRecord>(path, manifest, context.Password, context.KeyfilePath, "storage-tier-provisioning")
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private UsbBindingRecord? TryReadBindingRecord(VaultManifest manifest, VaultTierMigrationContext context)
        {
            try
            {
                string path = Path.Combine(context.TransportLayoutRoot, NormalizeCanonicalPath(manifest.BindingRecordPath, BindingRecordRelativePath).Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(path)
                    ? _usbArtifactProtectionService.ReadEncryptedJson<UsbBindingRecord>(path, manifest, context.Password, context.KeyfilePath, "usb-binding")
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private RecoveryVaultRecord? TryReadRecoveryRecord(VaultManifest manifest, VaultTierMigrationContext context)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(manifest.RecoveryRecordPath))
                    return null;

                string path = Path.Combine(context.TransportLayoutRoot, NormalizeCanonicalPath(manifest.RecoveryRecordPath, RecoveryRecordRelativePath).Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(path)
                    ? _usbArtifactProtectionService.ReadEncryptedJson<RecoveryVaultRecord>(path, manifest, context.Password, context.KeyfilePath, "recovery-record")
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<string> GetCanonicalPaths(StorageTierProvisioningRecord record)
        {
            yield return NormalizeCanonicalPath(record.CurrentRootContainerPath, RootContainerRelativePath);
            yield return NormalizeCanonicalPath(record.CurrentVaultContainerPath, VaultContainerRelativePath);
            yield return NormalizeCanonicalPath(record.CurrentObjectContainerPath, ObjectContainerRelativePath);
            yield return NormalizeCanonicalPath(record.CurrentRecoveryContainerPath, RecoveryContainerRelativePath);
        }

        private string? ResolvePhysicalDrivePath(string devicePath)
        {
            if (devicePath.StartsWith(@"\\.\PHYSICALDRIVE", StringComparison.OrdinalIgnoreCase))
                return devicePath;

            return _blackSecureRawVolumeService.TryResolvePhysicalDevicePathFromDriveRoot(devicePath, out var physicalPath)
                ? physicalPath
                : null;
        }

        private static string NormalizeCanonicalPath(string? path, string fallback)
        {
            if (string.IsNullOrWhiteSpace(path))
                return fallback;

            return Path.IsPathRooted(path)
                ? fallback
                : path.Replace('\\', '/');
        }

        private static string NormalizeDriveRoot(string driveRoot)
        {
            if (string.IsNullOrWhiteSpace(driveRoot))
                return driveRoot;

            return driveRoot.EndsWith(Path.DirectorySeparatorChar) ? driveRoot : driveRoot + Path.DirectorySeparatorChar;
        }

        private static string PrepareRollbackDirectoryPath()
        {
            string basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhantomVault",
                "TierMigrations");
            return Path.Combine(basePath, $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        }
    }
}
