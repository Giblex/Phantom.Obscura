using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.Security
{
    /// <summary>
    /// AI-aware access policy that actively defends against AI leakage.
    /// Unlike most vaults that ignore AI risks, Phantom explicitly blocks
    /// secret exposure to AI tools while remaining AI-aware for future features.
    /// </summary>
    public sealed class AIAccessPolicy
    {
        /// <summary>
        /// Version of the AI access policy schema.
        /// </summary>
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        /// <summary>
        /// Master switch: when false (default), all AI access is blocked.
        /// This is the secure default - AI must be explicitly enabled.
        /// </summary>
        [JsonPropertyName("allowAiAccess")]
        public bool AllowAiAccess { get; set; } = false;

        /// <summary>
        /// Block clipboard sharing to detected AI tool contexts.
        /// Prevents accidental paste of secrets into AI chat interfaces.
        /// </summary>
        [JsonPropertyName("blockClipboardToAi")]
        public bool BlockClipboardToAi { get; set; } = true;

        /// <summary>
        /// Block autofill into browser contexts flagged as AI chat interfaces.
        /// Detected via URL patterns, DOM analysis, or known AI service domains.
        /// </summary>
        [JsonPropertyName("blockAutofillToAi")]
        public bool BlockAutofillToAi { get; set; } = true;

        /// <summary>
        /// Enable AI-safe view mode: shows masked placeholders instead of raw values.
        /// Useful when screen sharing or working near AI transcription tools.
        /// </summary>
        [JsonPropertyName("enableAiSafeView")]
        public bool EnableAiSafeView { get; set; } = false;

        /// <summary>
        /// When AI-safe view is active, show this placeholder instead of secrets.
        /// </summary>
        [JsonPropertyName("aiSafePlaceholder")]
        public string AiSafePlaceholder { get; set; } = "[REDACTED]";

        /// <summary>
        /// Detected AI service domains to block. Updated periodically.
        /// </summary>
        [JsonPropertyName("blockedAiDomains")]
        public List<string> BlockedAiDomains { get; set; } = new()
        {
            "chat.openai.com",
            "chatgpt.com",
            "claude.ai",
            "anthropic.com",
            "bard.google.com",
            "gemini.google.com",
            "copilot.microsoft.com",
            "bing.com/chat",
            "perplexity.ai",
            "poe.com",
            "character.ai",
            "pi.ai",
            "huggingface.co/chat",
            "you.com",
            "phind.com",
            "together.ai"
        };

        /// <summary>
        /// Additional user-defined domains to block for AI access.
        /// </summary>
        [JsonPropertyName("customBlockedDomains")]
        public List<string> CustomBlockedDomains { get; set; } = new();

        /// <summary>
        /// Window title patterns that indicate AI context (for clipboard blocking).
        /// </summary>
        [JsonPropertyName("aiWindowPatterns")]
        public List<string> AiWindowPatterns { get; set; } = new()
        {
            "*ChatGPT*",
            "*Claude*",
            "*Copilot*",
            "*Gemini*",
            "*AI Assistant*",
            "*AI Chat*"
        };

        /// <summary>
        /// Log all AI access attempts for audit purposes.
        /// </summary>
        [JsonPropertyName("logAiAccessAttempts")]
        public bool LogAiAccessAttempts { get; set; } = true;

        /// <summary>
        /// Require explicit user confirmation before any AI-related operation.
        /// </summary>
        [JsonPropertyName("requireAiConsentPrompt")]
        public bool RequireAiConsentPrompt { get; set; } = true;

        /// <summary>
        /// Future: allow AI to access non-sensitive metadata only.
        /// </summary>
        [JsonPropertyName("allowAiMetadataAccess")]
        public bool AllowAiMetadataAccess { get; set; } = false;

        /// <summary>
        /// Future: specific AI services that may be granted limited access.
        /// </summary>
        [JsonPropertyName("allowedAiServices")]
        public List<AllowedAiService> AllowedAiServices { get; set; } = new();

        /// <summary>
        /// Timestamp when policy was last updated.
        /// </summary>
        [JsonPropertyName("lastUpdatedUtc")]
        public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Configuration for a specifically allowed AI service (future use).
    /// </summary>
    public sealed class AllowedAiService
    {
        /// <summary>
        /// Identifier for the AI service.
        /// </summary>
        [JsonPropertyName("serviceId")]
        public string ServiceId { get; set; } = string.Empty;

        /// <summary>
        /// Display name of the service.
        /// </summary>
        [JsonPropertyName("serviceName")]
        public string ServiceName { get; set; } = string.Empty;

        /// <summary>
        /// Domains associated with this service.
        /// </summary>
        [JsonPropertyName("domains")]
        public List<string> Domains { get; set; } = new();

        /// <summary>
        /// What level of access is permitted.
        /// </summary>
        [JsonPropertyName("accessLevel")]
        public AiAccessLevel AccessLevel { get; set; } = AiAccessLevel.None;

        /// <summary>
        /// Whether this service requires explicit consent per-use.
        /// </summary>
        [JsonPropertyName("requiresPerUseConsent")]
        public bool RequiresPerUseConsent { get; set; } = true;

        /// <summary>
        /// Expiration date for this service's access grant.
        /// </summary>
        [JsonPropertyName("expiresUtc")]
        public DateTimeOffset? ExpiresUtc { get; set; }
    }

    /// <summary>
    /// Levels of AI access that may be granted.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AiAccessLevel
    {
        /// <summary>
        /// No access permitted.
        /// </summary>
        None,

        /// <summary>
        /// Can access non-sensitive metadata (site names, categories).
        /// </summary>
        MetadataOnly,

        /// <summary>
        /// Can access usernames but not passwords.
        /// </summary>
        UsernamesOnly,

        /// <summary>
        /// Full access (dangerous - requires explicit consent).
        /// </summary>
        Full
    }
}
