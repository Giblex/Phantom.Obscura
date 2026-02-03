using System.Security.Cryptography.X509Certificates;
using System.Text;
using System;
using System.Linq;
using System.Text.Json;

public sealed class ObscuraPolicy
{
    public string PolicyId { get; init; } = "";
    public string Version  { get; init; } = "";

    public UsbPolicy Usb { get; init; } = new();
    public DesktopPolicy Desktop { get; init; } = new();
    public ManifestPolicy Manifest { get; init; } = new();
    public PolicySync Sync { get; init; } = new();

    public sealed class UsbPolicy
    {
        public bool Required { get; init; }
        
        // "Any" | "LabelOnly" | "Serial" | "CryptoKey"
        public string IdentityMode { get; init; } = "Any";
        
        public bool RequireRemovable { get; init; } = true;
        public bool AllowHotSwap { get; init; } = false;  // Can USB be changed during session?
        
        public string? VolumeLabel { get; init; }
        public string[] AllowedSerials { get; init; } = Array.Empty<string>();
        public string[] RequiredKeyIds { get; init; } = Array.Empty<string>();
        public string[] TrustedDeviceIds { get; init; } = Array.Empty<string>();  // Device fingerprints
        
        // "USB2", "USB3", "USB3Plus", etc
        public string? MinStandard { get; init; }
        public int MinCapacityGB { get; init; } = 0;  // Minimum capacity (0 = no limit)
        
        public string OnRemoval { get; init; } = "LockAndZero";  // Ignore | Lock | LockAndZero
        public bool RequireRemountForSensitiveOps { get; init; } = true;
        public bool RequireAttestationFile { get; init; } = false;  // Require attestation.json on USB
        public int PollIntervalSeconds { get; init; } = 5;  // How often to check USB presence
    }

    public sealed class DesktopPolicy
    {
        // Device & Session Management
        public bool AllowDesktopSync { get; init; }
        public bool AllowMultipleDevices { get; init; }
        public string DeviceBindingMode { get; init; } = "None";  // None | Weak | Strong | Hardware
        public string[] TrustedDeviceFingerprints { get; init; } = Array.Empty<string>();
        public int MaxConcurrentSessions { get; init; } = 1;
        public bool RequireDeviceRegistration { get; init; } = false;

        // Security Features
        public bool AllowDebuggers { get; init; }
        public bool AllowScreenCapture { get; init; } = true;
        public bool AllowClipboardAccess { get; init; } = true;
        public bool RequireSecureBoot { get; init; } = false;
        public bool RequireTpm { get; init; } = false;
        public bool BlockVirtualMachines { get; init; } = false;

        // AI Protection (Shadow AI Defense)
        public bool AllowAiAccess { get; init; } = false;  // Master switch - default OFF
        public bool BlockClipboardToAi { get; init; } = true;  // Block clipboard to AI contexts
        public bool BlockAutofillToAi { get; init; } = true;  // Block autofill to AI chat interfaces
        public bool EnableAiSafeView { get; init; } = false;  // Masked secrets mode
        public string[] BlockedAiDomains { get; init; } = Array.Empty<string>();  // Custom blocked domains
        public bool LogAiAccessAttempts { get; init; } = true;  // Audit AI access attempts
        public bool RequireAiConsentPrompt { get; init; } = true;  // Require explicit consent

        // Session Controls
        public int MaxIdleMinutes { get; init; } = 15;  // 0 = no limit
        public int MaxSessionDurationMinutes { get; init; } = 480;  // 0 = no limit (8 hours default)
        public bool RequireReauthOnWake { get; init; } = true;
        public string SessionTerminationMode { get; init; } = "Lock";  // Lock | LockAndZero | Close

        // Network & Sync
        public bool AllowCloudSync { get; init; } = false;
        public string[] AllowedSyncDomains { get; init; } = Array.Empty<string>();
        public bool RequireSslPinning { get; init; } = true;
    }

    public sealed class ManifestPolicy
    {
        // Version Control
        public string? MinVersion { get; init; }
        public string? MaxVersion { get; init; }
        public bool EnforceExactVersion { get; init; } = false;
        public string? RequiredVersion { get; init; }  // For exact version enforcement
        
        // Signature & Attestation
        public bool RequireSignature { get; init; }
        public string SignatureAlgorithm { get; init; } = "ECDSA-P256";  // ECDSA-P256 | RSA-2048 | RSA-4096
        public bool RequireTimestamp { get; init; } = false;
        public bool RequireCounterSignature { get; init; } = false;
        public string[] TrustedSignerKeyIds { get; init; } = Array.Empty<string>();
        
