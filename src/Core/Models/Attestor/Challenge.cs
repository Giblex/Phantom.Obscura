using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.Attestor
{

    public sealed class Challenge
    {

        [JsonPropertyName("challenge_id")]
        public string ChallengeId { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("requester")]
        public ChallengeRequester Requester { get; set; } = new();

        [JsonPropertyName("identity_id")]
        public string IdentityId { get; set; } = string.Empty;

        [JsonPropertyName("operation")]
        public ChallengeOperation Operation { get; set; } = ChallengeOperation.TotpReveal;

        [JsonPropertyName("nonce")]
        public string Nonce { get; set; } = Convert.ToBase64String(Guid.NewGuid().ToByteArray());

        [JsonPropertyName("ttl_seconds")]
        public int TtlSeconds { get; set; } = 20;

        [JsonPropertyName("context")]
        public ChallengeContext Context { get; set; } = new();

        [JsonPropertyName("policy_eval")]
        public PolicyEvaluation PolicyEval { get; set; } = new();

        [JsonPropertyName("status")]
        public ChallengeStatus Status { get; set; } = ChallengeStatus.Pending;

        [JsonPropertyName("expires_at")]
        public DateTimeOffset ExpiresAt => CreatedAt.AddSeconds(TtlSeconds);

        [JsonIgnore]
        public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;

        [JsonPropertyName("user_hint")]
        public string? UserHint { get; set; }
    }

    public sealed class ChallengeRequester
    {

        [JsonPropertyName("type")]
        public RequesterType Type { get; set; } = RequesterType.LocalApp;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("process_hash")]
        public string? ProcessHash { get; set; }

        [JsonPropertyName("pid")]
        public int? Pid { get; set; }

        [JsonPropertyName("origin")]
        public string? Origin { get; set; }

        [JsonPropertyName("user_agent")]
        public string? UserAgent { get; set; }

        [JsonPropertyName("verified")]
        public bool Verified { get; set; } = false;

        [JsonPropertyName("trust_level")]
        public TrustLevel TrustLevel { get; set; } = TrustLevel.Unknown;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RequesterType
    {

        LocalApp,

        BrowserExtension,

        Cli,

        RemoteDevice,

        SystemService,

        Unknown
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TrustLevel
    {

        Unknown,

        Suspicious,

        Known,

        Trusted,

        System
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChallengeOperation
    {

        TotpReveal,

        Sign,

        Decrypt,

        ExportPublic,

        ApproveLogin,

        Autofill,

        ClipboardCopy,

        SshAuth,

        CertAuth,

        ApiKeyReveal,

        RecoveryCodeUse
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChallengeStatus
    {

        Pending,

        Approved,

        Denied,

        Expired,

        PolicyBlocked,

        Cancelled
    }

    public sealed class ChallengeContext
    {

        [JsonPropertyName("ip")]
        public string? Ip { get; set; }

        [JsonPropertyName("geo_hint")]
        public string? GeoHint { get; set; }

        [JsonPropertyName("user_hint")]
        public string? UserHint { get; set; }

        [JsonPropertyName("target_host")]
        public string? TargetHost { get; set; }

        [JsonPropertyName("target_user")]
        public string? TargetUser { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    public sealed class ApprovalArtifact
    {

        [JsonPropertyName("approval_id")]
        public string ApprovalId { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("challenge_id")]
        public string ChallengeId { get; set; } = string.Empty;

        [JsonPropertyName("approved_at")]
        public DateTimeOffset ApprovedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("expires_at")]
        public DateTimeOffset ExpiresAt { get; set; }

        [JsonPropertyName("decision")]
        public ApprovalDecision Decision { get; set; } = ApprovalDecision.Allow;

        [JsonPropertyName("policy_snapshot_hash")]
        public string PolicySnapshotHash { get; set; } = string.Empty;

        [JsonPropertyName("artifact")]
        public SignedArtifact Artifact { get; set; } = new();

        [JsonPropertyName("identity_id")]
        public string IdentityId { get; set; } = string.Empty;

        [JsonPropertyName("operation")]
        public ChallengeOperation Operation { get; set; }

        [JsonPropertyName("liveness")]
        public LivenessEvidence? Liveness { get; set; }

        [JsonPropertyName("used")]
        public bool Used { get; set; } = false;

        [JsonPropertyName("used_at")]
        public DateTimeOffset? UsedAt { get; set; }

        [JsonIgnore]
        public bool IsValid => DateTimeOffset.UtcNow <= ExpiresAt && !Used;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ApprovalDecision
    {

        Allow,

        Deny,

        PolicyDeny
    }

    public sealed class SignedArtifact
    {

        [JsonPropertyName("format")]
        public string Format { get; set; } = "cbor";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "signed_token";

        [JsonPropertyName("payload_hash")]
        public string PayloadHash { get; set; } = string.Empty;

        [JsonPropertyName("signature")]
        public string Signature { get; set; } = string.Empty;

        [JsonPropertyName("signing_key_id")]
        public string? SigningKeyId { get; set; }

        [JsonPropertyName("challenge_nonce")]
        public string ChallengeNonce { get; set; } = string.Empty;
    }

    public sealed class LivenessEvidence
    {

        [JsonPropertyName("type")]
        public LivenessType Type { get; set; }

        [JsonPropertyName("verified_at")]
        public DateTimeOffset VerifiedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("usb_present")]
        public bool UsbPresent { get; set; }

        [JsonPropertyName("button_pressed")]
        public bool? ButtonPressed { get; set; }

        [JsonPropertyName("validity_seconds")]
        public int ValiditySeconds { get; set; } = 20;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LivenessType
    {

        TimeBounded,

        UsbPresence,

        HardwareButton,

        TotpEntry,

        PinEntry,

        MultiFactor
    }
}

