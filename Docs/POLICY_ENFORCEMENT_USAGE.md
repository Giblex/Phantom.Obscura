# Policy Enforcement Usage Guide

## Overview

The PhantomVault application includes a comprehensive security policy system that verifies cryptographic signatures and enforces security rules at runtime. The policy is loaded from embedded resources and verified against a root public key.

## Architecture

csharp
┌─────────────────────────────────────────────────────────────────┐
│                         Program.cs                               │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ 1. Load root_public.json (embedded resource)              │  │
│  │ 2. Load base_policy.signed.json (embedded resource)       │  │
│  │ 3. Create PolicyService (verifies signature)              │  │
│  │ 4. Enforce policies at startup                            │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      PolicyService.cs                            │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ • EnforceDebuggerPolicy()     - Blocks debuggers          │  │
│  │ • EnforceUsbPolicy()          - Requires USB device       │  │
│  │ • EnforceDesktopSyncPolicy()  - Controls sync feature     │  │
│  │ • EnforceManifestPolicy()     - Validates manifest sigs   │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                     PolicyVerifier.cs                            │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ • CreateRootVerifier()    - Loads root public key         │  │
│  │ • VerifyPolicy()          - ECDsa signature verification  │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘

```csharp

## Current Implementation (Startup)

### Program.cs - Application Startup

```csharp
// PHASE 1: Initialize and verify security policy
InitializePolicyService();  // Loads and verifies root_public.json + base_policy.signed.json
Log("Policy verification succeeded");

// PHASE 2: Enforce policy rules
if (_policyService != null)
{
    _policyService.EnforceDebuggerPolicy();  // ✅ ACTIVE: Blocks debuggers if policy forbids
    _policyService.EnforceUsbPolicy();       // ✅ ACTIVE: Checks USB device if required
    Log("All startup policy checks passed");
}

// PHASE 3: Start application
var exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
```

### Global Access

The PolicyService is globally accessible throughout the application:

```csharp
using PhantomVault.UI;

// Access from anywhere in the application
var policyService = Program.PolicyService;