        // Trust Chain
        public bool RequireChainOfTrust { get; init; } = true;  // Policy → USB Key → Manifest
        public bool AllowSelfSigned { get; init; } = false;
        public int MaxSignatureAgeHours { get; init; } = 0;  // 0 = no limit
        
        // Integrity & Updates
        public bool RequireIntegrityCheck { get; init; } = true;
        public bool AllowRollback { get; init; } = false;  // Can downgrade manifest version?
        public bool RequireUpdateChannel { get; init; } = false;
        public string[] AllowedUpdateChannels { get; init; } = Array.Empty<string>();  // stable | beta | dev
        
        // Metadata Requirements
        public bool RequireDeviceBinding { get; init; } = false;  // Manifest must specify device IDs
        public bool RequireUsbBinding { get; init; } = false;  // Manifest must specify USB IDs
        public string[] RequiredFields { get; init; } = Array.Empty<string>();  // Custom required fields
    }

    public sealed class PolicySync
    {
        public bool EnforceCrossPolicyValidation { get; init; } = true;
        public bool RequireAllPoliciesActive { get; init; } = true;
        public string EnforcementOrder { get; init; } = "Sequential"; // Sequential | Parallel

        public bool ValidateDeviceBindingConsistency { get; init; } = true;
        public bool ValidateUsbManifestBinding { get; init; } = true;
        public bool ValidateSignatureConsistency { get; init; } = true;

        public string OnPolicyConflict { get; init; } = "MostRestrictive"; // MostRestrictive | Fail | Warn
        public string OnPolicySyncFailure { get; init; } = "Fail"; // Fail | Warn
        public bool AllowPolicyOverride { get; init; } = false;

        public bool LogAllPolicyChecks { get; init; } = true;
        public bool RequireAuditTrail { get; init; } = true;
        public int AuditRetentionDays { get; init; } = 90;
    }

