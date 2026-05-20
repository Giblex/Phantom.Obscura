using System;
using System.Collections.Generic;
using System.Linq;

namespace PhantomVault.Core.Services.Security
{
    public enum CapabilityState
    {
        Implemented,
        FailClosed,
        ExternalDependency
    }

    public sealed record CapabilityRecord(
        string Id,
        string Component,
        string Description,
        CapabilityState State,
        bool SecurityCritical,
        string Enforcement);

    public static class CapabilityLedger
    {
        private static readonly CapabilityRecord[] Records =
        {
            new(
                "obscura.container.v4",
                "Phantom Obscura",
                "Authenticated v4 encrypted container with encrypted private header and payload hash validation.",
                CapabilityState.Implemented,
                SecurityCritical: true,
                "Container open fails on unauthenticated private-header access or payload hash mismatch."),
            new(
                "obscura.rekey.streaming",
                "Phantom Obscura",
                "Container rekey streams plaintext through a bounded in-memory pipe into a newly encrypted container.",
                CapabilityState.Implemented,
                SecurityCritical: true,
                "No plaintext temp file is created; replacement happens after new container authentication succeeds."),
            new(
                "obscura.recovery.launch",
                "Phantom Recovery",
                "Obscura can launch a suite-bundled PhantomRecovery executable with a resolved workspace.",
                CapabilityState.ExternalDependency,
                SecurityCritical: true,
                "Launch is denied when the signed suite executable or vault workspace cannot be resolved."),
            new(
                "obscura.recovery.embedded-panel",
                "Phantom Recovery",
                "Embedded recovery panel inside Obscura.",
                CapabilityState.FailClosed,
                SecurityCritical: true,
                "The embedded panel is disabled; recovery must launch through the external signed app path."),
            new(
                "obscura.autofill.browser-native-host",
                "Phantom Attestor",
                "Browser native messaging autofill host.",
                CapabilityState.FailClosed,
                SecurityCritical: true,
                "Native host services are not registered until a vault-backed credential repository is provided."),
            new(
                "obscura.autofill.usb-auto-inject",
                "Phantom Attestor",
                "USB-triggered active-window credential injection.",
                CapabilityState.Implemented,
                SecurityCritical: true,
                "Policy engine defaults to prompt and requires a current unlocked-vault credential provider."),
            new(
                "obscura.yubikey.fido2",
                "Phantom Key",
                "YubiKey FIDO2 vault binding.",
                CapabilityState.FailClosed,
                SecurityCritical: true,
                "Unavailable operations throw before credential registration/authentication can be treated as successful."),
            new(
                "obscura.platform.passkey.windows",
                "Phantom Key",
                "Windows platform authenticator bridge.",
                CapabilityState.ExternalDependency,
                SecurityCritical: true,
                "Windows Hello availability is checked at runtime and unsupported platforms use NullPasskeyService."),
            new(
                "obscura.platform.passkey.mobile",
                "Phantom Key",
                "Android/iOS platform authenticator bridge.",
                CapabilityState.FailClosed,
                SecurityCritical: true,
                "Mobile platform code is excluded from the desktop target and throws PlatformNotSupported outside mobile TFMs."),
            new(
                "obscura.policy.sync",
                "Phantom Obscura",
                "Cross-policy USB, desktop, and manifest consistency validation.",
                CapabilityState.Implemented,
                SecurityCritical: true,
                "Base policy enables cross-policy validation and policy sync failure is configured to fail closed."),
            new(
                "obscura.totp.qr-camera",
                "Phantom Attestor",
                "Desktop camera QR capture for TOTP enrollment.",
                CapabilityState.FailClosed,
                SecurityCritical: false,
                "Manual secret entry remains available; camera capture does not create or modify secrets."),
        };

        public static IReadOnlyList<CapabilityRecord> All => Records;

        public static CapabilityRecord GetRequired(string id)
        {
            var record = Records.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));
            return record ?? throw new InvalidOperationException($"Capability '{id}' is not recorded in the Obscura capability ledger.");
        }

        public static IReadOnlyList<CapabilityRecord> GetSecurityCriticalUnsafeStates()
            => Records
                .Where(r => r.SecurityCritical && r.State != CapabilityState.Implemented && r.State != CapabilityState.FailClosed && r.State != CapabilityState.ExternalDependency)
                .ToArray();
    }
}
