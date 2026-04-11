using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Stages non-sensitive Obscura recovery metadata into the PhantomRecovery workspace
    /// so the sibling app can ingest it into its encrypted artifact vault on first open.
    /// </summary>
    public sealed class RecoverySuiteBootstrapService
    {
        private readonly UsbArtifactProtectionService _usbArtifactProtectionService;

        public RecoverySuiteBootstrapService(UsbArtifactProtectionService usbArtifactProtectionService)
        {
            _usbArtifactProtectionService = usbArtifactProtectionService ?? throw new ArgumentNullException(nameof(usbArtifactProtectionService));
        }

        public void StageBootstrapArtifacts(
            string manifestPath,
            VaultManifest manifest,
            string? passphrase,
            string? keyfilePath,
            string recoveryVaultPath)
        {
            if (string.IsNullOrWhiteSpace(manifestPath))
                throw new ArgumentException("Manifest path is required.", nameof(manifestPath));
            if (string.IsNullOrWhiteSpace(recoveryVaultPath))
                throw new ArgumentException("Recovery vault path is required.", nameof(recoveryVaultPath));

            var pendingDirectory = Path.Combine(recoveryVaultPath, ".suite-bootstrap", "pending");
            Directory.CreateDirectory(pendingDirectory);

            var options = new JsonSerializerOptions { WriteIndented = true };
            var manifestDirectory = Path.GetDirectoryName(manifestPath) ?? AppContext.BaseDirectory;
            RecoveryVaultRecord? recoveryRecord = null;

            if (!string.IsNullOrWhiteSpace(manifest.RecoveryRecordPath) && File.Exists(manifest.RecoveryRecordPath))
            {
                try
                {
                    recoveryRecord = _usbArtifactProtectionService.ReadEncryptedJson<RecoveryVaultRecord>(
                        manifest.RecoveryRecordPath,
                        manifest,
                        passphrase,
                        keyfilePath,
                        "recovery-record");
                }
                catch
                {
                    // Best effort: bootstrap should still proceed with manifest-level metadata.
                }
            }

            var vaultSummary = new
            {
                Source = "Phantom.Obscura",
                VaultName = manifest.VaultName,
                ManifestVersion = manifest.Version,
                CreatedUtc = manifest.CreatedUtc,
                Description = manifest.Description,
                ManifestPath = manifestPath,
                RootContainerPath = ResolvePath(manifest.RootContainerPath, manifestDirectory),
                VaultContainerPath = ResolvePath(manifest.ContainerPath, manifestDirectory),
                ObjectContainerPath = ResolvePath(manifest.ObjectContainerPath, manifestDirectory),
                RecoveryContainerPath = ResolvePath(manifest.RecoveryContainerPath, manifestDirectory),
                RecoveryRecordPath = ResolvePath(manifest.RecoveryRecordPath, manifestDirectory),
                UsbBindingId = manifest.UsbBindingId,
                UsbBindingGuid = manifest.UsbBindingGuid,
                DeviceId = manifest.DeviceId,
                Guuid = manifest.Guuid,
                RequiresHardwareToken = manifest.RequiresHardwareToken,
                RequiresTotp = manifest.RequiresTotp,
                HasPlatformAuthenticator = !string.IsNullOrWhiteSpace(manifest.PasskeyId),
                HasBoundYubiKey = manifest.YubiKeySerial.HasValue || !string.IsNullOrWhiteSpace(manifest.YubiKeyCredentialIdBase64),
                RecoveryCodesRemaining = manifest.RecoveryCodes?.Count(code => !code.Used) ?? 0,
                RecoveryCodesTotal = manifest.RecoveryCodes?.Length ?? 0,
                RecoveryWorkspacePath = recoveryVaultPath
            };

            var recoveryRecordSummary = recoveryRecord is null
                ? null
                : new
                {
                    recoveryRecord.BindingId,
                    recoveryRecord.BindingGuid,
                    RecoveryContainerPath = ResolvePath(recoveryRecord.RecoveryContainerPath, manifestDirectory),
                    RecoveryVaultPath = ResolvePath(recoveryRecord.RecoveryVaultPath, manifestDirectory),
                    recoveryRecord.RecoveryContainerSizeBytes,
                    recoveryRecord.CreatedUtc,
                    recoveryRecord.Notes
                };

            File.WriteAllText(
                Path.Combine(pendingDirectory, "obscura-vault-summary.json"),
                JsonSerializer.Serialize(vaultSummary, options));

            if (recoveryRecordSummary != null)
            {
                File.WriteAllText(
                    Path.Combine(pendingDirectory, "recovery-record-summary.json"),
                    JsonSerializer.Serialize(recoveryRecordSummary, options));
            }

            var instructions = new List<string>
            {
                "Phantom.Obscura staged these files for Phantom.Recovery.",
                "They contain suite metadata only and are safe to import into the encrypted Recovery vault.",
                $"Recovery workspace: {recoveryVaultPath}",
                $"Recovery container: {vaultSummary.RecoveryContainerPath ?? "(not configured)"}",
                $"Recovery codes remaining: {vaultSummary.RecoveryCodesRemaining}/{vaultSummary.RecoveryCodesTotal}",
                "After Phantom.Recovery imports these files, the pending bootstrap folder can be deleted."
            };

            File.WriteAllLines(
                Path.Combine(pendingDirectory, "README.txt"),
                instructions);
        }

        private static string? ResolvePath(string? value, string manifestDirectory)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return Path.IsPathRooted(value)
                ? Path.GetFullPath(value)
                : Path.GetFullPath(Path.Combine(manifestDirectory, value));
        }
    }
}
