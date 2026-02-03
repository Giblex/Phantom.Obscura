using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.Attestor
{
    /// <summary>
    /// A request for approval from a local app, browser, SSH client, etc.
    /// Challenges are time-limited and policy-evaluated before approval.
    /// </summary>
    public sealed class Challenge
    {
        /// <summary>
        /// Unique identifier for this challenge (UUIDv7 recommended).
        /// </summary>
        [JsonPropertyName("challenge_id")]
        public string ChallengeId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Timestamp when this challenge was created.
        /// </summary>
        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Information about the entity requesting approval.
        /// </summary>
        [JsonPropertyName("requester")]
        public ChallengeRequester Requester { get; set; } = new();

        /// <summary>
        /// Identity being requested for this operation.
        /// </summary>
        [JsonPropertyName("identity_id")]
        public string IdentityId { get; set; } = string.Empty;

        /// <summary>
        /// Type of operation being requested.
        /// </summary>
        [JsonPropertyName("operation")]
        public ChallengeOperation Operation { get; set; } = ChallengeOperation.TotpReveal;

        /// <summary>
        /// Cryptographic nonce to prevent replay attacks.
        /// </summary>
        [JsonPropertyName("nonce")]
        public string Nonce { get; set; } = Convert.ToBase64String(Guid.NewGuid().ToByteArray());

        /// <summary>
        /// Time-to-live for this challenge in seconds.
        /// </summary>
        [JsonPropertyName("ttl_seconds")]
        public int TtlSeconds { get; set; } = 20;

        /// <summary>
        /// Additional context about the challenge.
        /// </summary>
        [JsonPropertyName("context")]
        public ChallengeContext Context { get; set; } = new();

        /// <summary>
        /// Result of policy evaluation for this challenge.
        /// </summary>
        [JsonPropertyName("policy_eval")]
        public PolicyEvaluation PolicyEval { get; set; } = new();

        /// <summary>
        /// Current status of this challenge.
        /// </summary>
        [JsonPropertyName("status")]
        public ChallengeStatus Status { get; set; } = ChallengeStatus.Pending;

        /// <summary>
        /// Timestamp when challenge expires.
        /// </summary>
        [JsonPropertyName("expires_at")]
        public DateTimeOffset ExpiresAt => CreatedAt.AddSeconds(TtlSeconds);

        /// <summary>
        /// Whether this challenge has expired.
        /// </summary>
        [JsonIgnore]
        public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;

        /// <summary>
        /// Optional user-visible hint about what this challenge is for.
        /// </summary>
        [JsonPropertyName("user_hint")]
        public string? UserHint { get; set; }
    }

    /// <summary>
    /// Information about the entity requesting approval.
    /// </summary>
    public sealed class ChallengeRequester
    {
        /// <summary>
        /// Type of requester.
        /// </summary>
        [JsonPropertyName("type")]
        public RequesterType Type { get; set; } = RequesterType.LocalApp;

        /// <summary>
        /// Name of the requesting application or service.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// SHA-256 hash of the requesting process executable.
        /// </summary>
        [JsonPropertyName("process_hash")]
        public string? ProcessHash { get; set; }

        /// <summary>
        /// Process ID of the requester (for local apps).
        /// </summary>
        [JsonPropertyName("pid")]
        public int? Pid { get; set; }

        /// <summary>
        /// Origin URL or connection string.
        /// </summary>
        [JsonPropertyName("origin")]
        public string? Origin { get; set; }

        /// <summary>
        /// User agent string (for browser requests).
        /// </summary>
        [JsonPropertyName("user_agent")]
        public string? UserAgent { get; set; }

        /// <summary>
        /// Whether this requester has been verified/allowlisted.
        /// </summary>
        [JsonPropertyName("verified")]
        public bool Verified { get; set; } = false;

        /// <summary>
        /// Trust level of this requester based on history.
        /// </summary>
        [JsonPropertyName("trust_level")]
        public TrustLevel TrustLevel { get; set; } = TrustLevel.Unknown;
    }

    /// <summary>
    /// Types of entities that can request approval.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RequesterType
    {
        /// <summary>
        /// Local application on this device.
        /// </summary>
        LocalApp,

        /// <summary>
        /// Browser extension.
        /// </summary>
        BrowserExtension,

        /// <summary>
        /// Command-line interface.
        /// </summary>
        Cli,

        /// <summary>
        /// Remote device (SSH, etc.).
        /// </summary>
        RemoteDevice,

        /// <summary>
        /// System service.
        /// </summary>
        SystemService,

        /// <summary>
        /// Unknown/unverified requester.
        /// </summary>
        Unknown
    }

    /// <summary>
    /// Trust levels for requesters.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TrustLevel
    {
        /// <summary>
        /// Unknown requester - first time seen.
        /// </summary>
        Unknown,

        /// <summary>
        /// Suspicious - flagged for review.
        /// </summary>
        Suspicious,

        /// <summary>
        /// Known but not explicitly trusted.
        /// </summary>
        Known,

        /// <summary>
        /// Explicitly trusted by user.
        /// </summary>
        Trusted,

        /// <summary>
        /// System-level trust (built-in).
        /// </summary>
        System
    }

    /// <summary>
    /// Types of operations that can be requested.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChallengeOperation
    {
        /// <summary>
        /// Reveal TOTP code.
        /// </summary>
        TotpReveal,

        /// <summary>
        /// Sign data with identity's key.
        /// </summary>
        Sign,

        /// <summary>
        /// Decrypt data with identity's key.
        /// </summary>
        Decrypt,

        /// <summary>
        /// Export public key/certificate.
        /// </summary>
        ExportPublic,

        /// <summary>
        /// Approve a login attempt.
        /// </summary>
        ApproveLogin,

        /// <summary>
        /// Autofill credentials into application.
        /// </summary>
        Autofill,

        /// <summary>
        /// Copy to clipboard.
        /// </summary>
        ClipboardCopy,

        /// <summary>
        /// SSH authentication.
        /// </summary>
        SshAuth,

        /// <summary>
        /// Certificate authentication.
        /// </summary>
        CertAuth,

        /// <summary>
        /// API key retrieval.
        /// </summary>
        ApiKeyReveal,

        /// <summary>
        /// Recovery code use.
        /// </summary>
        RecoveryCodeUse
    }

    /// <summary>
    /// Status of a challenge.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChallengeStatus
    {
        /// <summary>
        /// Waiting for user decision.
        /// </summary>
        Pending,

        /// <summary>
        /// User approved the challenge.
        /// </summary>
        Approved,

        /// <summary>
        /// User denied the challenge.
        /// </summary>
        Denied,

        /// <summary>
        /// Challenge expired before decision.
        /// </summary>
        Expired,

        /// <summary>
        /// Challenge blocked by policy.
        /// </summary>
        PolicyBlocked,

        /// <summary>
        /// Challenge cancelled by requester.
        /// </summary>
        Cancelled
    }

    /// <summary>
    /// Additional context about a challenge.
    /// </summary>
    public sealed class ChallengeContext
    {
        /// <summary>
        /// IP address of the request origin.
        /// </summary>
        [JsonPropertyName("ip")]
        public string? Ip { get; set; }

        /// <summary>
        /// Geographic location hint (if available).
        /// </summary>
        [JsonPropertyName("geo_hint")]
        public string? GeoHint { get; set; }

        /// <summary>
        /// User-visible hint about the request.
        /// </summary>
        [JsonPropertyName("user_hint")]
        public string? UserHint { get; set; }

        /// <summary>
        /// Target hostname/service for this operation.
        /// </summary>
        [JsonPropertyName("target_host")]
        public string? TargetHost { get; set; }

        /// <summary>
        /// Username being authenticated (if applicable).
        /// </summary>
        [JsonPropertyName("target_user")]
        public string? TargetUser { get; set; }

        /// <summary>
        /// Additional metadata as key-value pairs.
        /// </summary>
        [JsonPropertyName("metadata")]
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Approval artifact produced when user approves a challenge.
    /// Signed, timestamped, and non-replayable.
    /// "Here is evidence this action was authorised."
    /// </summary>
    public sealed class ApprovalArtifact
    {
        /// <summary>
        /// Unique identifier for this approval.
        /// </summary>
        [JsonPropertyName("approval_id")]
        public string ApprovalId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Reference to the challenge that was approved.
        /// </summary>
        [JsonPropertyName("challenge_id")]
        public string ChallengeId { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp when approval was granted.
        /// </summary>
        [JsonPropertyName("approved_at")]
        public DateTimeOffset ApprovedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Timestamp when this approval expires.
        /// </summary>
        [JsonPropertyName("expires_at")]
        public DateTimeOffset ExpiresAt { get; set; }

        /// <summary>
        /// The decision that was made.
        /// </summary>
        [JsonPropertyName("decision")]
        public ApprovalDecision Decision { get; set; } = ApprovalDecision.Allow;

        /// <summary>
        /// Hash of the policy set used for evaluation (audit trail).
        /// </summary>
        [JsonPropertyName("policy_snapshot_hash")]
        public string PolicySnapshotHash { get; set; } = string.Empty;

        /// <summary>
        /// The signed artifact itself.
        /// </summary>
        [JsonPropertyName("artifact")]
        public SignedArtifact Artifact { get; set; } = new();

        /// <summary>
        /// Identity ID that was approved for use.
        /// </summary>
        [JsonPropertyName("identity_id")]
        public string IdentityId { get; set; } = string.Empty;

        /// <summary>
        /// Operation that was approved.
        /// </summary>
        [JsonPropertyName("operation")]
        public ChallengeOperation Operation { get; set; }

        /// <summary>
        /// Liveness proof at time of approval.
        /// </summary>
        [JsonPropertyName("liveness")]
        public LivenessEvidence? Liveness { get; set; }

        /// <summary>
        /// Whether this artifact has been used (for single-use approvals).
        /// </summary>
        [JsonPropertyName("used")]
        public bool Used { get; set; } = false;

        /// <summary>
        /// Timestamp when artifact was used (if used).
        /// </summary>
        [JsonPropertyName("used_at")]
        public DateTimeOffset? UsedAt { get; set; }

        /// <summary>
        /// Whether this approval is still valid (not expired, not used if single-use).
        /// </summary>
        [JsonIgnore]
        public bool IsValid => DateTimeOffset.UtcNow <= ExpiresAt && !Used;
    }

    /// <summary>
    /// The decision made on an approval request.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ApprovalDecision
    {
        /// <summary>
        /// Request was allowed.
        /// </summary>
        Allow,

        /// <summary>
        /// Request was denied by user.
        /// </summary>
        Deny,

        /// <summary>
        /// Request was denied by policy.
        /// </summary>
        PolicyDeny
    }

    /// <summary>
    /// The cryptographically signed artifact.
    /// </summary>
    public sealed class SignedArtifact
    {
        /// <summary>
        /// Format of the artifact (cbor or json).
        /// </summary>
        [JsonPropertyName("format")]
        public string Format { get; set; } = "cbor";

        /// <summary>
        /// Type of artifact.
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "signed_token";

        /// <summary>
        /// SHA-256 hash of the payload.
        /// </summary>
        [JsonPropertyName("payload_hash")]
        public string PayloadHash { get; set; } = string.Empty;

        /// <summary>
        /// Ed25519 signature of the payload (base64).
        /// </summary>
        [JsonPropertyName("signature")]
        public string Signature { get; set; } = string.Empty;

        /// <summary>
        /// Key ID used for signing.
        /// </summary>
        [JsonPropertyName("signing_key_id")]
        public string? SigningKeyId { get; set; }

        /// <summary>
        /// Nonce from the original challenge (prevents replay).
        /// </summary>
        [JsonPropertyName("challenge_nonce")]
        public string ChallengeNonce { get; set; } = string.Empty;
    }

    /// <summary>
    /// Evidence of liveness at time of approval (without biometrics).
    /// </summary>
    public sealed class LivenessEvidence
    {
        /// <summary>
        /// Type of liveness verification.
        /// </summary>
        [JsonPropertyName("type")]
        public LivenessType Type { get; set; }

        /// <summary>
        /// Timestamp when liveness was verified.
        /// </summary>
        [JsonPropertyName("verified_at")]
        public DateTimeOffset VerifiedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Whether USB was physically present.
        /// </summary>
        [JsonPropertyName("usb_present")]
        public bool UsbPresent { get; set; }

        /// <summary>
        /// Whether hardware button was pressed (if applicable).
        /// </summary>
        [JsonPropertyName("button_pressed")]
        public bool? ButtonPressed { get; set; }

        /// <summary>
        /// Validity window in seconds.
        /// </summary>
        [JsonPropertyName("validity_seconds")]
        public int ValiditySeconds { get; set; } = 20;
    }

    /// <summary>
    /// Types of liveness verification (no biometrics).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LivenessType
    {
        /// <summary>
        /// Time-bounded approval window.
        /// </summary>
        TimeBounded,

        /// <summary>
        /// USB token physical presence.
        /// </summary>
        UsbPresence,

        /// <summary>
        /// Hardware button press.
        /// </summary>
        HardwareButton,

        /// <summary>
        /// TOTP code entry.
        /// </summary>
        TotpEntry,

        /// <summary>
        /// PIN entry.
        /// </summary>
        PinEntry,

        /// <summary>
        /// Multiple factors combined.
        /// </summary>
        MultiFactor
    }
}
