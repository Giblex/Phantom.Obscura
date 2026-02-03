using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.Security
{
    /// <summary>
    /// Attestation evidence for signed approval artifacts.
    /// Provides evidence that an action was authorized, not just trust assertions.
    /// "Here is evidence this action was authorised" vs "trust this device".
    /// Supports: zero-trust, audit requirements, incident reconstruction.
    /// </summary>
    public sealed class AttestationEvidence
    {
        /// <summary>
        /// Version of the attestation schema.
        /// </summary>
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        /// <summary>
        /// Unique identifier for this attestation.
        /// </summary>
        [JsonPropertyName("attestationId")]
        public string AttestationId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Type of action being attested.
        /// </summary>
        [JsonPropertyName("actionType")]
        public AttestationActionType ActionType { get; set; }

        /// <summary>
        /// Timestamp when attestation was created.
        /// </summary>
        [JsonPropertyName("createdUtc")]
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Timestamp when this attestation expires.
        /// </summary>
        [JsonPropertyName("expiresUtc")]
        public DateTimeOffset ExpiresUtc { get; set; }

        /// <summary>
        /// Nonce to prevent replay attacks.
        /// </summary>
        [JsonPropertyName("nonce")]
        public string Nonce { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Reference to the policy that authorized this action.
        /// </summary>
        [JsonPropertyName("policyReference")]
        public string? PolicyReference { get; set; }

        /// <summary>
        /// Device fingerprint where attestation was created.
        /// </summary>
        [JsonPropertyName("deviceFingerprint")]
        public string? DeviceFingerprint { get; set; }

        /// <summary>
        /// Whether USB token was present during attestation.
        /// </summary>
        [JsonPropertyName("usbTokenPresent")]
        public bool UsbTokenPresent { get; set; }

        /// <summary>
        /// Session context for this attestation.
        /// </summary>
        [JsonPropertyName("sessionContext")]
        public AttestationSessionContext? SessionContext { get; set; }

        /// <summary>
        /// Intent declaration - what the user intended to do.
        /// </summary>
        [JsonPropertyName("intentDeclaration")]
        public IntentDeclaration? Intent { get; set; }

        /// <summary>
        /// Liveness proof - evidence of real-time user presence.
        /// </summary>
        [JsonPropertyName("livenessProof")]
        public LivenessProof? Liveness { get; set; }

        /// <summary>
        /// Ed25519 signature of the attestation payload.
        /// </summary>
        [JsonPropertyName("signature")]
        public string? SignatureBase64 { get; set; }

        /// <summary>
        /// Key ID used for signing.
        /// </summary>
        [JsonPropertyName("signingKeyId")]
        public string? SigningKeyId { get; set; }

        /// <summary>
        /// Hash of the payload that was signed (for verification).
        /// </summary>
        [JsonPropertyName("payloadHash")]
        public string? PayloadHashBase64 { get; set; }

        /// <summary>
        /// Whether this attestation has been revoked.
        /// </summary>
        [JsonPropertyName("revoked")]
        public bool Revoked { get; set; } = false;

        /// <summary>
        /// Reason for revocation (if revoked).
        /// </summary>
        [JsonPropertyName("revocationReason")]
        public string? RevocationReason { get; set; }
    }

    /// <summary>
    /// Types of actions that can be attested.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AttestationActionType
    {
        /// <summary>
        /// Vault unlock operation.
        /// </summary>
        VaultUnlock,

        /// <summary>
        /// Credential read/view operation.
        /// </summary>
        CredentialAccess,

        /// <summary>
        /// Credential creation or modification.
        /// </summary>
        CredentialModify,

        /// <summary>
        /// Credential deletion.
        /// </summary>
        CredentialDelete,

        /// <summary>
        /// Export operation (backup, CSV, etc.).
        /// </summary>
        Export,

        /// <summary>
        /// Autofill operation into external application.
        /// </summary>
        Autofill,

        /// <summary>
        /// Clipboard copy operation.
        /// </summary>
        ClipboardCopy,

        /// <summary>
        /// Policy change operation.
        /// </summary>
        PolicyChange,

        /// <summary>
        /// Key rotation/rekey operation.
        /// </summary>
        Rekey,

        /// <summary>
        /// Recovery operation using recovery codes.
        /// </summary>
        Recovery,

        /// <summary>
        /// Device trust decision.
        /// </summary>
        DeviceTrust,

        /// <summary>
        /// Share credential operation.
        /// </summary>
        Share
    }

    /// <summary>
    /// Session context for attestation.
    /// </summary>
    public sealed class AttestationSessionContext
    {
        /// <summary>
        /// Unique session identifier.
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// When the session started.
        /// </summary>
        [JsonPropertyName("sessionStartUtc")]
        public DateTimeOffset SessionStartUtc { get; set; }

        /// <summary>
        /// Number of actions in this session so far.
        /// </summary>
        [JsonPropertyName("actionCount")]
        public int ActionCount { get; set; }

        /// <summary>
        /// Session timeout in seconds.
        /// </summary>
        [JsonPropertyName("timeoutSeconds")]
        public int TimeoutSeconds { get; set; } = 300;

        /// <summary>
        /// Whether session requires hardware presence.
        /// </summary>
        [JsonPropertyName("requiresHardwarePresence")]
        public bool RequiresHardwarePresence { get; set; }
    }

    /// <summary>
    /// Intent declaration - what the user explicitly intended.
    /// </summary>
    public sealed class IntentDeclaration
    {
        /// <summary>
        /// Human-readable description of intended action.
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Target of the action (e.g., credential ID, domain).
        /// </summary>
        [JsonPropertyName("target")]
        public string? Target { get; set; }

        /// <summary>
        /// Scope limitations (e.g., "read-only", "single-use").
        /// </summary>
        [JsonPropertyName("scope")]
        public List<string> Scope { get; set; } = new();

        /// <summary>
        /// Whether explicit user confirmation was obtained.
        /// </summary>
        [JsonPropertyName("userConfirmed")]
        public bool UserConfirmed { get; set; }

        /// <summary>
        /// Method of confirmation (button click, PIN, etc.).
        /// </summary>
        [JsonPropertyName("confirmationMethod")]
        public string? ConfirmationMethod { get; set; }
    }

    /// <summary>
    /// Liveness proof - evidence of real-time user presence without biometrics.
    /// </summary>
    public sealed class LivenessProof
    {
        /// <summary>
        /// Type of liveness verification performed.
        /// </summary>
        [JsonPropertyName("proofType")]
        public LivenessProofType ProofType { get; set; }

        /// <summary>
        /// Timestamp when liveness was verified.
        /// </summary>
        [JsonPropertyName("verifiedUtc")]
        public DateTimeOffset VerifiedUtc { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Validity window in seconds (e.g., 20 seconds for time-bounded approval).
        /// </summary>
        [JsonPropertyName("validityWindowSeconds")]
        public int ValidityWindowSeconds { get; set; } = 20;

        /// <summary>
        /// Whether USB was physically inserted during this window.
        /// </summary>
        [JsonPropertyName("usbInsertionDetected")]
        public bool UsbInsertionDetected { get; set; }

        /// <summary>
        /// Challenge-response nonce (if applicable).
        /// </summary>
        [JsonPropertyName("challengeNonce")]
        public string? ChallengeNonce { get; set; }

        /// <summary>
        /// Response to challenge (if applicable).
        /// </summary>
        [JsonPropertyName("challengeResponse")]
        public string? ChallengeResponseBase64 { get; set; }
    }

    /// <summary>
    /// Types of liveness proof (without biometrics).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LivenessProofType
    {
        /// <summary>
        /// Time-bounded approval window (code valid for N seconds).
        /// </summary>
        TimeBoundedApproval,

        /// <summary>
        /// Physical USB token insertion detected.
        /// </summary>
        UsbPresence,

        /// <summary>
        /// Hardware button press on security key.
        /// </summary>
        HardwareButtonPress,

        /// <summary>
        /// Rotating secret tied to session intent.
        /// </summary>
        RotatingSecret,

        /// <summary>
        /// TOTP code verification within time window.
        /// </summary>
        TotpVerification,

        /// <summary>
        /// PIN entry with rate limiting.
        /// </summary>
        PinEntry,

        /// <summary>
        /// Multiple factors combined.
        /// </summary>
        MultiFactor
    }

    /// <summary>
    /// Collection of attestation records for audit trail.
    /// </summary>
    public sealed class AttestationAuditTrail
    {
        /// <summary>
        /// Version of the audit trail schema.
        /// </summary>
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        /// <summary>
        /// Recent attestation records (limited to prevent unbounded growth).
        /// </summary>
        [JsonPropertyName("recentAttestations")]
        public List<AttestationEvidence> RecentAttestations { get; set; } = new();

        /// <summary>
        /// Maximum attestations to retain.
        /// </summary>
        [JsonPropertyName("maxRetainedCount")]
        public int MaxRetainedCount { get; set; } = 100;

        /// <summary>
        /// Hash of the previous audit entry (for chain integrity).
        /// </summary>
        [JsonPropertyName("previousEntryHash")]
        public string? PreviousEntryHashBase64 { get; set; }

        /// <summary>
        /// Timestamp of last attestation.
        /// </summary>
        [JsonPropertyName("lastAttestationUtc")]
        public DateTimeOffset? LastAttestationUtc { get; set; }
    }
}
