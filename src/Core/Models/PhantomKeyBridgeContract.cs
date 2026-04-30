using System;

namespace PhantomVault.Core.Models
{
    /// <summary>
    /// Canonical Phantom Key bridge contract for Obscura-side provisioning and runtime validation.
    /// The bridge is intentionally narrow: only policy, continuity, mapping, and audit records cross
    /// the boundary. Raw credential or private key material must never be copied into Obscura state.
    /// </summary>
    public static class PhantomKeyBridgeContract
    {
        public const string WorkspaceRelativePath = "vaults/phantomkey";
        public const string BridgeManifestRelativePath = "vaults/phantomkey/bridge.manifest.json";
        public const string ContinuityRelativePath = "vaults/phantomkey/phantomkey.continuity.pmeta";
        public const string PolicyRelativePath = "vaults/phantomkey/phantomkey.policy.pmeta";
        public const string ConsumerMapRelativePath = "vaults/phantomkey/phantomkey.consumer-map.pmeta";
        public const string AuditLogRelativePath = "vaults/phantomkey/phantomkey.audit.log";
        public const string BridgeReceiptRelativePath = "root/phantomkey.bridge.pmeta";

        public const string BridgeManifestModel = "isolated-phantomkey-bridge";
        public const string ObscuraOwnerApp = "PhantomObscura";
        public const string AttestorConsumerApp = "PhantomAttestor";
        public const string ContinuityPurpose = "phantomkey-continuity";
        public const string PolicyPurpose = "phantomkey-policy";
        public const string ConsumerMapPurpose = "phantomkey-consumer-map";
        public const string BridgeReceiptPurpose = "phantomkey-bridge-receipt";

        public static readonly string[] DefaultConsumers = { ObscuraOwnerApp, AttestorConsumerApp };
        public static readonly string[] DefaultRecordClasses = { "policy", "continuity", "consumer-map", "audit-log" };
    }

    public sealed class PhantomKeyBridgeManifestDocument
    {
        public DateTimeOffset CreatedUtc { get; init; }
        public string BridgeModel { get; init; } = string.Empty;
        public string OwnerApp { get; init; } = string.Empty;
        public string[] Consumers { get; init; } = Array.Empty<string>();
        public string WorkspacePath { get; init; } = string.Empty;
        public string Notes { get; init; } = string.Empty;
    }

    public sealed class PhantomKeyContinuityDocument
    {
        public DateTimeOffset CreatedUtc { get; init; }
        public string VaultName { get; init; } = string.Empty;
        public string ProtectionTier { get; init; } = string.Empty;
        public string EffectiveTransport { get; init; } = string.Empty;
        public string BridgeWorkspacePath { get; init; } = string.Empty;
        public string[] Consumers { get; init; } = Array.Empty<string>();
        public string BindingDigest { get; init; } = string.Empty;
        public bool RequiresPasskeyBridge { get; init; }
        public string Notes { get; init; } = string.Empty;
    }

    public sealed class PhantomKeyPolicyWorkspaceDocument
    {
        public DateTimeOffset CreatedUtc { get; init; }
        public string OwnerApp { get; init; } = string.Empty;
        public string StorageBoundary { get; init; } = string.Empty;
        public bool PrivateMaterialExportAllowed { get; init; }
        public bool RequiresBridgeMediation { get; init; }
        public string[] AllowedConsumers { get; init; } = Array.Empty<string>();
        public string[] AllowedRecordClasses { get; init; } = Array.Empty<string>();
        public string Notes { get; init; } = string.Empty;
    }

    public sealed class PhantomKeyConsumerMapDocument
    {
        public DateTimeOffset CreatedUtc { get; init; }
        public string OwnerApp { get; init; } = string.Empty;
        public string WorkspacePath { get; init; } = string.Empty;
        public string ObscuraBindingRecordPath { get; init; } = string.Empty;
        public string ObscuraProvisioningRecordPath { get; init; } = string.Empty;
        public string RecoveryWorkspacePath { get; init; } = string.Empty;
        public string[] ConsumerApps { get; init; } = Array.Empty<string>();
        public string Notes { get; init; } = string.Empty;
    }

    public sealed class PhantomKeyBridgeReceiptDocument
    {
        public DateTimeOffset CreatedUtc { get; init; }
        public string WorkspacePath { get; init; } = string.Empty;
        public string ManifestPath { get; init; } = string.Empty;
        public string ContinuityPath { get; init; } = string.Empty;
        public string PolicyPath { get; init; } = string.Empty;
        public string ConsumerMapPath { get; init; } = string.Empty;
        public string AuditLogPath { get; init; } = string.Empty;
        public string StorageBoundary { get; init; } = string.Empty;
        public bool PrivateMaterialExportAllowed { get; init; }
        public string Notes { get; init; } = string.Empty;
    }
}
