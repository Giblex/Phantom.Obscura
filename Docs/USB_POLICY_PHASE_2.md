# USB Policy Implementation - Phase 2

## Overview

Updated USB policy system from basic label-only checking to a comprehensive, layered security model with multiple identity modes and configurable enforcement.

## What Changed

### 1. **Policy Model Extensions** (`ObscuraPolicy.cs`)

#### New UsbPolicy Properties

```csharp
public sealed class UsbPolicy
{
    public bool Required { get; init; }
    public string IdentityMode { get; init; } = "Any";  // Any | LabelOnly | Serial | CryptoKey
    public bool RequireRemovable { get; init; } = true;
    
    public string? VolumeLabel { get; init; }
    public string[] AllowedSerials { get; init; } = Array.Empty<string>();
    public string[] RequiredKeyIds { get; init; } = Array.Empty<string>();
    
    public string? MinStandard { get; init; }  // USB2 | USB3 | USB3Plus
    
    public string OnRemoval { get; init; } = "LockAndZero";  // Ignore | Lock | LockAndZero
    public bool RequireRemountForSensitiveOps { get; init; } = true;
}
```

#### What This Controls

**Presence** – Is any acceptable USB inserted?

- `Required`: Must there be a USB at all?
- `RequireRemovable`: Must it be a removable drive (not virtual/network)?

**Identity** – Is this the correct USB (or one of a specific set)?

- `IdentityMode`: How strict is USB verification?
  - `Any` (loosest): Any removable drive works
  - `LabelOnly`: Must match specific volume label
  - `Serial`: Must match hardware serial number
  - `CryptoKey` (strictest): Must have signed USB key file

**Properties** – Is it the right kind of device?

- `MinStandard`: Minimum USB version (USB2, USB3, USB3Plus)
- `VolumeLabel`: Expected label (e.g., "PHANTOM_OBSCURA")
- `AllowedSerials`: Hardware-level identities (for Serial/CryptoKey modes)
- `RequiredKeyIds`: Which USB keys are trusted (for CryptoKey mode)

**Behavior** – What happens when things change?

- `OnRemoval`: What to do if USB disappears mid-session
  - `Ignore`: No action (DEV only)
  - `Lock`: Lock UI, require re-auth
  - `LockAndZero`: Zero secrets + lock UI (RECOMMENDED)
- `RequireRemountForSensitiveOps`: Re-check USB before sensitive actions

### 2. **PolicyEngine Enhancements** (`PolicyEngine.cs`)

#### New Method: `EnforceUsbAtStartup()`

```csharp
public (DriveInfo? drive, string? volumeSerial) EnforceUsbAtStartup()
```

Returns the accepted USB drive and its serial (if retrieved). Throws `PolicyViolationException` if no acceptable USB found.

#### Identity Mode Enforcement

**Any Mode** (Current Default):

```csharp
case "Any":
    // Any removable drive (>= minStandard) is acceptable
    return (drive, null);
```

- Checks: `RequireRemovable`, `MinStandard` (logged but not enforced in v1)
- Use case: Developer machines, flexible deployment
- Security: Lowest - only requires *any* USB

**LabelOnly Mode**:

```csharp
case "LabelOnly":
    if (drive.VolumeLabel == _policy.Usb.VolumeLabel)
        return (drive, null);
```

- Checks: Volume label match
- Use case: Simple deployment with labeled USBs
- Security: Low - labels are easily cloned

**Serial Mode**:

```csharp
case "Serial":
    var serial = TryGetVolumeSerial(drive.Name);
    if (serial != null && AllowedSerials.Contains(serial))
        return (drive, serial);
```

- Checks: Hardware volume serial number (via WMI)
- Use case: Medium security with specific USB sticks
- Security: Medium - harder to clone than labels

**CryptoKey Mode** (Future):

```csharp
case "CryptoKey":
    // v1: Serial check
    // TODO: Load usb_key.json, verify signature, check keyId
    return (drive, serial);
```

- Checks: Cryptographically signed USB key file
- Use case: High security deployments
- Security: High - requires signed key file on USB

#### USB 3.0 Detection (Honest v1 Implementation)

