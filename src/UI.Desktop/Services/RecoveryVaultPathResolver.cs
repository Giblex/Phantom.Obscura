using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.Services
{
    public readonly record struct RecoveryVaultResolution(
        bool Resolved,
        string VaultPath,
        bool DirectoryExists,
        bool HasHeader,
        string Message);

    /// <summary>
    /// Resolves the PhantomRecovery workspace path that belongs to the current Obscura vault.
    /// </summary>
    public sealed class RecoveryVaultPathResolver
    {
        private readonly UsbArtifactProtectionService _usbArtifactProtectionService;

        public RecoveryVaultPathResolver(UsbArtifactProtectionService usbArtifactProtectionService)
        {
            _usbArtifactProtectionService = usbArtifactProtectionService ?? throw new ArgumentNullException(nameof(usbArtifactProtectionService));
        }

        public RecoveryVaultResolution Resolve(
            string manifestPath,
            VaultManifest manifest,
            string? passphrase,
            string? keyfilePath)
        {
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                return new RecoveryVaultResolution(false, string.Empty, false, false,
                    "No Obscura manifest path is available for recovery resolution.");
            }

            var manifestDirectory = Path.GetDirectoryName(manifestPath) ?? AppContext.BaseDirectory;
            var candidateDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddCandidateDirectory(candidateDirectories, ResolvePath(manifest.RecoveryContainerPath, manifestDirectory));
            AddCandidateDirectory(candidateDirectories, ResolvePath(manifest.RecoveryRecordPath, manifestDirectory));

            try
            {
                if (!string.IsNullOrWhiteSpace(manifest.RecoveryRecordPath))
                {
                    var recoveryRecord = _usbArtifactProtectionService.ReadEncryptedJson<RecoveryVaultRecord>(
                        manifest.RecoveryRecordPath,
                        manifest,
                        passphrase,
                        keyfilePath,
                        "recovery-record");

                    AddCandidateDirectory(candidateDirectories, ResolvePath(recoveryRecord.RecoveryVaultPath, manifestDirectory));
                    AddCandidateDirectory(candidateDirectories, ResolvePath(recoveryRecord.RecoveryContainerPath, manifestDirectory));
                }
            }
            catch
            {
                // Best effort: the recovery record can remain unreadable while the manifest
                // still provides enough information to resolve the suite workspace path.
            }

            foreach (var baseDirectory in candidateDirectories)
            {
                var legacyHeaderPath = Path.Combine(baseDirectory, "header.json");
                if (File.Exists(legacyHeaderPath))
                {
                    return new RecoveryVaultResolution(
                        true,
                        baseDirectory,
                        true,
                        true,
                        $"Recovery vault resolved for this Obscura vault at {baseDirectory}.");
                }

                var contractDirectory = Path.Combine(baseDirectory, "vault");
                var contractHeaderPath = Path.Combine(contractDirectory, "header.json");
                if (File.Exists(contractHeaderPath))
                {
                    return new RecoveryVaultResolution(
                        true,
                        contractDirectory,
                        true,
                        true,
                        $"Recovery vault resolved for this Obscura vault at {contractDirectory}.");
                }

                return new RecoveryVaultResolution(
                    true,
                    contractDirectory,
                    Directory.Exists(contractDirectory),
                    false,
                    $"Recovery workspace resolved for this Obscura vault at {contractDirectory}. PhantomRecovery will initialize it on first open.");
            }

            return new RecoveryVaultResolution(
                false,
                string.Empty,
                false,
                false,
                "No recovery vault path could be resolved from the current Obscura manifest.");
        }

        public string EnsureDirectory(string vaultPath)
        {
            if (string.IsNullOrWhiteSpace(vaultPath))
                throw new ArgumentException("Recovery vault path is required.", nameof(vaultPath));

            return Directory.CreateDirectory(vaultPath).FullName;
        }

        private static void AddCandidateDirectory(HashSet<string> candidates, string? candidatePath)
        {
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                return;
            }

            if (Directory.Exists(candidatePath))
            {
                candidates.Add(candidatePath);
                return;
            }

            var directory = Path.GetDirectoryName(candidatePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                candidates.Add(directory);
            }
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
