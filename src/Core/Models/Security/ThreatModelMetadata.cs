using System;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.Security
{
    /// <summary>
    /// Explicit threat model metadata for hostile-environment vault operation.
    /// Makes security assumptions first-class for UI warnings, AI auditing, and compliance narratives.
    /// Designed for zero-trust environments where no component is trusted by default.
    /// </summary>
    public sealed class ThreatModelMetadata
    {
        /// <summary>
        /// Version of the threat model schema.
        /// </summary>
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        /// <summary>
        /// Primary operating environment assumption.
        /// </summary>
        [JsonPropertyName("environment")]
        public ThreatEnvironment Environment { get; set; } = ThreatEnvironment.HostileOS;

        /// <summary>
        /// Assumed threats active in the operating environment.
        /// </summary>
        [JsonPropertyName("assumedThreats")]
        public AssumedThreats AssumedThreats { get; set; } = new();

        /// <summary>
        /// Trust boundaries explicitly defined for this vault.
        /// </summary>
        [JsonPropertyName("trustBoundaries")]
        public TrustBoundaries TrustBoundaries { get; set; } = new();

        /// <summary>
        /// Timestamp when threat model was last reviewed/updated.
        /// </summary>
        [JsonPropertyName("lastReviewedUtc")]
        public DateTimeOffset LastReviewedUtc { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Optional notes about threat model customizations.
        /// </summary>
        [JsonPropertyName("notes")]
        public string? Notes { get; set; }
    }

    /// <summary>
    /// Operating environment threat level classification.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ThreatEnvironment
    {
        /// <summary>
        /// Trusted corporate environment with managed devices.
        /// </summary>
        Trusted,

        /// <summary>
        /// Standard personal device, moderate threat level.
        /// </summary>
        Standard,

        /// <summary>
        /// Operating system may be compromised or monitored.
        /// </summary>
        HostileOS,

        /// <summary>
        /// Kernel-level threats assumed (rootkits, hypervisor attacks).
        /// </summary>
        UntrustedKernel,

        /// <summary>
        /// Physical access threats (evil maid, device seizure).
        /// </summary>
        PhysicalCompromise,

        /// <summary>
        /// Maximum paranoia: all layers potentially compromised.
        /// </summary>
        AdversarialEnvironment
    }

    /// <summary>
    /// Explicit threat assumptions for the vault environment.
    /// Setting these to true enables corresponding defensive behaviors.
    /// </summary>
    public sealed class AssumedThreats
    {
        /// <summary>
        /// Assume clipboard contents may be monitored or exfiltrated.
        /// Enables: clipboard clearing, no clipboard autofill, masked copy.
        /// </summary>
        [JsonPropertyName("clipboardCompromise")]
        public bool ClipboardCompromise { get; set; } = true;

        /// <summary>
        /// Assume keystrokes may be logged by malware or hardware.
        /// Enables: on-screen keyboard option, keystroke obfuscation.
        /// </summary>
        [JsonPropertyName("keyloggerPresent")]
        public bool KeyloggerPresent { get; set; } = true;

        /// <summary>
        /// Assume memory may be scraped by malware or cold boot attacks.
        /// Enables: aggressive memory zeroing, reduced key lifetime.
        /// </summary>
        [JsonPropertyName("memoryScrapeRisk")]
        public bool MemoryScrapeRisk { get; set; } = true;

        /// <summary>
        /// Assume screen may be captured or shoulder-surfed.
        /// Enables: masked display by default, no preview thumbnails.
        /// </summary>
        [JsonPropertyName("screenCaptureRisk")]
        public bool ScreenCaptureRisk { get; set; } = true;

        /// <summary>
        /// Assume network traffic may be intercepted or modified.
        /// Enables: certificate pinning, no network features.
        /// </summary>
        [JsonPropertyName("networkInterception")]
        public bool NetworkInterception { get; set; } = true;

        /// <summary>
        /// Assume supply chain may be compromised (malicious packages, updates).
        /// Enables: build verification, dependency hash checking.
        /// </summary>
        [JsonPropertyName("supplyChainRisk")]
        public bool SupplyChainRisk { get; set; } = true;

        /// <summary>
        /// Assume AI tools may inadvertently leak secrets via prompts.
        /// Enables: AI access blocking, clipboard isolation from AI contexts.
        /// </summary>
        [JsonPropertyName("aiLeakageRisk")]
        public bool AiLeakageRisk { get; set; } = true;

        /// <summary>
        /// Assume browser extensions may intercept autofill data.
        /// Enables: extension detection warnings, isolated autofill.
        /// </summary>
        [JsonPropertyName("browserExtensionRisk")]
        public bool BrowserExtensionRisk { get; set; } = true;

        /// <summary>
        /// Assume device may be physically seized or stolen.
        /// Enables: emergency wipe, decoy vaults, rapid lockout.
        /// </summary>
        [JsonPropertyName("physicalSeizureRisk")]
        public bool PhysicalSeizureRisk { get; set; } = false;
    }

    /// <summary>
    /// Explicit trust boundaries for the vault.
    /// Defines what is considered inside vs outside the security perimeter.
    /// </summary>
    public sealed class TrustBoundaries
    {
        /// <summary>
        /// The vault application itself is the only trusted component.
        /// </summary>
        [JsonPropertyName("trustOnlyVaultApp")]
        public bool TrustOnlyVaultApp { get; set; } = true;

        /// <summary>
        /// The operating system is NOT trusted.
        /// </summary>
        [JsonPropertyName("trustOperatingSystem")]
        public bool TrustOperatingSystem { get; set; } = false;

        /// <summary>
        /// The browser is NOT trusted (for autofill purposes).
        /// </summary>
        [JsonPropertyName("trustBrowser")]
        public bool TrustBrowser { get; set; } = false;

        /// <summary>
        /// Cloud services are NOT trusted.
        /// </summary>
        [JsonPropertyName("trustCloudServices")]
        public bool TrustCloudServices { get; set; } = false;

        /// <summary>
        /// AI assistants are NOT trusted with secrets.
        /// </summary>
        [JsonPropertyName("trustAiAssistants")]
        public bool TrustAiAssistants { get; set; } = false;

        /// <summary>
        /// The USB hardware token is trusted when present.
        /// </summary>
        [JsonPropertyName("trustUsbToken")]
        public bool TrustUsbToken { get; set; } = true;

        /// <summary>
        /// Biometric systems are NOT trusted (spoofable).
        /// </summary>
        [JsonPropertyName("trustBiometrics")]
        public bool TrustBiometrics { get; set; } = false;
    }
}