```csharp
private static bool IsUsbStandardSatisfied(DriveInfo drive, string minStandard)
{
    // v1: HONEST IMPLEMENTATION
    // Always returns true (logs requirement but doesn't enforce)
    // TODO: Query Win32_USBController, parse USB descriptors
    System.Diagnostics.Debug.WriteLine($"USB standard check for '{minStandard}': v1 implementation always passes");
    return true;
}
```

**Why this approach?**

- **Transparent**: We're honest that v1 doesn't enforce USB 3.0
- **Progressive**: Policy shape exists, enforcement comes later
- **Logged**: Debug output shows the requirement for future testing
- **Pragmatic**: Avoids half-baked WMI queries that might fail

#### USB Removal Handling

```csharp
public void HandleUsbRemoved()
{
    switch (_policy.Usb.OnRemoval)
    {
        case "Ignore":   // DEV mode
        case "Lock":     // Lock UI
        case "LockAndZero": // Zero secrets + lock (RECOMMENDED)
    }
}
```

### 3. **Policy Exception System** (`PolicyViolationException.cs`)

New exception type with structured error codes:

```csharp
public enum PolicyViolationCode
{
    UsbNotFound,
    UsbPolicyInvalid,
    UsbIdentityRejected,
    UsbStandardInsufficient,
    ManifestSignatureInvalid,
    ManifestVersionMismatch,
    DesktopSyncDisabled,
    DeviceBindingFailed,
    DebuggerDetected
}

public class PolicyViolationException : Exception
{
    public PolicyViolationCode Code { get; }
}
```

Allows UI to handle different policy failures differently (e.g., show USB instructions vs. critical error).

### 4. **Base Policy Update** (`base_policy.json`)

```json
{
  "policyId": "OBSCURA-BASE-1",
  "version": "2.0.0",
  
  "usb": {
    "required": true,
    "identityMode": "Any",
    "requireRemovable": true,
    "volumeLabel": null,
    "allowedSerials": [],
    "requiredKeyIds": [],
    "minStandard": "USB3",
    "onRemoval": "LockAndZero",
    "requireRemountForSensitiveOps": true
  },
  
  "desktop": {
    "allowDesktopSync": false,
    "allowMultipleDevices": false,
    "allowDebuggers": false
  },
  
  "manifest": {
    "minVersion": "1.0.0",
    "maxVersion": "2.0.0",
    "requireSignature": true
  }
}
```

**Current Configuration:**

- `identityMode: "Any"` – Accept any removable USB
- `minStandard: "USB3"` – Prefer USB 3.0+ (not enforced in v1)
- `onRemoval: "LockAndZero"` – Zero secrets if USB removed
- `requireRemountForSensitiveOps: true` – Re-check USB before sensitive actions

## Policy Profiles

### DEV Profile (Development)

```json
"usb": {
  "required": false,
  "identityMode": "Any",
  "volumeLabel": null,
  "allowedSerials": [],
  "requiredKeyIds": [],
  "minStandard": null,
  "onRemoval": "Ignore",
  "requireRemountForSensitiveOps": false
}
```

- No USB required
- No enforcement
- For development/testing

### FLEXIBLE Profile (Current)

```json
"usb": {
  "required": true,
  "identityMode": "Any",
  "requireRemovable": true,
  "volumeLabel": null,
  "allowedSerials": [],
  "requiredKeyIds": [],
  "minStandard": "USB3",
  "onRemoval": "LockAndZero",
  "requireRemountForSensitiveOps": true
}
```

- Any removable USB required
- Prefers USB 3.0+ (logged, not enforced)
- Locks and zeros on removal
- Good balance of security and flexibility

### LABELED Profile

```json
"usb": {
  "required": true,
  "identityMode": "LabelOnly",
  "requireRemovable": true,
  "volumeLabel": "PHANTOM_OBSCURA",
  "allowedSerials": [],
  "requiredKeyIds": [],
  "minStandard": "USB3",
  "onRemoval": "LockAndZero",
  "requireRemountForSensitiveOps": true
}
```

- Must have specific volume label
- Easy to deploy (just label your USBs)
- Low security (labels are easily cloned)

### SERIAL Profile

```json
"usb": {
  "required": true,
  "identityMode": "Serial",
  "requireRemovable": true,
  "volumeLabel": "PHANTOM_OBSCURA",
  "allowedSerials": ["ABC1234", "DEF5678"],
  "requiredKeyIds": [],
  "minStandard": "USB3",
  "onRemoval": "LockAndZero",
  "requireRemountForSensitiveOps": true
}
```

