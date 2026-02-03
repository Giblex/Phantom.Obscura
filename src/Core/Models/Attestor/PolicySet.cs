using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.Attestor
{
    /// <summary>
    /// Policy set attached to an Identity or IdentityGroup.
    /// Policies are evaluated whenever Attestor generates/approves anything.
    /// Rules are stable schema; add new rule types over time.
    /// </summary>
    public sealed class PolicySet
    {
        /// <summary>
        /// Unique identifier for this policy set.
        /// </summary>
        [JsonPropertyName("policy_set_id")]
        public string PolicySetId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Human-readable name for this policy set.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Description of what this policy set enforces.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// References to entities this policy applies to (identity:uuid, group:uuid).
        /// </summary>
        [JsonPropertyName("applies_to")]
        public List<string> AppliesTo { get; set; } = new();

        /// <summary>
        /// Rules in this policy set.
        /// </summary>
        [JsonPropertyName("rules")]
        public List<PolicyRule> Rules { get; set; } = new();

        /// <summary>
        /// Version of this policy set (incremented on changes).
        /// </summary>
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        /// <summary>
        /// Timestamp when this policy set was created.
        /// </summary>
        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Timestamp when this policy set was last updated.
        /// </summary>
        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Whether this policy set is currently enabled.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Priority for conflict resolution (higher = more important).
        /// </summary>
        [JsonPropertyName("priority")]
        public int Priority { get; set; } = 0;
    }

    /// <summary>
    /// A single policy rule within a PolicySet.
    /// </summary>
    public sealed class PolicyRule
    {
        /// <summary>
        /// Unique identifier for this rule.
        /// </summary>
        [JsonPropertyName("rule_id")]
        public string RuleId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Type of rule (determines which parameters are valid).
        /// </summary>
        [JsonPropertyName("type")]
        public PolicyRuleType Type { get; set; }

        /// <summary>
        /// Human-readable description of what this rule does.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// Rule parameters (type-specific).
        /// </summary>
        [JsonPropertyName("params")]
        public PolicyRuleParams Params { get; set; } = new();

        /// <summary>
        /// What happens when this rule is violated.
        /// </summary>
        [JsonPropertyName("severity_on_violation")]
        public ViolationSeverity SeverityOnViolation { get; set; } = ViolationSeverity.Block;

        /// <summary>
        /// Whether this rule is currently enabled.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Optional message to display when rule is violated.
        /// </summary>
        [JsonPropertyName("violation_message")]
        public string? ViolationMessage { get; set; }
    }

    /// <summary>
    /// Types of policy rules supported day one.
    /// Stable schema - add new types over time.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PolicyRuleType
    {
        /// <summary>
        /// Require USB token to be physically present.
        /// </summary>
        RequireUsbPresence,

        /// <summary>
        /// Require re-authentication after specified seconds.
        /// </summary>
        ReauthEverySeconds,

        /// <summary>
        /// Set TTL for approval artifacts.
        /// </summary>
        ApprovalTtlSeconds,

        /// <summary>
        /// Allow only offline operation (no network).
        /// </summary>
        AllowOfflineOnly,

        /// <summary>
        /// Control clipboard operations (copy, autofill).
        /// </summary>
        ClipboardControls,

        /// <summary>
        /// Deny if process is not in allowlist.
        /// </summary>
        DenyIfProcessNotAllowlisted,

        /// <summary>
        /// Enforce origin binding for web operations.
        /// </summary>
        OriginBindingEnforced,

        /// <summary>
        /// Rate limit approvals (max N per time window).
        /// </summary>
        RateLimit,

        /// <summary>
        /// Deny if hostile OS flag is set in threat assumptions.
        /// </summary>
        DenyIfHostileOsFlagged,

        /// <summary>
        /// Require hardware token presence.
        /// </summary>
        RequireHardwareToken,

        /// <summary>
        /// Require specific device binding.
        /// </summary>
        RequireDeviceBinding,

        /// <summary>
        /// Deny during specified time windows.
        /// </summary>
        TimeRestriction,

        /// <summary>
        /// Deny from specific geographic locations.
        /// </summary>
        GeoRestriction,

        /// <summary>
        /// Require multi-factor authentication.
        /// </summary>
        RequireMfa,

        /// <summary>
        /// Deny AI access to this identity.
        /// </summary>
        DenyAiAccess,

        /// <summary>
        /// Require explicit user confirmation (no auto-approve).
        /// </summary>
        RequireExplicitConfirmation,

        /// <summary>
        /// Limit total uses of this identity.
        /// </summary>
        MaxTotalUses,

        /// <summary>
        /// Expire after first use.
        /// </summary>
        SingleUse
    }

    /// <summary>
    /// What happens when a policy rule is violated.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ViolationSeverity
    {
        /// <summary>
        /// Log the violation but allow the action.
        /// </summary>
        Log,

        /// <summary>
        /// Warn the user but allow them to proceed.
        /// </summary>
        Warn,

        /// <summary>
        /// Block the action entirely.
        /// </summary>
        Block,

        /// <summary>
        /// Block and lock the identity temporarily.
        /// </summary>
        BlockAndLock,

        /// <summary>
        /// Block and revoke the identity permanently.
        /// </summary>
        BlockAndRevoke
    }

    /// <summary>
    /// Parameters for policy rules (type-specific fields).
    /// Not all fields apply to all rule types.
    /// </summary>
    public sealed class PolicyRuleParams
    {
        // --- General ---

        /// <summary>
        /// Boolean requirement (for simple required/not-required rules).
        /// </summary>
        [JsonPropertyName("required")]
        public bool? Required { get; set; }

        // --- Time-based ---

        /// <summary>
        /// Number of seconds (for reauth, TTL rules).
        /// </summary>
        [JsonPropertyName("seconds")]
        public int? Seconds { get; set; }

        // --- Rate limiting ---

        /// <summary>
        /// Maximum count (for rate limiting, max uses).
        /// </summary>
        [JsonPropertyName("max_count")]
        public int? MaxCount { get; set; }

        /// <summary>
        /// Time window in seconds (for rate limiting).
        /// </summary>
        [JsonPropertyName("window_seconds")]
        public int? WindowSeconds { get; set; }

        // --- Clipboard ---

        /// <summary>
        /// Allow copy to clipboard.
        /// </summary>
        [JsonPropertyName("allow_copy")]
        public bool? AllowCopy { get; set; }

        /// <summary>
        /// Allow autofill operations.
        /// </summary>
        [JsonPropertyName("allow_autofill")]
        public bool? AllowAutofill { get; set; }

        /// <summary>
        /// Auto-clear clipboard after seconds.
        /// </summary>
        [JsonPropertyName("clear_after_seconds")]
        public int? ClearAfterSeconds { get; set; }

        // --- Process allowlist ---

        /// <summary>
        /// Allowed process names or hashes.
        /// </summary>
        [JsonPropertyName("allowed_processes")]
        public List<string>? AllowedProcesses { get; set; }

        // --- Origin binding ---

        /// <summary>
        /// Allowed origins (URLs, domains).
        /// </summary>
        [JsonPropertyName("allowed_origins")]
        public List<string>? AllowedOrigins { get; set; }

        // --- Time/geo restrictions ---

        /// <summary>
        /// Allowed time windows (e.g., "09:00-17:00").
        /// </summary>
        [JsonPropertyName("allowed_times")]
        public List<string>? AllowedTimes { get; set; }

        /// <summary>
        /// Denied time windows.
        /// </summary>
        [JsonPropertyName("denied_times")]
        public List<string>? DeniedTimes { get; set; }

        /// <summary>
        /// Allowed geographic regions (country codes).
        /// </summary>
        [JsonPropertyName("allowed_regions")]
        public List<string>? AllowedRegions { get; set; }

        /// <summary>
        /// Denied geographic regions.
        /// </summary>
        [JsonPropertyName("denied_regions")]
        public List<string>? DeniedRegions { get; set; }

        // --- Device binding ---

        /// <summary>
        /// Allowed device fingerprints.
        /// </summary>
        [JsonPropertyName("allowed_devices")]
        public List<string>? AllowedDevices { get; set; }

        // --- Threat flags ---

        /// <summary>
        /// Required threat flag value (for deny-if-flagged rules).
        /// </summary>
        [JsonPropertyName("required_flag")]
        public bool? RequiredFlag { get; set; }

        // --- MFA ---

        /// <summary>
        /// Required MFA factors (e.g., ["totp", "usb"]).
        /// </summary>
        [JsonPropertyName("required_factors")]
        public List<string>? RequiredFactors { get; set; }

        /// <summary>
        /// Minimum number of factors required.
        /// </summary>
        [JsonPropertyName("min_factors")]
        public int? MinFactors { get; set; }
    }

    /// <summary>
    /// Result of evaluating a policy set against a challenge.
    /// </summary>
    public sealed class PolicyEvaluation
    {
        /// <summary>
        /// Overall result of the evaluation.
        /// </summary>
        [JsonPropertyName("result")]
        public PolicyEvalResult Result { get; set; } = PolicyEvalResult.Pending;

        /// <summary>
        /// List of rule violations found.
        /// </summary>
        [JsonPropertyName("violations")]
        public List<PolicyViolation> Violations { get; set; } = new();

        /// <summary>
        /// List of rules that passed.
        /// </summary>
        [JsonPropertyName("passed_rules")]
        public List<string> PassedRules { get; set; } = new();

        /// <summary>
        /// Timestamp when evaluation was performed.
        /// </summary>
        [JsonPropertyName("evaluated_at")]
        public DateTimeOffset EvaluatedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Hash of the policy set used for evaluation (for audit).
        /// </summary>
        [JsonPropertyName("policy_snapshot_hash")]
        public string? PolicySnapshotHash { get; set; }
    }

    /// <summary>
    /// Result of policy evaluation.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PolicyEvalResult
    {
        /// <summary>
        /// Evaluation not yet complete.
        /// </summary>
        Pending,

        /// <summary>
        /// All rules passed.
        /// </summary>
        Allow,

        /// <summary>
        /// Some rules warn but allow.
        /// </summary>
        AllowWithWarnings,

        /// <summary>
        /// One or more blocking rules violated.
        /// </summary>
        Deny
    }

    /// <summary>
    /// Details about a policy rule violation.
    /// </summary>
    public sealed class PolicyViolation
    {
        /// <summary>
        /// ID of the rule that was violated.
        /// </summary>
        [JsonPropertyName("rule_id")]
        public string RuleId { get; set; } = string.Empty;

        /// <summary>
        /// Type of the violated rule.
        /// </summary>
        [JsonPropertyName("rule_type")]
        public PolicyRuleType RuleType { get; set; }

        /// <summary>
        /// Severity of this violation.
        /// </summary>
        [JsonPropertyName("severity")]
        public ViolationSeverity Severity { get; set; }

        /// <summary>
        /// Human-readable message about the violation.
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Additional context about the violation.
        /// </summary>
        [JsonPropertyName("details")]
        public Dictionary<string, string> Details { get; set; } = new();
    }
}
