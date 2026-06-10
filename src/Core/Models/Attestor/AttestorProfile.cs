using System;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.Attestor
{

    public sealed class AttestorProfile
    {

        [JsonPropertyName("profile_id")]
        public string ProfileId { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("device_id")]
        public string DeviceId { get; set; } = string.Empty;

        [JsonPropertyName("threat_assumptions")]
        public ThreatAssumptions ThreatAssumptions { get; set; } = new();

        [JsonPropertyName("defaults")]
        public AttestorDefaults Defaults { get; set; } = new();

        [JsonPropertyName("crypto_policy")]
        public CryptoPolicy CryptoPolicy { get; set; } = new();

        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed class ThreatAssumptions
    {

        [JsonPropertyName("hostile_os")]
        public bool HostileOs { get; set; } = true;

        [JsonPropertyName("untrusted_kernel")]
        public bool UntrustedKernel { get; set; } = true;

        [JsonPropertyName("clipboard_compromised")]
        public bool ClipboardCompromised { get; set; } = true;

        [JsonPropertyName("keylogger_possible")]
        public bool KeyloggerPossible { get; set; } = true;

        [JsonPropertyName("memory_scrape_possible")]
        public bool MemoryScrapePossible { get; set; } = true;

        [JsonPropertyName("supply_chain_risk")]
        public bool SupplyChainRisk { get; set; } = true;

        [JsonPropertyName("ai_leakage_risk")]
        public bool AiLeakageRisk { get; set; } = true;

        [JsonPropertyName("screen_capture_risk")]
        public bool ScreenCaptureRisk { get; set; } = true;
    }

    public sealed class AttestorDefaults
    {

        [JsonPropertyName("require_usb_presence")]
        public bool RequireUsbPresence { get; set; } = true;

        [JsonPropertyName("require_reauth_seconds")]
        public int RequireReauthSeconds { get; set; } = 900;

        [JsonPropertyName("approval_ttl_seconds")]
        public int ApprovalTtlSeconds { get; set; } = 20;

        [JsonPropertyName("totp_step_seconds")]
        public int TotpStepSeconds { get; set; } = 30;

        [JsonPropertyName("clock_skew_tolerance_steps")]
        public int ClockSkewToleranceSteps { get; set; } = 1;

        [JsonPropertyName("max_approvals_per_minute")]
        public int MaxApprovalsPerMinute { get; set; } = 10;

        [JsonPropertyName("auto_lock_idle_seconds")]
        public int AutoLockIdleSeconds { get; set; } = 300;
    }

    public sealed class CryptoPolicy
    {

        [JsonPropertyName("kdf")]
        public string Kdf { get; set; } = "argon2id";

        [JsonPropertyName("cipher")]
        public string Cipher { get; set; } = "xchacha20-poly1305";

        [JsonPropertyName("signing")]
        public string Signing { get; set; } = "ed25519";

        [JsonPropertyName("hash")]
        public string Hash { get; set; } = "sha256";

        [JsonPropertyName("post_quantum")]
        public string? PostQuantum { get; set; } = "ml-kem-768";
    }

    public sealed class VaultRef
    {

        [JsonPropertyName("vault_ref_id")]
        public string VaultRefId { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("type")]
        public VaultRefType Type { get; set; } = VaultRefType.PhantomObscuraContainer;

        [JsonPropertyName("location_hint")]
        public string LocationHint { get; set; } = string.Empty;

        [JsonPropertyName("vault_fingerprint")]
        public string VaultFingerprint { get; set; } = string.Empty;

        [JsonPropertyName("access_requirements")]
        public VaultAccessRequirements AccessRequirements { get; set; } = new();

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("is_available")]
        public bool IsAvailable { get; set; } = false;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum VaultRefType
    {

        PhantomObscuraContainer,

        LocalSecureStore,

        HardwareSecurityModule,

        HardwareToken
    }

    public sealed class VaultAccessRequirements
    {

        [JsonPropertyName("usb_presence")]
        public bool UsbPresence { get; set; } = true;

        [JsonPropertyName("hardware_token")]
        public bool HardwareToken { get; set; } = false;

        [JsonPropertyName("pin_required")]
        public bool PinRequired { get; set; } = false;

        [JsonPropertyName("biometric")]
        public bool Biometric { get; set; } = false;
    }
}

