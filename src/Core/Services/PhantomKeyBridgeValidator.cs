using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Validates the Obscura-side Phantom Key bridge contract at runtime.
    /// This validator is intentionally strict and fails closed whenever the
    /// bridge is required but missing, incomplete, or inconsistent.
    /// </summary>
    public sealed class PhantomKeyBridgeValidator
    {
        private readonly UsbArtifactProtectionService _artifactProtectionService;

        public PhantomKeyBridgeValidator(UsbArtifactProtectionService artifactProtectionService)
        {
            _artifactProtectionService = artifactProtectionService ?? throw new ArgumentNullException(nameof(artifactProtectionService));
        }

        public void Validate(VaultManifest manifest, string? passphrase, string? keyfilePath)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));

            if (!manifest.PhantomKeyBridgeEnabled)
            {
                if (manifest.RequiresHardwareToken)
                {
                    throw new InvalidOperationException("The vault requires Phantom Key hardware assurance but the manifest does not declare an isolated bridge.");
                }

                return;
            }

            string workspacePath = RequirePath(manifest.PhantomKeyBridgeWorkspacePath, nameof(manifest.PhantomKeyBridgeWorkspacePath));
            string layoutRoot = ResolveLayoutRootFromWorkspace(workspacePath);
            string bridgeManifestPath = RequirePath(manifest.PhantomKeyBridgeManifestPath, nameof(manifest.PhantomKeyBridgeManifestPath));
            string continuityPath = RequirePath(manifest.PhantomKeyBridgeContinuityPath, nameof(manifest.PhantomKeyBridgeContinuityPath));
            string policyPath = RequirePath(manifest.PhantomKeyBridgePolicyPath, nameof(manifest.PhantomKeyBridgePolicyPath));
            string consumerMapPath = RequirePath(manifest.PhantomKeyBridgeConsumerMapPath, nameof(manifest.PhantomKeyBridgeConsumerMapPath));
            string auditLogPath = RequirePath(manifest.PhantomKeyBridgeAuditLogPath, nameof(manifest.PhantomKeyBridgeAuditLogPath));
            string bridgeReceiptPath = RequirePath(manifest.PhantomKeyBridgeReceiptPath, nameof(manifest.PhantomKeyBridgeReceiptPath));

            EnsureExists(workspacePath, "Phantom Key bridge workspace");
            EnsureExists(bridgeManifestPath, "Phantom Key bridge manifest");
            EnsureExists(continuityPath, "Phantom Key bridge continuity record");
            EnsureExists(policyPath, "Phantom Key bridge policy record");
            EnsureExists(consumerMapPath, "Phantom Key bridge consumer map");
            EnsureExists(auditLogPath, "Phantom Key bridge audit log");
            EnsureExists(bridgeReceiptPath, "Phantom Key bridge receipt");

            var bridgeManifest = JsonSerializer.Deserialize<PhantomKeyBridgeManifestDocument>(File.ReadAllText(bridgeManifestPath))
                ?? throw new InvalidOperationException("Phantom Key bridge manifest is empty.");
            var continuity = _artifactProtectionService.ReadEncryptedJson<PhantomKeyContinuityDocument>(
                continuityPath,
                manifest,
                passphrase,
                keyfilePath,
                PhantomKeyBridgeContract.ContinuityPurpose);
            var policy = _artifactProtectionService.ReadEncryptedJson<PhantomKeyPolicyWorkspaceDocument>(
                policyPath,
                manifest,
                passphrase,
                keyfilePath,
                PhantomKeyBridgeContract.PolicyPurpose);
            var consumerMap = _artifactProtectionService.ReadEncryptedJson<PhantomKeyConsumerMapDocument>(
                consumerMapPath,
                manifest,
                passphrase,
                keyfilePath,
                PhantomKeyBridgeContract.ConsumerMapPurpose);
            var receipt = _artifactProtectionService.ReadEncryptedJson<PhantomKeyBridgeReceiptDocument>(
                bridgeReceiptPath,
                manifest,
                passphrase,
                keyfilePath,
                PhantomKeyBridgeContract.BridgeReceiptPurpose);

            if (!string.Equals(bridgeManifest.BridgeModel, PhantomKeyBridgeContract.BridgeManifestModel, StringComparison.Ordinal))
                throw new InvalidOperationException("Phantom Key bridge model is not recognized.");

            if (!string.Equals(bridgeManifest.OwnerApp, PhantomKeyBridgeContract.ObscuraOwnerApp, StringComparison.Ordinal))
                throw new InvalidOperationException("Phantom Key bridge owner is not trusted for Obscura runtime.");

            if (!ContainsRequiredConsumers(bridgeManifest.Consumers) ||
                !ContainsRequiredConsumers(continuity.Consumers) ||
                !ContainsRequiredConsumers(policy.AllowedConsumers) ||
                !ContainsRequiredConsumers(consumerMap.ConsumerApps))
            {
                throw new InvalidOperationException("Phantom Key bridge consumer authorization is incomplete.");
            }

            if (!string.Equals(bridgeManifest.WorkspacePath, PhantomKeyBridgeContract.WorkspaceRelativePath, StringComparison.Ordinal) ||
                !string.Equals(continuity.BridgeWorkspacePath, PhantomKeyBridgeContract.WorkspaceRelativePath, StringComparison.Ordinal) ||
                !string.Equals(consumerMap.WorkspacePath, PhantomKeyBridgeContract.WorkspaceRelativePath, StringComparison.Ordinal) ||
                !string.Equals(receipt.WorkspacePath, PhantomKeyBridgeContract.WorkspaceRelativePath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Phantom Key bridge workspace mapping is inconsistent.");
            }

            if (policy.PrivateMaterialExportAllowed || receipt.PrivateMaterialExportAllowed || !policy.RequiresBridgeMediation)
                throw new InvalidOperationException("Phantom Key bridge policy allows unsafe material exposure.");

            if (!PhantomKeyBridgeContract.DefaultRecordClasses.All(recordClass =>
                    policy.AllowedRecordClasses.Contains(recordClass, StringComparer.Ordinal)))
            {
                throw new InvalidOperationException("Phantom Key bridge policy is missing one or more required record classes.");
            }

            if (!string.Equals(receipt.ManifestPath, PhantomKeyBridgeContract.BridgeManifestRelativePath, StringComparison.Ordinal) ||
                !string.Equals(receipt.ContinuityPath, PhantomKeyBridgeContract.ContinuityRelativePath, StringComparison.Ordinal) ||
                !string.Equals(receipt.PolicyPath, PhantomKeyBridgeContract.PolicyRelativePath, StringComparison.Ordinal) ||
                !string.Equals(receipt.ConsumerMapPath, PhantomKeyBridgeContract.ConsumerMapRelativePath, StringComparison.Ordinal) ||
                !string.Equals(receipt.AuditLogPath, PhantomKeyBridgeContract.AuditLogRelativePath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Phantom Key bridge receipt paths do not match the required contract.");
            }

            if (!string.Equals(consumerMap.ObscuraBindingRecordPath, NormalizeRelativeAgainstLayout(layoutRoot, manifest.BindingRecordPath), StringComparison.Ordinal) ||
                !string.Equals(consumerMap.ObscuraProvisioningRecordPath, NormalizeRelativeAgainstLayout(layoutRoot, manifest.ProvisioningRecordPath), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Phantom Key bridge consumer map does not match the Obscura manifest.");
            }

            if (!string.Equals(continuity.VaultName, manifest.VaultName, StringComparison.Ordinal) ||
                !string.Equals(continuity.ProtectionTier, manifest.ProtectionTier.ToString(), StringComparison.Ordinal) ||
                !string.Equals(continuity.EffectiveTransport, manifest.EffectiveStorageTransport.ToString(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Phantom Key continuity metadata does not match the active manifest.");
            }

            string expectedDigest = string.IsNullOrWhiteSpace(manifest.UsbBindingId)
                ? string.Empty
                : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest.UsbBindingId)));
            if (!string.Equals(continuity.BindingDigest, expectedDigest, StringComparison.Ordinal))
                throw new InvalidOperationException("Phantom Key continuity binding digest does not match the active manifest.");
        }

        public static void ResolveRuntimePaths(VaultManifest manifest, string layoutRoot)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));
            if (string.IsNullOrWhiteSpace(layoutRoot))
                throw new ArgumentException("Layout root is required.", nameof(layoutRoot));

            manifest.PhantomKeyBridgeWorkspacePath = ResolvePath(layoutRoot, manifest.PhantomKeyBridgeWorkspacePath);
            manifest.PhantomKeyBridgeManifestPath = ResolvePath(layoutRoot, manifest.PhantomKeyBridgeManifestPath);
            manifest.PhantomKeyBridgeContinuityPath = ResolvePath(layoutRoot, manifest.PhantomKeyBridgeContinuityPath);
            manifest.PhantomKeyBridgePolicyPath = ResolvePath(layoutRoot, manifest.PhantomKeyBridgePolicyPath);
            manifest.PhantomKeyBridgeConsumerMapPath = ResolvePath(layoutRoot, manifest.PhantomKeyBridgeConsumerMapPath);
            manifest.PhantomKeyBridgeAuditLogPath = ResolvePath(layoutRoot, manifest.PhantomKeyBridgeAuditLogPath);
            manifest.PhantomKeyBridgeReceiptPath = ResolvePath(layoutRoot, manifest.PhantomKeyBridgeReceiptPath);
        }

        private static string RequirePath(string? path, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException($"Phantom Key bridge requires manifest field '{fieldName}'.");

            return path;
        }

        private static void EnsureExists(string path, string label)
        {
            bool exists = Directory.Exists(path) || File.Exists(path);
            if (!exists)
                throw new FileNotFoundException($"{label} is missing.", path);
        }

        private static bool ContainsRequiredConsumers(string[] consumers)
            => PhantomKeyBridgeContract.DefaultConsumers.All(required =>
                consumers.Contains(required, StringComparer.Ordinal));

        private static string NormalizeRelativeAgainstLayout(string layoutRoot, string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            string normalizedPath = path.Replace('\\', '/');
            if (!Path.IsPathRooted(path))
                return normalizedPath;

            return Path.GetRelativePath(layoutRoot, path).Replace('\\', '/');
        }

        private static string? ResolvePath(string layoutRoot, string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            return Path.IsPathRooted(path)
                ? path
                : Path.Combine(layoutRoot, path.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string ResolveLayoutRootFromWorkspace(string workspacePath)
        {
            var workspaceDirectory = new DirectoryInfo(workspacePath);
            var vaultsDirectory = workspaceDirectory.Parent;
            var layoutRoot = vaultsDirectory?.Parent;
            if (layoutRoot == null)
                throw new InvalidOperationException("Unable to determine the layout root for the Phantom Key bridge workspace.");

            return layoutRoot.FullName;
        }
    }
}
