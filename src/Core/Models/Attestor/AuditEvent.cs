using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.Attestor
{

    public sealed class AuditEvent
    {

        [JsonPropertyName("event_id")]
        public string EventId { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("ts")]
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("type")]
        public AuditEventType Type { get; set; }

        [JsonPropertyName("identity_id")]
        public string? IdentityId { get; set; }

        [JsonPropertyName("challenge_id")]
        public string? ChallengeId { get; set; }

        [JsonPropertyName("approval_id")]
        public string? ApprovalId { get; set; }

        [JsonPropertyName("policy_set_id")]
        public string? PolicySetId { get; set; }

        [JsonPropertyName("details")]
        public AuditEventDetails Details { get; set; } = new();

        [JsonPropertyName("prev_hash")]
        public string PrevHash { get; set; } = string.Empty;

        [JsonPropertyName("this_hash")]
        public string ThisHash { get; set; } = string.Empty;

        [JsonPropertyName("device_id")]
        public string? DeviceId { get; set; }

        [JsonPropertyName("session_id")]
        public string? SessionId { get; set; }

        public string ComputeHash()
        {
            var payload = new
            {
                event_id = EventId,
                ts = Timestamp.ToString("O"),
                type = Type.ToString(),
                identity_id = IdentityId,
                challenge_id = ChallengeId,
                approval_id = ApprovalId,
                policy_set_id = PolicySetId,
                details = Details,
                prev_hash = PrevHash,
                device_id = DeviceId,
                session_id = SessionId
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(json));
            return $"sha256:{Convert.ToBase64String(hashBytes)}";
        }

        public bool VerifyHash()
        {
            return ThisHash == ComputeHash();
        }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AuditEventType
    {

        ChallengeCreated,

        ChallengeExpired,

        ChallengeCancelled,

        ApprovalIssued,

        ApprovalDenied,

        ApprovalUsed,

        ApprovalExpired,

        PolicyViolation,

        PolicyModified,

        PolicyEvaluated,

        VaultOpened,

        VaultClosed,

        VaultRefAdded,

        IdentityUsed,

        IdentityCreated,

        IdentityModified,

        IdentityDeleted,

        IdentityRevoked,

        IdentityExpired,

        SessionStarted,

        SessionEnded,

        ReauthRequired,

        ReauthCompleted,

        ThreatDetected,

        SecurityWarning,

        RateLimitTriggered,

        UsbRemoved,

        UsbInserted,

        ProfileModified,

        AuditExported,

        ChainVerified
    }

    public sealed class AuditEventDetails
    {

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("operation")]
        public string? Operation { get; set; }

        [JsonPropertyName("result")]
        public string? Result { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("severity")]
        public AuditSeverity Severity { get; set; } = AuditSeverity.Info;

        [JsonPropertyName("requester_type")]
        public string? RequesterType { get; set; }

        [JsonPropertyName("requester_name")]
        public string? RequesterName { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AuditSeverity
    {

        Debug,

        Info,

        Warning,

        Error,

        Critical
    }

    public sealed class AuditLog
    {

        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("profile_id")]
        public string ProfileId { get; set; } = string.Empty;

        [JsonPropertyName("events")]
        public List<AuditEvent> Events { get; set; } = new();

        [JsonPropertyName("max_events")]
        public int MaxEvents { get; set; } = 10000;

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("last_event_at")]
        public DateTimeOffset? LastEventAt { get; set; }

        [JsonPropertyName("chain_head_hash")]
        public string? ChainHeadHash { get; set; }

        [JsonPropertyName("total_event_count")]
        public long TotalEventCount { get; set; } = 0;

        public AuditEvent AddEvent(AuditEventType type, Action<AuditEvent>? configure = null)
        {
            var evt = new AuditEvent
            {
                Type = type,
                PrevHash = ChainHeadHash ?? "genesis"
            };

            configure?.Invoke(evt);

            evt.ThisHash = evt.ComputeHash();
            Events.Add(evt);
            ChainHeadHash = evt.ThisHash;
            LastEventAt = evt.Timestamp;
            TotalEventCount++;

            while (Events.Count > MaxEvents)
            {
                Events.RemoveAt(0);
            }

            return evt;
        }

        public ChainVerificationResult VerifyChain()
        {
            var result = new ChainVerificationResult
            {
                VerifiedAt = DateTimeOffset.UtcNow,
                TotalEvents = Events.Count
            };

            if (Events.Count == 0)
            {
                result.IsValid = true;
                return result;
            }

            string? expectedPrevHash = "genesis";

            for (int i = 0; i < Events.Count; i++)
            {
                var evt = Events[i];

                if (evt.PrevHash != expectedPrevHash)
                {
                    result.IsValid = false;
                    result.FirstBrokenIndex = i;
                    result.BrokenEventId = evt.EventId;
                    result.ErrorMessage = $"Chain broken at index {i}: expected prev_hash {expectedPrevHash}, got {evt.PrevHash}";
                    return result;
                }

                if (!evt.VerifyHash())
                {
                    result.IsValid = false;
                    result.FirstBrokenIndex = i;
                    result.BrokenEventId = evt.EventId;
                    result.ErrorMessage = $"Hash mismatch at index {i}: event {evt.EventId} has been tampered";
                    return result;
                }

                expectedPrevHash = evt.ThisHash;
            }

            result.IsValid = true;
            result.ChainHeadHash = Events[^1].ThisHash;
            return result;
        }
    }

    public sealed class ChainVerificationResult
    {

        [JsonPropertyName("is_valid")]
        public bool IsValid { get; set; }

        [JsonPropertyName("verified_at")]
        public DateTimeOffset VerifiedAt { get; set; }

        [JsonPropertyName("total_events")]
        public int TotalEvents { get; set; }

        [JsonPropertyName("first_broken_index")]
        public int? FirstBrokenIndex { get; set; }

        [JsonPropertyName("broken_event_id")]
        public string? BrokenEventId { get; set; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("chain_head_hash")]
        public string? ChainHeadHash { get; set; }
    }

    public sealed class AuditExportBundle
    {

        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("profile_id")]
        public string ProfileId { get; set; } = string.Empty;

        [JsonPropertyName("exported_at")]
        public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("time_range")]
        public TimeRange TimeRange { get; set; } = new();

        [JsonPropertyName("events")]
        public List<AuditEvent> Events { get; set; } = new();

        [JsonPropertyName("chain_verification")]
        public ChainVerificationResult ChainVerification { get; set; } = new();

        [JsonPropertyName("signature")]
        public string? Signature { get; set; }

        [JsonPropertyName("signing_key_id")]
        public string? SigningKeyId { get; set; }
    }

    public sealed class TimeRange
    {

        [JsonPropertyName("from")]
        public DateTimeOffset? From { get; set; }

        [JsonPropertyName("to")]
        public DateTimeOffset? To { get; set; }
    }
}

