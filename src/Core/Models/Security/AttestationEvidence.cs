using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.Security
{

    public sealed class AttestationEvidence
    {

        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("attestationId")]
        public string AttestationId { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("actionType")]
        public AttestationActionType ActionType { get; set; }

        [JsonPropertyName("createdUtc")]
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("expiresUtc")]
        public DateTimeOffset ExpiresUtc { get; set; }

        [JsonPropertyName("nonce")]
        public string Nonce { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("policyReference")]
        public string? PolicyReference { get; set; }

        [JsonPropertyName("deviceFingerprint")]
        public string? DeviceFingerprint { get; set; }

        [JsonPropertyName("usbTokenPresent")]
        public bool UsbTokenPresent { get; set; }

        [JsonPropertyName("sessionContext")]
        public AttestationSessionContext? SessionContext { get; set; }

        [JsonPropertyName("intentDeclaration")]
        public IntentDeclaration? Intent { get; set; }

        [JsonPropertyName("livenessProof")]
        public LivenessProof? Liveness { get; set; }

        [JsonPropertyName("signature")]
        public string? SignatureBase64 { get; set; }

        [JsonPropertyName("signingKeyId")]
        public string? SigningKeyId { get; set; }

        [JsonPropertyName("payloadHash")]
        public string? PayloadHashBase64 { get; set; }

        [JsonPropertyName("revoked")]
        public bool Revoked { get; set; } = false;

        [JsonPropertyName("revocationReason")]
        public string? RevocationReason { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AttestationActionType
    {

        VaultUnlock,

        CredentialAccess,

        CredentialModify,

        CredentialDelete,

        Export,

        Autofill,

        ClipboardCopy,

        PolicyChange,

        Rekey,

        Recovery,

        DeviceTrust,

        Share
    }

    public sealed class AttestationSessionContext
    {

        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        [JsonPropertyName("sessionStartUtc")]
        public DateTimeOffset SessionStartUtc { get; set; }

        [JsonPropertyName("actionCount")]
        public int ActionCount { get; set; }

        [JsonPropertyName("timeoutSeconds")]
        public int TimeoutSeconds { get; set; } = 300;

        [JsonPropertyName("requiresHardwarePresence")]
        public bool RequiresHardwarePresence { get; set; }
    }

    public sealed class IntentDeclaration
    {

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("target")]
        public string? Target { get; set; }

        [JsonPropertyName("scope")]
        public List<string> Scope { get; set; } = new();

        [JsonPropertyName("userConfirmed")]
        public bool UserConfirmed { get; set; }

        [JsonPropertyName("confirmationMethod")]
        public string? ConfirmationMethod { get; set; }
    }

    public sealed class LivenessProof
    {

        [JsonPropertyName("proofType")]
        public LivenessProofType ProofType { get; set; }

        [JsonPropertyName("verifiedUtc")]
        public DateTimeOffset VerifiedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("validityWindowSeconds")]
        public int ValidityWindowSeconds { get; set; } = 20;

        [JsonPropertyName("usbInsertionDetected")]
        public bool UsbInsertionDetected { get; set; }

        [JsonPropertyName("challengeNonce")]
        public string? ChallengeNonce { get; set; }

        [JsonPropertyName("challengeResponse")]
        public string? ChallengeResponseBase64 { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LivenessProofType
    {

        TimeBoundedApproval,

        UsbPresence,

        HardwareButtonPress,

        RotatingSecret,

        TotpVerification,

        PinEntry,

        MultiFactor
    }

    public sealed class AttestationAuditTrail
    {

        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("recentAttestations")]
        public List<AttestationEvidence> RecentAttestations { get; set; } = new();

        [JsonPropertyName("maxRetainedCount")]
        public int MaxRetainedCount { get; set; } = 100;

        [JsonPropertyName("previousEntryHash")]
        public string? PreviousEntryHashBase64 { get; set; }

        [JsonPropertyName("lastAttestationUtc")]
        public DateTimeOffset? LastAttestationUtc { get; set; }
    }
}