// Check policy settings
bool usbRequired = policyService.IsUsbRequired();
bool syncAllowed = policyService.IsDesktopSyncAllowed();
bool debuggersAllowed = policyService.AreDebuggersAllowed();
```

## Usage Examples

### 1. Desktop Sync Enforcement

Add this check before performing desktop synchronization:

```csharp
// In your SyncService or wherever sync is initiated
public async Task PerformDesktopSyncAsync()
{
    // Enforce policy before sync
    Program.PolicyService.EnforceDesktopSyncPolicy();
    
    // If we reach here, policy allows sync - proceed
    await DoActualSyncOperations();
}
```

**When to call:** Before any desktop sync operation in `SyncService.cs` or similar

### 2. Manifest Verification

Add this check after loading vault manifest:

```csharp
// In your ManifestService or vault loading code
public void LoadVaultManifest(string manifestPath)
{
    var manifest = LoadManifestFromFile(manifestPath);
    var version = manifest.Version;
    
    // Verify signature
    bool signatureValid = VerifyManifestSignature(manifest);
    
    // Enforce policy - throws if signature invalid
    Program.PolicyService.EnforceManifestPolicy(version, signatureValid);
    
    // If we reach here, manifest is valid - proceed
    ProcessManifest(manifest);
}
```

**When to call:** In `ManifestService.LoadManifest()` or `VaultService.LoadVault()` after signature verification

### 3. Runtime USB Check

For features that require USB presence:

```csharp
// In sensitive operations
public void PerformSecureOperation()
{
    // Re-check USB at runtime for critical operations
    if (Program.PolicyService.IsUsbRequired())
    {
        Program.PolicyService.EnforceUsbPolicy();
    }
    
    // Proceed with secure operation
    ExecuteSensitiveTask();
}
```

**When to call:** Before sensitive operations like:

- Opening vault
- Exporting credentials
- Changing master password
- Decrypting secrets

### 4. Conditional Features

Disable UI features based on policy:

```csharp
// In ViewModel initialization
public VaultViewModel()
{
    var policyService = Program.PolicyService;
    
    // Hide/disable sync button if policy forbids it
    IsSyncEnabled = policyService.IsDesktopSyncAllowed();
    
    // Show USB indicator if policy requires it
    ShowUsbIndicator = policyService.IsUsbRequired();
}
```

## Policy File Structure

### root_public.json

```json
{
  "kty": "EC",
  "crv": "P-384",
  "x": "base64-encoded-x-coordinate",
  "y": "base64-encoded-y-coordinate"
}
```

### base_policy.signed.json

```json
{
  "policyId": "phantom-vault-base-policy",
  "version": "1.0.0",
  "requireUsb": false,
  "allowDesktopSync": true,
  "allowDebuggers": true,
  "signature": "base64-encoded-ecdsa-signature"
}
```

## Error Handling

All enforcement methods throw `System.Security.SecurityException` on policy violations:

```csharp
try
{
    Program.PolicyService.EnforceUsbPolicy();
    Program.PolicyService.EnforceDesktopSyncPolicy();
}
catch (System.Security.SecurityException ex)
{
    // Show user-friendly error message
    await ShowErrorDialog("Security Policy Violation", ex.Message);
    // Optionally: log, exit, or disable feature
}
```

## Recommended Integration Points

| Location | Method | Purpose |
|----------|--------|---------|
| `Program.Main()` | ✅ `EnforceDebuggerPolicy()`, `EnforceUsbPolicy()` | **IMPLEMENTED** - Startup checks |
| `SyncService.cs` | ⏳ `EnforceDesktopSyncPolicy()` | **TODO** - Before sync operations |
| `ManifestService.cs` | ⏳ `EnforceManifestPolicy()` | **TODO** - After manifest loading |
| `VaultService.OpenVault()` | ⏳ `EnforceUsbPolicy()` | **TODO** - Before opening vault |
| `SettingsViewModel` | ✅ Feature flags based on `IsXxxAllowed()` | **IMPLEMENTED** - UI conditional rendering |

## Implementation Status

### ✅ Completed

- Root public key verification system
- Signed policy loading and verification
- PolicyService with all enforcement methods
- Startup enforcement (debugger + USB)
- Global PolicyService access via `Program.PolicyService`

### ⏳ Pending

- Desktop sync policy enforcement in sync operations
- Manifest policy enforcement in vault loading
- Runtime USB checks in sensitive operations
- UI feature toggles based on policy flags
- Actual USB device detection implementation (currently placeholder)

## Testing

To test policy enforcement:

1. **Debugger Policy:**
   - Set `"allowDebuggers": false` in policy
   - Attach debugger → Application should exit with SecurityException

2. **USB Policy:**
   - Set `"requireUsb": true` in policy
   - Implement `CheckUsbDevice()` to return false
   - Application should exit with SecurityException

3. **Sync Policy:**
   - Set `"allowDesktopSync": false` in policy
   - Attempt sync → Should throw SecurityException

4. **Manifest Policy:**
   - Load manifest with invalid signature
   - Call `EnforceManifestPolicy()` → Should throw SecurityException

## Security Notes

⚠️ **Important Security Considerations:**

1. Policy files are **embedded resources** - cannot be modified after compilation
2. Signature verification uses **ECDsa P-384** - strong cryptographic security
3. Root public key is **separate** from policy - allows policy updates with same key
4. Policy violations throw **SecurityException** - application must handle gracefully
5. USB detection is **placeholder** - requires hardware-specific implementation

## Next Steps

1. Integrate `EnforceDesktopSyncPolicy()` in sync service
2. Integrate `EnforceManifestPolicy()` in manifest loading
3. Implement actual USB device detection logic
4. Add UI policy indicators (USB required, sync allowed, etc.)
5. Create user documentation for policy system
