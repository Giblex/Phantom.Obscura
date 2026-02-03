using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.Attestor
{
    /// <summary>
    /// Append-only, hash-chained audit event for tamper-evident logging.
    /// Stored locally, exportable as a signed report.
    /// Privacy-first: no PII, just operational evidence.
    /// </summary>
    public sealed class AuditEvent
    {
        /// <summary>
        /// Unique identifier for this event (UUIDv7 for time-sortability).
        /// </summary>
        [JsonPropertyName("event_id")]
        public string EventId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Timestamp when this event occurred.
        /// </summary>
        [JsonPropertyName("ts")]
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Type of audit event.
        /// </summary>
        [JsonPropertyName("type")]
        public AuditEventType Type { get; set; }

        /// <summary>
        /// Identity ID involved in this event (if applicable).
        /// </summary>
        [JsonPropertyName("identity_id")]
        public string? IdentityId { get; set; }

        /// <summary>
        /// Challenge ID involved in this event (if applicable).
        /// </summary>
        [JsonPropertyName("challenge_id")]
        public string? ChallengeId { get; set; }

        /// <summary>
        /// Approval ID involved in this event (if applicable).
        /// </summary>
        [JsonPropertyName("approval_id")]
        public string? ApprovalId { get; set; }

        /// <summary>
        /// Policy set ID involved in this event (if applicable).
        /// </summary>
        [JsonPropertyName("policy_set_id")]
        public string? PolicySetId { get; set; }

        /// <summary>
        /// Details about the event (privacy-conscious summaries only).
        /// </summary>
        [JsonPropertyName("details")]
        public AuditEventDetails Details { get; set; } = new();

        /// <summary>
        /// SHA-256 hash of the previous event in the chain.
        /// </summary>
        [JsonPropertyName("prev_hash")]
        public string PrevHash { get; set; } = string.Empty;

        /// <summary>
        /// SHA-256 hash of this event (computed over all fields except this_hash).
        /// </summary>
        [JsonPropertyName("this_hash")]
        public string ThisHash { get; set; } = string.Empty;

        /// <summary>
        /// Device ID where this event occurred.
        /// </summary>
        [JsonPropertyName("device_id")]
        public string? DeviceId { get; set; }

        /// <summary>
        /// Session ID if this event is part of a session.
        /// </summary>
        [JsonPropertyName("session_id")]
        public string? SessionId { get; set; }

        /// <summary>
        /// Computes the hash for this event (for chain integrity).
        /// </summary>
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

        /// <summary>
        /// Verifies that this event's hash is valid.
        /// </summary>
        public bool VerifyHash()
        {
            return ThisHash == ComputeHash();
        }
    }

    /// <summary>
    /// Types of audit events.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AuditEventType
    {
        // --- Challenge lifecycle ---

        /// <summary>
        /// A new challenge was created.
        /// </summary>
        ChallengeCreated,

        /// <summary>
        /// A challenge expired without decision.
        /// </summary>
        ChallengeExpired,

        /// <summary>
        /// A challenge was cancelled.
        /// </summary>
        ChallengeCancelled,

        // --- Approval lifecycle ---

        /// <summary>
        /// An approval was issued.
        /// </summary>
        ApprovalIssued,

        /// <summary>
        /// An approval was denied by user.
        /// </summary>
        ApprovalDenied,

        /// <summary>
        /// An approval artifact was used.
        /// </summary>
        ApprovalUsed,

        /// <summary>
        /// An approval expired.
        /// </summary>
        ApprovalExpired,

        // --- Policy events ---

        /// <summary>
        /// A policy rule was violated.
        /// </summary>
        PolicyViolation,

        /// <summary>
        /// A policy set was modified.
        /// </summary>
        PolicyModified,

        /// <summary>
        /// Policy evaluation completed.
        /// </summary>
        PolicyEvaluated,

        // --- Vault events ---

        /// <summary>
        /// A vault was opened/unlocked.
        /// </summary>
        VaultOpened,

        /// <summary>
        /// A vault was closed/locked.
        /// </summary>
        VaultClosed,

        /// <summary>
        /// A vault reference was added.
        /// </summary>
        VaultRefAdded,

        // --- Identity events ---

        /// <summary>
        /// An identity was used (TOTP generated, key signed, etc.).
        /// </summary>
        IdentityUsed,

        /// <summary>
        /// An identity was created.
        /// </summary>
        IdentityCreated,

        /// <summary>
        /// An identity was modified.
        /// </summary>
        IdentityModified,

        /// <summary>
        /// An identity was deleted.
        /// </summary>
        IdentityDeleted,

        /// <summary>
        /// An identity was revoked.
        /// </summary>
        IdentityRevoked,

        /// <summary>
        /// An identity expired.
        /// </summary>
        IdentityExpired,

        // --- Session events ---

        /// <summary>
        /// A session was started.
        /// </summary>
        SessionStarted,

        /// <summary>
        /// A session was ended.
        /// </summary>
        SessionEnded,

        /// <summary>
        /// Re-authentication was required.
        /// </summary>
        ReauthRequired,

        /// <summary>
        /// Re-authentication was completed.
        /// </summary>
        ReauthCompleted,

        // --- Security events ---

        /// <summary>
        /// A threat was detected.
        /// </summary>
        ThreatDetected,

        /// <summary>
        /// A security warning was raised.
        /// </summary>
        SecurityWarning,

        /// <summary>
        /// A rate limit was triggered.
        /// </summary>
        RateLimitTriggered,

        /// <summary>
        /// USB token was removed.
        /// </summary>
        UsbRemoved,

        /// <summary>
        /// USB token was inserted.
        /// </summary>
        UsbInserted,

        // --- System events ---

        /// <summary>
        /// Profile was created or modified.
        /// </summary>
        ProfileModified,

        /// <summary>
        /// Audit log was exported.
        /// </summary>
        AuditExported,

        /// <summary>
        /// Chain integrity verification completed.
        /// </summary>
        ChainVerified
    }

    /// <summary>
    /// Details about an audit event (privacy-conscious).
    /// </summary>
    public sealed class AuditEventDetails
    {
        /// <summary>
        /// Brief summary of what happened.
        /// </summary>
        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        /// <summary>
        /// Operation that was performed (if applicable).
        /// </summary>
        [JsonPropertyName("operation")]
        public string? Operation { get; set; }

        /// <summary>
        /// Result of the operation (success/failure).
        /// </summary>
        [JsonPropertyName("result")]
        public string? Result { get; set; }

        /// <summary>
        /// Reason for failure or denial (if applicable).
        /// </summary>
        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        /// <summary>
        /// Severity level of this event.
        /// </summary>
        [JsonPropertyName("severity")]
        public AuditSeverity Severity { get; set; } = AuditSeverity.Info;

        /// <summary>
        /// Requester type (if applicable).
        /// </summary>
        [JsonPropertyName("requester_type")]
        public string? RequesterType { get; set; }

        /// <summary>
        /// Requester name (if applicable).
        /// </summary>
        [JsonPropertyName("requester_name")]
        public string? RequesterName { get; set; }

        /// <summary>
        /// Additional key-value metadata.
        /// </summary>
        [JsonPropertyName("metadata")]
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Severity levels for audit events.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AuditSeverity
    {
        /// <summary>
        /// Debug-level information.
        /// </summary>
        Debug,

        /// <summary>
        /// Normal operational information.
        /// </summary>
        Info,

        /// <summary>
        /// Warning that may need attention.
        /// </summary>
        Warning,

        /// <summary>
        /// Error that needs attention.
        /// </summary>
        Error,

        /// <summary>
        /// Critical security event.
        /// </summary>
        Critical
    }

    /// <summary>
    /// Collection of audit events with chain integrity tracking.
    /// </summary>
    public sealed class AuditLog
    {
        /// <summary>
        /// Version of the audit log schema.
        /// </summary>
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        /// <summary>
        /// Profile ID this audit log belongs to.
        /// </summary>
        [JsonPropertyName("profile_id")]
        public string ProfileId { get; set; } = string.Empty;

        /// <summary>
        /// Events in this log (most recent last).
        /// </summary>
        [JsonPropertyName("events")]
        public List<AuditEvent> Events { get; set; } = new();

        /// <summary>
        /// Maximum events to retain before rotation.
        /// </summary>
        [JsonPropertyName("max_events")]
        public int MaxEvents { get; set; } = 10000;

        /// <summary>
        /// Timestamp when log was created.
        /// </summary>
        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Timestamp of last event.
        /// </summary>
        [JsonPropertyName("last_event_at")]
        public DateTimeOffset? LastEventAt { get; set; }

        /// <summary>
        /// Hash of the last event (chain head).
        /// </summary>
        [JsonPropertyName("chain_head_hash")]
        public string? ChainHeadHash { get; set; }

        /// <summary>
        /// Total events ever recorded (including rotated).
        /// </summary>
        [JsonPropertyName("total_event_count")]
        public long TotalEventCount { get; set; } = 0;

        /// <summary>
        /// Adds a new event to the log with proper hash chaining.
        /// </summary>
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

            // Rotate if over limit
            while (Events.Count > MaxEvents)
            {
                Events.RemoveAt(0);
            }

            return evt;
        }

        /// <summary>
        /// Verifies the integrity of the entire hash chain.
        /// </summary>
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

                // Check prev_hash links correctly
                if (evt.PrevHash != expectedPrevHash)
                {
                    result.IsValid = false;
                    result.FirstBrokenIndex = i;
                    result.BrokenEventId = evt.EventId;
                    result.ErrorMessage = $"Chain broken at index {i}: expected prev_hash {expectedPrevHash}, got {evt.PrevHash}";
                    return result;
                }

                // Verify this event's hash
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

    /// <summary>
    /// Result of verifying the audit chain.
    /// </summary>
    public sealed class ChainVerificationResult
    {
        /// <summary>
        /// Whether the chain is valid.
        /// </summary>
        [JsonPropertyName("is_valid")]
        public bool IsValid { get; set; }

        /// <summary>
        /// Timestamp when verification was performed.
        /// </summary>
        [JsonPropertyName("verified_at")]
        public DateTimeOffset VerifiedAt { get; set; }

        /// <summary>
        /// Total events verified.
        /// </summary>
        [JsonPropertyName("total_events")]
        public int TotalEvents { get; set; }

        /// <summary>
        /// Index of first broken link (if any).
        /// </summary>
        [JsonPropertyName("first_broken_index")]
        public int? FirstBrokenIndex { get; set; }

        /// <summary>
        /// Event ID where chain broke (if any).
        /// </summary>
        [JsonPropertyName("broken_event_id")]
        public string? BrokenEventId { get; set; }

        /// <summary>
        /// Error message (if chain is broken).
        /// </summary>
        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Hash of the chain head (if valid).
        /// </summary>
        [JsonPropertyName("chain_head_hash")]
        public string? ChainHeadHash { get; set; }
    }

    /// <summary>
    /// Export bundle for audit reports (no secrets).
    /// </summary>
    public sealed class AuditExportBundle
    {
        /// <summary>
        /// Version of the export format.
        /// </summary>
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        /// <summary>
        /// Profile ID this export is from.
        /// </summary>
        [JsonPropertyName("profile_id")]
        public string ProfileId { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp when export was created.
        /// </summary>
        [JsonPropertyName("exported_at")]
        public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Time range covered by this export.
        /// </summary>
        [JsonPropertyName("time_range")]
        public TimeRange TimeRange { get; set; } = new();

        /// <summary>
        /// Audit events in this export.
        /// </summary>
        [JsonPropertyName("events")]
        public List<AuditEvent> Events { get; set; } = new();

        /// <summary>
        /// Chain verification result at time of export.
        /// </summary>
        [JsonPropertyName("chain_verification")]
        public ChainVerificationResult ChainVerification { get; set; } = new();

        /// <summary>
        /// Ed25519 signature of this export (base64).
        /// </summary>
        [JsonPropertyName("signature")]
        public string? Signature { get; set; }

        /// <summary>
        /// Key ID used for signing.
        /// </summary>
        [JsonPropertyName("signing_key_id")]
        public string? SigningKeyId { get; set; }
    }

    /// <summary>
    /// Time range for audit queries/exports.
    /// </summary>
    public sealed class TimeRange
    {
        /// <summary>
        /// Start of time range.
        /// </summary>
        [JsonPropertyName("from")]
        public DateTimeOffset? From { get; set; }

        /// <summary>
        /// End of time range.
        /// </summary>
        [JsonPropertyName("to")]
        public DateTimeOffset? To { get; set; }
    }
}
