using System;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.Security
{

    public sealed class ThreatModelMetadata
    {

        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("environment")]
        public ThreatEnvironment Environment { get; set; } = ThreatEnvironment.HostileOS;

        [JsonPropertyName("assumedThreats")]
        public AssumedThreats AssumedThreats { get; set; } = new();

        [JsonPropertyName("trustBoundaries")]
        public TrustBoundaries TrustBoundaries { get; set; } = new();

        [JsonPropertyName("lastReviewedUtc")]
        public DateTimeOffset LastReviewedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ThreatEnvironment
    {

        Trusted,

        Standard,

        HostileOS,

        UntrustedKernel,

        PhysicalCompromise,

        AdversarialEnvironment
    }

    public sealed class AssumedThreats
    {

        [JsonPropertyName("clipboardCompromise")]
        public bool ClipboardCompromise { get; set; } = true;

        [JsonPropertyName("keyloggerPresent")]
        public bool KeyloggerPresent { get; set; } = true;

        [JsonPropertyName("memoryScrapeRisk")]
        public bool MemoryScrapeRisk { get; set; } = true;

        [JsonPropertyName("screenCaptureRisk")]
        public bool ScreenCaptureRisk { get; set; } = true;

        [JsonPropertyName("networkInterception")]
        public bool NetworkInterception { get; set; } = true;

        [JsonPropertyName("supplyChainRisk")]
        public bool SupplyChainRisk { get; set; } = true;

        [JsonPropertyName("aiLeakageRisk")]
        public bool AiLeakageRisk { get; set; } = true;

        [JsonPropertyName("browserExtensionRisk")]
        public bool BrowserExtensionRisk { get; set; } = true;

        [JsonPropertyName("physicalSeizureRisk")]
        public bool PhysicalSeizureRisk { get; set; } = false;
    }

    public sealed class TrustBoundaries
    {

        [JsonPropertyName("trustOnlyVaultApp")]
        public bool TrustOnlyVaultApp { get; set; } = true;

        [JsonPropertyName("trustOperatingSystem")]
        public bool TrustOperatingSystem { get; set; } = false;

        [JsonPropertyName("trustBrowser")]
        public bool TrustBrowser { get; set; } = false;

        [JsonPropertyName("trustCloudServices")]
        public bool TrustCloudServices { get; set; } = false;

        [JsonPropertyName("trustAiAssistants")]
        public bool TrustAiAssistants { get; set; } = false;

        [JsonPropertyName("trustUsbToken")]
        public bool TrustUsbToken { get; set; } = true;

        [JsonPropertyName("trustBiometrics")]
        public bool TrustBiometrics { get; set; } = false;
    }
}