    public static ObscuraPolicy FromVerifiedJson(string verifiedPolicyJson)
    {
        // This assumes you've already run PolicyVerifier.VerifyPolicy(...)
        // i.e. signature is valid, so we just parse it.
        var doc = JsonDocument.Parse(verifiedPolicyJson);
        var root = doc.RootElement;

        return new ObscuraPolicy
        {
            PolicyId = root.GetProperty("policyId").GetString() ?? "",
            Version  = root.GetProperty("version").GetString() ?? "",
            Usb = new UsbPolicy
            {
                Required = root.GetProperty("usb").GetProperty("required").GetBoolean(),
                IdentityMode = root.GetProperty("usb").GetProperty("identityMode").GetString() ?? "Any",
                RequireRemovable = root.GetProperty("usb").GetProperty("requireRemovable").GetBoolean(),
                AllowHotSwap = root.GetProperty("usb").TryGetProperty("allowHotSwap", out var ahs) && ahs.GetBoolean(),
                
                VolumeLabel = root.GetProperty("usb").GetProperty("volumeLabel").ValueKind == JsonValueKind.Null
                    ? null
                    : root.GetProperty("usb").GetProperty("volumeLabel").GetString(),
                
                AllowedSerials = root.GetProperty("usb").GetProperty("allowedSerials")
                    .EnumerateArray()
                    .Select(x => x.GetString()!)
                    .ToArray(),
                
                RequiredKeyIds = root.GetProperty("usb").GetProperty("requiredKeyIds")
                    .EnumerateArray()
                    .Select(x => x.GetString()!)
                    .ToArray(),
                
                TrustedDeviceIds = root.GetProperty("usb").TryGetProperty("trustedDeviceIds", out var tdi)
                    ? tdi.EnumerateArray().Select(x => x.GetString()!).ToArray()
                    : Array.Empty<string>(),
                
                MinStandard = root.GetProperty("usb").GetProperty("minStandard").ValueKind == JsonValueKind.Null
                    ? null
                    : root.GetProperty("usb").GetProperty("minStandard").GetString(),
                
                MinCapacityGB = root.GetProperty("usb").TryGetProperty("minCapacityGB", out var mcg) ? mcg.GetInt32() : 0,
                
                OnRemoval = root.GetProperty("usb").GetProperty("onRemoval").GetString() ?? "LockAndZero",
                RequireRemountForSensitiveOps = root.GetProperty("usb")
                    .GetProperty("requireRemountForSensitiveOps")
                    .GetBoolean(),
                RequireAttestationFile = root.GetProperty("usb").TryGetProperty("requireAttestationFile", out var raf) && raf.GetBoolean(),
                PollIntervalSeconds = root.GetProperty("usb").TryGetProperty("pollIntervalSeconds", out var pis) ? pis.GetInt32() : 5
            },
            Desktop = new DesktopPolicy
            {
                AllowDesktopSync = root.GetProperty("desktop").GetProperty("allowDesktopSync").GetBoolean(),
                AllowMultipleDevices = root.GetProperty("desktop").GetProperty("allowMultipleDevices").GetBoolean(),
                DeviceBindingMode = root.GetProperty("desktop").TryGetProperty("deviceBindingMode", out var dbm) ? dbm.GetString() ?? "None" : "None",
                TrustedDeviceFingerprints = root.GetProperty("desktop").TryGetProperty("trustedDeviceFingerprints", out var tdf)
                    ? tdf.EnumerateArray().Select(x => x.GetString()!).ToArray()
                    : Array.Empty<string>(),
                MaxConcurrentSessions = root.GetProperty("desktop").TryGetProperty("maxConcurrentSessions", out var mcs) ? mcs.GetInt32() : 1,
                RequireDeviceRegistration = root.GetProperty("desktop").TryGetProperty("requireDeviceRegistration", out var rdr) && rdr.GetBoolean(),
                
                AllowDebuggers = root.GetProperty("desktop").GetProperty("allowDebuggers").GetBoolean(),
                AllowScreenCapture = root.GetProperty("desktop").TryGetProperty("allowScreenCapture", out var asc) ? asc.GetBoolean() : true,
                AllowClipboardAccess = root.GetProperty("desktop").TryGetProperty("allowClipboardAccess", out var aca) ? aca.GetBoolean() : true,
                RequireSecureBoot = root.GetProperty("desktop").TryGetProperty("requireSecureBoot", out var rsb) && rsb.GetBoolean(),
                RequireTpm = root.GetProperty("desktop").TryGetProperty("requireTpm", out var rt) && rt.GetBoolean(),
                BlockVirtualMachines = root.GetProperty("desktop").TryGetProperty("blockVirtualMachines", out var bvm) && bvm.GetBoolean(),

                // AI Protection
                AllowAiAccess = root.GetProperty("desktop").TryGetProperty("allowAiAccess", out var aaa) && aaa.GetBoolean(),
                BlockClipboardToAi = root.GetProperty("desktop").TryGetProperty("blockClipboardToAi", out var bcta) ? bcta.GetBoolean() : true,
                BlockAutofillToAi = root.GetProperty("desktop").TryGetProperty("blockAutofillToAi", out var bata) ? bata.GetBoolean() : true,
                EnableAiSafeView = root.GetProperty("desktop").TryGetProperty("enableAiSafeView", out var easv) && easv.GetBoolean(),
                BlockedAiDomains = root.GetProperty("desktop").TryGetProperty("blockedAiDomains", out var bad)
                    ? bad.EnumerateArray().Select(x => x.GetString()!).ToArray()
                    : Array.Empty<string>(),
                LogAiAccessAttempts = root.GetProperty("desktop").TryGetProperty("logAiAccessAttempts", out var laaa) ? laaa.GetBoolean() : true,
                RequireAiConsentPrompt = root.GetProperty("desktop").TryGetProperty("requireAiConsentPrompt", out var racp) ? racp.GetBoolean() : true,

                MaxIdleMinutes = root.GetProperty("desktop").TryGetProperty("maxIdleMinutes", out var mim) ? mim.GetInt32() : 15,
                MaxSessionDurationMinutes = root.GetProperty("desktop").TryGetProperty("maxSessionDurationMinutes", out var msdm) ? msdm.GetInt32() : 480,
                RequireReauthOnWake = root.GetProperty("desktop").TryGetProperty("requireReauthOnWake", out var rrow) ? rrow.GetBoolean() : true,
                SessionTerminationMode = root.GetProperty("desktop").TryGetProperty("sessionTerminationMode", out var stm) ? stm.GetString() ?? "Lock" : "Lock",
                
                AllowCloudSync = root.GetProperty("desktop").TryGetProperty("allowCloudSync", out var acs2) && acs2.GetBoolean(),
                AllowedSyncDomains = root.GetProperty("desktop").TryGetProperty("allowedSyncDomains", out var asd)
                    ? asd.EnumerateArray().Select(x => x.GetString()!).ToArray()
                    : Array.Empty<string>(),
                RequireSslPinning = root.GetProperty("desktop").TryGetProperty("requireSslPinning", out var rsp) ? rsp.GetBoolean() : true
            },
            Manifest = new ManifestPolicy
            {
                MinVersion = root.GetProperty("manifest").GetProperty("minVersion").GetString(),
                MaxVersion = root.GetProperty("manifest").GetProperty("maxVersion").GetString(),
                EnforceExactVersion = root.GetProperty("manifest").TryGetProperty("enforceExactVersion", out var eev) && eev.GetBoolean(),
                RequiredVersion = root.GetProperty("manifest").TryGetProperty("requiredVersion", out var rv) && rv.ValueKind != JsonValueKind.Null
                    ? rv.GetString()
                    : null,
                
                RequireSignature = root.GetProperty("manifest").GetProperty("requireSignature").GetBoolean(),
                SignatureAlgorithm = root.GetProperty("manifest").TryGetProperty("signatureAlgorithm", out var sa) ? sa.GetString() ?? "ECDSA-P256" : "ECDSA-P256",
                RequireTimestamp = root.GetProperty("manifest").TryGetProperty("requireTimestamp", out var rts) && rts.GetBoolean(),
                RequireCounterSignature = root.GetProperty("manifest").TryGetProperty("requireCounterSignature", out var rcs) && rcs.GetBoolean(),
                TrustedSignerKeyIds = root.GetProperty("manifest").TryGetProperty("trustedSignerKeyIds", out var tski)
                    ? tski.EnumerateArray().Select(x => x.GetString()!).ToArray()
                    : Array.Empty<string>(),
                
                RequireChainOfTrust = root.GetProperty("manifest").TryGetProperty("requireChainOfTrust", out var rcot) ? rcot.GetBoolean() : true,
                AllowSelfSigned = root.GetProperty("manifest").TryGetProperty("allowSelfSigned", out var ass) && ass.GetBoolean(),
                MaxSignatureAgeHours = root.GetProperty("manifest").TryGetProperty("maxSignatureAgeHours", out var msah) ? msah.GetInt32() : 0,
                
                RequireIntegrityCheck = root.GetProperty("manifest").TryGetProperty("requireIntegrityCheck", out var ric) ? ric.GetBoolean() : true,
                AllowRollback = root.GetProperty("manifest").TryGetProperty("allowRollback", out var arb) && arb.GetBoolean(),
                RequireUpdateChannel = root.GetProperty("manifest").TryGetProperty("requireUpdateChannel", out var ruc) && ruc.GetBoolean(),
                AllowedUpdateChannels = root.GetProperty("manifest").TryGetProperty("allowedUpdateChannels", out var auc)
                    ? auc.EnumerateArray().Select(x => x.GetString()!).ToArray()
                    : Array.Empty<string>(),
                
                RequireDeviceBinding = root.GetProperty("manifest").TryGetProperty("requireDeviceBinding", out var rdb) && rdb.GetBoolean(),
                RequireUsbBinding = root.GetProperty("manifest").TryGetProperty("requireUsbBinding", out var rub) && rub.GetBoolean(),
                RequiredFields = root.GetProperty("manifest").TryGetProperty("requiredFields", out var rf)
                    ? rf.EnumerateArray().Select(x => x.GetString()!).ToArray()
                    : Array.Empty<string>()
            },
            Sync = root.TryGetProperty("sync", out var syncProp) ? new PolicySync
            {
                EnforceCrossPolicyValidation = syncProp.TryGetProperty("enforceCrossPolicyValidation", out var ecpv) ? ecpv.GetBoolean() : true,
                RequireAllPoliciesActive = syncProp.TryGetProperty("requireAllPoliciesActive", out var rapa) ? rapa.GetBoolean() : true,
                EnforcementOrder = syncProp.TryGetProperty("enforcementOrder", out var eo) ? eo.GetString() ?? "Sequential" : "Sequential",
                
                ValidateDeviceBindingConsistency = syncProp.TryGetProperty("validateDeviceBindingConsistency", out var vdbc) ? vdbc.GetBoolean() : true,
                ValidateUsbManifestBinding = syncProp.TryGetProperty("validateUsbManifestBinding", out var vumb) ? vumb.GetBoolean() : true,
                ValidateSignatureConsistency = syncProp.TryGetProperty("validateSignatureConsistency", out var vsc) ? vsc.GetBoolean() : true,
                
                OnPolicyConflict = syncProp.TryGetProperty("onPolicyConflict", out var opc) ? opc.GetString() ?? "MostRestrictive" : "MostRestrictive",
                OnPolicySyncFailure = syncProp.TryGetProperty("onPolicySyncFailure", out var opsf) ? opsf.GetString() ?? "Fail" : "Fail",
                AllowPolicyOverride = syncProp.TryGetProperty("allowPolicyOverride", out var apo) && apo.GetBoolean(),
                
                LogAllPolicyChecks = syncProp.TryGetProperty("logAllPolicyChecks", out var lapc) ? lapc.GetBoolean() : true,
                RequireAuditTrail = syncProp.TryGetProperty("requireAuditTrail", out var rat) ? rat.GetBoolean() : true,
                AuditRetentionDays = syncProp.TryGetProperty("auditRetentionDays", out var ard) ? ard.GetInt32() : 90
            } : new PolicySync()
        };
    }
}
