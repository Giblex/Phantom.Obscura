using System;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models
{
    [Flags]
    public enum PossessionFactor
    {
        None = 0,
        UsbBinding = 1 << 0,
        Keyfile = 1 << 1,
        RecoveryCode = 1 << 2,
    }

    [Flags]
    public enum IdentityFactor
    {
        None = 0,
        PhantomKey = 1 << 0,
        Passkey = 1 << 1,
        Password = 1 << 2,
        Pin = 1 << 3,
    }

    /// <summary>
    /// Policy that gates the Recovery launch surface in Obscura. Stored as a sidecar JSON
    /// next to the vault manifest; null = no gating (backward compatible). Enforcement is
    /// UI-side only; Phantom.Recovery retains its own independent authentication.
    /// </summary>
    public sealed class RecoveryActivationPolicy
    {
        public const int DefaultRequiredPossessionFactors = 2;
        public const int DefaultRequiredIdentityFactors = 2;

        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        [JsonPropertyName("requiredPossessionFactors")]
        public int RequiredPossessionFactors { get; set; } = DefaultRequiredPossessionFactors;

        [JsonPropertyName("requiredIdentityFactors")]
        public int RequiredIdentityFactors { get; set; } = DefaultRequiredIdentityFactors;

        [JsonPropertyName("enabledPossessionFactors")]
        public PossessionFactor EnabledPossessionFactors { get; set; }
            = PossessionFactor.UsbBinding | PossessionFactor.Keyfile | PossessionFactor.RecoveryCode;

        [JsonPropertyName("enabledIdentityFactors")]
        public IdentityFactor EnabledIdentityFactors { get; set; }
            = IdentityFactor.PhantomKey | IdentityFactor.Passkey | IdentityFactor.Password | IdentityFactor.Pin;

        [JsonPropertyName("createdUtc")]
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("updatedUtc")]
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Snapshot of which factors were actually used / satisfied during the active unlocked
    /// session. Built once at unlock and consulted by the gate when the user attempts to
    /// launch Recovery.
    /// </summary>
    public sealed class RecoveryActivationSessionSnapshot
    {
        public PossessionFactor UsedPossessionFactors { get; set; }
        public IdentityFactor UsedIdentityFactors { get; set; }
    }

    public sealed class RecoveryActivationGateResult
    {
        public bool Allowed { get; init; }
        public string Reason { get; init; } = string.Empty;
        public int RequiredPossession { get; init; }
        public int RequiredIdentity { get; init; }
        public int SatisfiedPossession { get; init; }
        public int SatisfiedIdentity { get; init; }
        public PossessionFactor MissingPossession { get; init; }
        public IdentityFactor MissingIdentity { get; init; }
    }
}