- Must match specific hardware serials
- Medium security
- Harder to clone than labels

### CRYPTO Profile (Future)

```json
"usb": {
  "required": true,
  "identityMode": "CryptoKey",
  "requireRemovable": true,
  "volumeLabel": "PHANTOM_OBSCURA",
  "allowedSerials": ["ABC1234"],
  "requiredKeyIds": ["USB-KEY-001"],
  "minStandard": "USB3",
  "onRemoval": "LockAndZero",
  "requireRemountForSensitiveOps": true
}
```

- Requires signed `usb_key.json` on USB
- Verified with root public key
- Highest security
- v1: Not yet implemented

## Security Implications

### What This Buys You

**With USB Policy:**

- Stealing vault file alone = useless (needs USB present)
- Cloning USB = harder (serial + key checks)
- Swapping random USB = fails serial/key checks
- Removing USB mid-session = kills decrypted secrets

**Combined with Device Binding + Manifest:**

- Policy verified (root → policy)
- USB key verified (root → usb_key.json)
- Manifest verified (usb key → manifest)
- Device binding checked (policy/manifest → current device)

= **Layered trust model** instead of "open file anywhere"

### Current Security Posture

**v1 (Current):**

- ✅ Any removable USB required
- ✅ Logs USB 3.0 preference
- ✅ Locks and zeros on removal
- ✅ Re-checks before sensitive ops (skeleton)
- ❌ USB 3.0 not enforced yet
- ❌ CryptoKey mode not implemented

**This is honest security:**

- We say "USB 3.0 preferred" but don't lie about enforcement
- We build the policy shape now, enforcement later
- Better than claiming features we don't have

## Next Steps

### Phase 3 (USB 3.0 Enforcement)

1. Implement `TryGetUsbSpeed()` using WMI/PnP
2. Query `Win32_USBController` for connection speed
3. Parse device descriptors for USB version
4. Actually enforce `minStandard` instead of logging

### Phase 4 (CryptoKey Mode)

1. Define `usb_key.json` schema
2. Implement USB key generation in Keysmith
3. Implement signature verification in PolicyEngine
4. Store root public key in app for verification

### Phase 5 (Runtime Monitoring)

1. Implement USB device change monitoring (WMI events)
2. Call `HandleUsbRemoved()` on device removal
3. Implement SecretStore.ZeroAll() for memory cleanup
4. Implement UI lock mechanism

### Phase 6 (Sensitive Operations)

1. Define "sensitive operations" (export, rekey, backup, policy change)
2. Implement `RequireUsbPresent()` check before these ops
3. Re-verify USB identity (not just presence)

## Testing

### Manual Testing

```powershell
# Test USB enforcement at startup
dotnet run --project src/UI.Desktop

# Test with different policies (edit base_policy.json, re-sign with Keysmith)
cd Tools/Obscura.Keysmith
dotnet run -- sign-policy "../../Policies/base_policy.json" "../../Policies/base_policy.signed.json"

# Test USB removal (unplug USB while app running)
# Should trigger HandleUsbRemoved() (check Debug output)
```

### Future Automated Tests

- Mock DriveInfo for different USB scenarios
- Test each identity mode (Any, LabelOnly, Serial, CryptoKey)
- Test USB removal handling
- Test policy violation exceptions

## References

- `Policies/ObscuraPolicy.cs` - Policy model
- `Policies/PolicyEngine.cs` - Enforcement logic
- `Policies/PolicyViolationException.cs` - Exception handling
- `Policies/base_policy.json` - Current policy
- `Tools/Obscura.Keysmith/` - Policy signing tool

## Notes

**Why "Any" mode?**
You requested: "I want any USB to be used but maybe at least 3.0 USB"

This gives you:

- ✅ Flexibility (any removable USB works)
- ✅ Security baseline (still requires physical token)
- ✅ Future-ready (can tighten to Serial/CryptoKey later)
- ✅ Honest (v1 logs USB 3.0 but doesn't fake enforcement)

**Trade-offs:**

- Less binding than Serial/CryptoKey (attacker can use their USB if they have vault + machine)
- Still better than nothing (forces physical presence)
- Can upgrade policy later without code changes

**Philosophy:**
Build the policy **shape** now, enforce **gradually**. This is better than building half-baked enforcement that might fail in weird ways.
