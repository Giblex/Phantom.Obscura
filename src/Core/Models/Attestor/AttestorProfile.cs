using System;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.Attestor
{
    /// <summary>
    /// Top-level container representing instance-wide assumptions and defaults.
    /// Defines the security posture, crypto policy, and operational defaults
    /// for the entire Attestor instance.
    /// </summary>
    public sealed class AttestorProfile
    {
        /// <summary>
        /// Unique identifier for this profile (UUIDv7 recommended for time-sortability).
        /// </summary>
        [JsonPropertyName("profile_id")]
        public string ProfileId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Schema version for forward compatibility.
        /// </summary>
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        /// <summary>
        /// Timestamp when this profile was created.
        /// </summary>
        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Stable device fingerprint or UUID for device binding.
        /// </summary>
        [JsonPropertyName("device_id")]
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>
        /// Explicit threat assumptions for this environment.
        /// </summary>
        [JsonPropertyName("threat_assumptions")]
        public ThreatAssumptions ThreatAssumptions { get; set; } = new();

        /// <summary>
        /// Default operational parameters.
        /// </summary>
        [JsonPropertyName("defaults")]
        public AttestorDefaults Defaults { get; set; } = new();

        /// <summary>
        /// Cryptographic policy for this instance.
        /// </summary>
        [JsonPropertyName("crypto_policy")]
        public CryptoPolicy CryptoPolicy { get; set; } = new();

        /// <summary>
        /// Optional human-readable label for this profile.
        /// </summary>
        [JsonPropertyName("label")]
        public string? Label { get; set; }

        /// <summary>
        /// Timestamp when profile was last modified.
        /// </summary>
        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Explicit threat assumptions for hostile environment operation.
    /// These flags drive defensive behaviors throughout the system.
    /// </summary>
    public sealed class ThreatAssumptions
    {
        /// <summary>
        /// Operating system may be compromised or monitored.
        /// </summary>
        [JsonPropertyName("hostile_os")]
        public bool HostileOs { get; set; } = true;

        /// <summary>
        /// Kernel-level threats assumed (rootkits, hypervisor attacks).
        /// </summary>
        [JsonPropertyName("untrusted_kernel")]
        public bool UntrustedKernel { get; set; } = true;

        /// <summary>
        /// Clipboard contents may be monitored or exfiltrated.
        /// </summary>
        [JsonPropertyName("clipboard_compromised")]
        public bool ClipboardCompromised { get; set; } = true;

        /// <summary>
        /// Keystrokes may be logged by malware or hardware.
        /// </summary>
        [JsonPropertyName("keylogger_possible")]
        public bool KeyloggerPossible { get; set; } = true;

        /// <summary>
        /// Memory may be scraped by malware or cold boot attacks.
        /// </summary>
        [JsonPropertyName("memory_scrape_possible")]
        public bool MemoryScrapePossible { get; set; } = true;

        /// <summary>
        /// Supply chain may be compromised (malicious packages, updates).
        /// </summary>
        [JsonPropertyName("supply_chain_risk")]
        public bool SupplyChainRisk { get; set; } = true;

        /// <summary>
        /// AI tools may inadvertently leak secrets.
        /// </summary>
        [JsonPropertyName("ai_leakage_risk")]
        public bool AiLeakageRisk { get; set; } = true;

        /// <summary>
        /// Screen may be captured or shoulder-surfed.
        /// </summary>
        [JsonPropertyName("screen_capture_risk")]
        public bool ScreenCaptureRisk { get; set; } = true;
    }

    /// <summary>
    /// Default operational parameters for the Attestor instance.
    /// </summary>
    public sealed class AttestorDefaults
    {
        /// <summary>
        /// Require USB presence for sensitive operations.
        /// </summary>
        [JsonPropertyName("require_usb_presence")]
        public bool RequireUsbPresence { get; set; } = true;

        /// <summary>
        /// Re-authentication required after this many seconds.
        /// </summary>
        [JsonPropertyName("require_reauth_seconds")]
        public int RequireReauthSeconds { get; set; } = 900;

        /// <summary>
        /// Default TTL for approval artifacts in seconds.
        /// </summary>
        [JsonPropertyName("approval_ttl_seconds")]
        public int ApprovalTtlSeconds { get; set; } = 20;

        /// <summary>
        /// TOTP time step in seconds (RFC 6238 default: 30).
        /// </summary>
        [JsonPropertyName("totp_step_seconds")]
        public int TotpStepSeconds { get; set; } = 30;

        /// <summary>
        /// Clock skew tolerance in TOTP steps.
        /// </summary>
        [JsonPropertyName("clock_skew_tolerance_steps")]
        public int ClockSkewToleranceSteps { get; set; } = 1;

        /// <summary>
        /// Default rate limit: max approvals per minute.
        /// </summary>
        [JsonPropertyName("max_approvals_per_minute")]
        public int MaxApprovalsPerMinute { get; set; } = 10;

        /// <summary>
        /// Auto-lock after idle seconds (0 = disabled).
        /// </summary>
        [JsonPropertyName("auto_lock_idle_seconds")]
        public int AutoLockIdleSeconds { get; set; } = 300;
    }

    /// <summary>
    /// Cryptographic policy defining algorithms and parameters.
    /// </summary>
    public sealed class CryptoPolicy
    {
        /// <summary>
        /// Key derivation function (argon2id recommended).
        /// </summary>
        [JsonPropertyName("kdf")]
        public string Kdf { get; set; } = "argon2id";

        /// <summary>
        /// Symmetric cipher for encryption.
        /// </summary>
        [JsonPropertyName("cipher")]
        public string Cipher { get; set; } = "xchacha20-poly1305";

        /// <summary>
        /// Signing algorithm for attestations.
        /// </summary>
        [JsonPropertyName("signing")]
        public string Signing { get; set; } = "ed25519";

        /// <summary>
        /// Hash algorithm for integrity checks.
        /// </summary>
        [JsonPropertyName("hash")]
        public string Hash { get; set; } = "sha256";

        /// <summary>
        /// Post-quantum algorithm (optional).
        /// </summary>
        [JsonPropertyName("post_quantum")]
        public string? PostQuantum { get; set; } = "ml-kem-768";
    }

    /// <summary>
    /// A pointer to where secrets actually live (Obscura container or local secure store).
    /// Secrets are never copied; only referenced via VaultRef.
    /// </summary>
    public sealed class VaultRef
    {
        /// <summary>
        /// Unique identifier for this vault reference.
        /// </summary>
        [JsonPropertyName("vault_ref_id")]
        public string VaultRefId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Type of vault being referenced.
        /// </summary>
        [JsonPropertyName("type")]
        public VaultRefType Type { get; set; } = VaultRefType.PhantomObscuraContainer;

        /// <summary>
        /// Location hint for finding the vault (e.g., "usb://PHANTOM/VAULTS/main.phc").
        /// </summary>
        [JsonPropertyName("location_hint")]
        public string LocationHint { get; set; } = string.Empty;

        /// <summary>
        /// SHA-256 fingerprint of the vault for verification.
        /// </summary>
        [JsonPropertyName("vault_fingerprint")]
        public string VaultFingerprint { get; set; } = string.Empty;

        /// <summary>
        /// Access requirements for this vault.
        /// </summary>
        [JsonPropertyName("access_requirements")]
        public VaultAccessRequirements AccessRequirements { get; set; } = new();

        /// <summary>
        /// Timestamp when this reference was created.
        /// </summary>
        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Whether this vault is currently accessible.
        /// </summary>
        [JsonPropertyName("is_available")]
        public bool IsAvailable { get; set; } = false;
    }

    /// <summary>
    /// Types of vault that can be referenced.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum VaultRefType
    {
        /// <summary>
        /// Phantom Obscura encrypted container.
        /// </summary>
        PhantomObscuraContainer,

        /// <summary>
        /// Local OS secure storage (Keychain, Credential Manager).
        /// </summary>
        LocalSecureStore,

        /// <summary>
        /// Hardware security module.
        /// </summary>
        HardwareSecurityModule,

        /// <summary>
        /// YubiKey or similar hardware token.
        /// </summary>
        HardwareToken
    }

    /// <summary>
    /// Requirements for accessing a vault.
    /// </summary>
    public sealed class VaultAccessRequirements
    {
        /// <summary>
        /// USB token must be present.
        /// </summary>
        [JsonPropertyName("usb_presence")]
        public bool UsbPresence { get; set; } = true;

        /// <summary>
        /// Hardware token required.
        /// </summary>
        [JsonPropertyName("hardware_token")]
        public bool HardwareToken { get; set; } = false;

        /// <summary>
        /// PIN required.
        /// </summary>
        [JsonPropertyName("pin_required")]
        public bool PinRequired { get; set; } = false;

        /// <summary>
        /// Biometric verification (if trusted).
        /// </summary>
        [JsonPropertyName("biometric")]
        public bool Biometric { get; set; } = false;
    }
}
