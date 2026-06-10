using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.Attestor
{

    public sealed class PolicySet
    {

        [JsonPropertyName("policy_set_id")]
        public string PolicySetId { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("applies_to")]
        public List<string> AppliesTo { get; set; } = new();

        [JsonPropertyName("rules")]
        public List<PolicyRule> Rules { get; set; } = new();

        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("priority")]
        public int Priority { get; set; } = 0;
    }

    public sealed class PolicyRule
    {

        [JsonPropertyName("rule_id")]
        public string RuleId { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("type")]
        public PolicyRuleType Type { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("params")]
        public PolicyRuleParams Params { get; set; } = new();

        [JsonPropertyName("severity_on_violation")]
        public ViolationSeverity SeverityOnViolation { get; set; } = ViolationSeverity.Block;

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("violation_message")]
        public string? ViolationMessage { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PolicyRuleType
    {

        RequireUsbPresence,

        ReauthEverySeconds,

        ApprovalTtlSeconds,

        AllowOfflineOnly,

        ClipboardControls,

        DenyIfProcessNotAllowlisted,

        OriginBindingEnforced,

        RateLimit,

        DenyIfHostileOsFlagged,

        RequireHardwareToken,

        RequireDeviceBinding,

        TimeRestriction,

        GeoRestriction,

        RequireMfa,

        DenyAiAccess,

        RequireExplicitConfirmation,

        MaxTotalUses,

        SingleUse
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ViolationSeverity
    {

        Log,

        Warn,

        Block,

        BlockAndLock,

        BlockAndRevoke
    }

    public sealed class PolicyRuleParams
    {

        [JsonPropertyName("required")]
        public bool? Required { get; set; }

        [JsonPropertyName("seconds")]
        public int? Seconds { get; set; }

        [JsonPropertyName("max_count")]
        public int? MaxCount { get; set; }

        [JsonPropertyName("window_seconds")]
        public int? WindowSeconds { get; set; }

        [JsonPropertyName("allow_copy")]
        public bool? AllowCopy { get; set; }

        [JsonPropertyName("allow_autofill")]
        public bool? AllowAutofill { get; set; }

        [JsonPropertyName("clear_after_seconds")]
        public int? ClearAfterSeconds { get; set; }

        [JsonPropertyName("allowed_processes")]
        public List<string>? AllowedProcesses { get; set; }

        [JsonPropertyName("allowed_origins")]
        public List<string>? AllowedOrigins { get; set; }

        [JsonPropertyName("allowed_times")]
        public List<string>? AllowedTimes { get; set; }

        [JsonPropertyName("denied_times")]
        public List<string>? DeniedTimes { get; set; }

        [JsonPropertyName("allowed_regions")]
        public List<string>? AllowedRegions { get; set; }

        [JsonPropertyName("denied_regions")]
        public List<string>? DeniedRegions { get; set; }

        [JsonPropertyName("allowed_devices")]
        public List<string>? AllowedDevices { get; set; }

        [JsonPropertyName("required_flag")]
        public bool? RequiredFlag { get; set; }

        [JsonPropertyName("required_factors")]
        public List<string>? RequiredFactors { get; set; }

        [JsonPropertyName("min_factors")]
        public int? MinFactors { get; set; }
    }

    public sealed class PolicyEvaluation
    {

        [JsonPropertyName("result")]
        public PolicyEvalResult Result { get; set; } = PolicyEvalResult.Pending;

        [JsonPropertyName("violations")]
        public List<PolicyViolation> Violations { get; set; } = new();

        [JsonPropertyName("passed_rules")]
        public List<string> PassedRules { get; set; } = new();

        [JsonPropertyName("evaluated_at")]
        public DateTimeOffset EvaluatedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("policy_snapshot_hash")]
        public string? PolicySnapshotHash { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PolicyEvalResult
    {

        Pending,

        Allow,

        AllowWithWarnings,

        Deny
    }

    public sealed class PolicyViolation
    {

        [JsonPropertyName("rule_id")]
        public string RuleId { get; set; } = string.Empty;

        [JsonPropertyName("rule_type")]
        public PolicyRuleType RuleType { get; set; }

        [JsonPropertyName("severity")]
        public ViolationSeverity Severity { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("details")]
        public Dictionary<string, string> Details { get; set; } = new();
    }
}

