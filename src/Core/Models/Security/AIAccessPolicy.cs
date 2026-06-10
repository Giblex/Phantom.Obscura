using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.Security
{

    public sealed class AIAccessPolicy
    {

        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("allowAiAccess")]
        public bool AllowAiAccess { get; set; } = false;

        [JsonPropertyName("blockClipboardToAi")]
        public bool BlockClipboardToAi { get; set; } = true;

        [JsonPropertyName("blockAutofillToAi")]
        public bool BlockAutofillToAi { get; set; } = true;

        [JsonPropertyName("enableAiSafeView")]
        public bool EnableAiSafeView { get; set; } = false;

        [JsonPropertyName("aiSafePlaceholder")]
        public string AiSafePlaceholder { get; set; } = "[REDACTED]";

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

        [JsonPropertyName("customBlockedDomains")]
        public List<string> CustomBlockedDomains { get; set; } = new();

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

        [JsonPropertyName("logAiAccessAttempts")]
        public bool LogAiAccessAttempts { get; set; } = true;

        [JsonPropertyName("requireAiConsentPrompt")]
        public bool RequireAiConsentPrompt { get; set; } = true;

        [JsonPropertyName("allowAiMetadataAccess")]
        public bool AllowAiMetadataAccess { get; set; } = false;

        [JsonPropertyName("allowedAiServices")]
        public List<AllowedAiService> AllowedAiServices { get; set; } = new();

        [JsonPropertyName("lastUpdatedUtc")]
        public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed class AllowedAiService
    {

        [JsonPropertyName("serviceId")]
        public string ServiceId { get; set; } = string.Empty;

        [JsonPropertyName("serviceName")]
        public string ServiceName { get; set; } = string.Empty;

        [JsonPropertyName("domains")]
        public List<string> Domains { get; set; } = new();

        [JsonPropertyName("accessLevel")]
        public AiAccessLevel AccessLevel { get; set; } = AiAccessLevel.None;

        [JsonPropertyName("requiresPerUseConsent")]
        public bool RequiresPerUseConsent { get; set; } = true;

        [JsonPropertyName("expiresUtc")]
        public DateTimeOffset? ExpiresUtc { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AiAccessLevel
    {

        None,

        MetadataOnly,

        UsernamesOnly,

        Full
    }
}

