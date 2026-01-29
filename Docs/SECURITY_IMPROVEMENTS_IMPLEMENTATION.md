# Security Improvements Implementation Guide

## Overview
This document tracks the implementation of critical security improvements identified in the code review.

## Priority 1: Secure Password Handling (IN PROGRESS)

### Completed
✅ Created `SecurePassword` class (`src/Core/Utils/SecurePassword.cs`)
  - Properly pins and zeros password memory
  - Three-pass secure memory wiping (random → zeros → random)
  - IDisposable pattern for automatic cleanup
  - Supports creating from string, SecureString, or empty

✅ Created `SecurePasswordCombiner` class (`src/Core/Utils/SecurePasswordCombiner.cs`)
  - Securely combines passphrase + keyfile contents
  - Zeros all intermediate buffers
  - Replaces string concatenation in `CombineSecret` method

✅ Updated `ManifestService` with secure overloads
  - Added `WriteManifestSecure(SecurePassword)` method
  - Marked old `WriteManifest(string)` as Obsolete
  - Marked old `ReadManifest(string)` as Obsolete
  - Extracted validation methods

### Remaining Work
🔲 Complete ReadManifestSecure implementation in ManifestService
🔲 Update VaultService with secure overloads for CreateVaultAsync and MountVaultAsync
🔲 Update BackupService with secure overloads
🔲 Update IntrusionService with secure overloads
🔲 Update MigrationService with secure overloads
🔲 Update all ViewModels to use SecurePassword instead of string
🔲 Update UI layer password input controls to return SecurePassword
🔲 Add migration guide for existing code
🔲 Update unit tests to use SecurePassword

### Implementation Pattern
```csharp
// OLD (insecure - string remains in memory):
public void DoSomething(string? passphrase)
{
    var key = DeriveKey(passphrase.AsSpan(), salt);
    // passphrase string still in memory!
}

// NEW (secure - password properly zeroed):
public void DoSomethingSecure(SecurePassword passphrase)
{
    using var combined = SecurePasswordCombiner.Combine(passphrase, keyfilePath);
    var key = DeriveKey(combined.AsSpan(), salt);
    try
    {
        // Use key
    }
    finally
    {
        CryptographicOperations.ZeroMemory(key);
    }
    // combined and passphrase automatically zeroed on dispose
}
```

## Priority 2: Complete or Remove Stubs (PENDING)

### YubiKey FIDO2 Implementation
**Location**: `src/Core/Services/YubiKeyService.cs`

**Issues Found**:
- Line 79: `TODO: Call native Yubico.YubiKey library`
- Line 112: `TODO: Implement FIDO2 assertion verification`
- Line 175: `TODO: Implement YubiKey PIV certificate operations`

**Recommended Actions**:
1. **Option A: Complete Implementation**
   - Add NuGet package: `Yubico.YubiKey` (official SDK)
   - Implement actual FIDO2 operations
   - Add proper error handling for missing YubiKey
   - Test with physical YubiKey hardware

2. **Option B: Graceful Degradation** (RECOMMENDED)
   - Add clear UI warnings when YubiKey not available
   - Throw `NotSupportedException` with helpful message
   - Disable YubiKey options in UI when service unavailable
   - Document limitation in README

### Biometric Authentication
**Location**: `src/Core/Services/Security/SystemSecurityController.cs`

**Issues Found**:
- Line 23-34: Clipboard clearing is placeholder implementation
- Touch ID/Face ID detection incomplete (line 307, 313)

**Recommended Actions**:
1. Implement platform-specific clipboard clearing:
   ```csharp
   #if WINDOWS
   Clipboard.Clear(); // Already works
   #elif MACOS
   NSPasteboard.GeneralPasteboard.ClearContents();
   #elif LINUX
   // Use xclip or wl-clipboard
   #endif
   ```

2. Complete biometric detection or disable feature

### SecurityCheckService
**Location**: `src/Core/Services/SecurityCheckService.cs`
- Line 137: TODO for cryptographic signature verification

**Recommended Action**:
- Either complete X.509 certificate validation
- OR document as future feature and remove from UI

## Priority 3: Remove Developer Bypass Flags (PENDING)

### Identified Bypass Mechanisms
1. `PHANTOM_DEV_BYPASS_POLICY` - Bypasses all policy enforcement
2. `PHANTOM_ALLOW_UNSIGNED_POLICY` - Allows unsigned policy files

### Locations to Fix
```bash
# Find all usages
grep -r "PHANTOM_DEV_BYPASS_POLICY" src/
grep -r "PHANTOM_ALLOW_UNSIGNED_POLICY" src/
```

### Recommended Implementation
```csharp
// Add in each file that checks bypass flags:
#if DEBUG
private static bool IsDevelopmentBypass()
{
    return Environment.GetEnvironmentVariable("PHANTOM_DEV_BYPASS_POLICY") == "1";
}
#else
private static bool IsDevelopmentBypass() => false; // Never allow in release
#endif
```

**Files to Update**:
- `Policies/ObscuraPolicy.cs`
- `Policies/PolicyEngine.cs`
- `Policies/PolicySynchronizer.cs`
- Any service checking these environment variables

## Priority 4: Add Constant-Time Comparison (PENDING)

### Current Status
✅ ManifestService already uses `CryptographicOperations.FixedTimeEquals` (line 520)

### Files to Audit
🔲 Search for string/byte comparisons in authentication paths:
```bash
grep -r "== passphrase" src/
grep -r "Equals.*password" src/
grep -r "Equals.*key" src/
```

🔲 Replace with `CryptographicOperations.FixedTimeEquals()` where applicable

### Pattern to Replace
```csharp
// BAD (timing attack vulnerable):
if (providedPassword == storedPassword) { }
if (providedKey.SequenceEqual(storedKey)) { }

// GOOD (constant-time):
if (CryptographicOperations.FixedTimeEquals(
    Encoding.UTF8.GetBytes(providedPassword),
    Encoding.UTF8.GetBytes(storedPassword))) { }
```

## Priority 5: Refactor Large ViewModels (PENDING)

### VaultViewModel.cs Analysis
**Current Size**: 150+ properties, ~2000+ lines (estimated)

**Suggested Breakdown**:
1. **CredentialManagementViewModel** - CRUD operations for credentials
2. **SearchAndFilterViewModel** - Search, filter, favorites logic
3. **SecurityViewModel** - Lock/unlock, PIN, security coordinator
4. **ImportExportViewModel** - Import/export operations
5. **UndoRedoViewModel** - Undo/redo stack management

**Implementation Steps**:
1. Create new focused ViewModels
2. Use composition in VaultViewModel:
   ```csharp
   public class VaultViewModel : ViewModelBase
   {
       public CredentialManagementViewModel Credentials { get; }
       public SearchAndFilterViewModel SearchFilter { get; }
       public SecurityViewModel Security { get; }
       // etc...
   }
   ```
3. Update XAML bindings to use sub-properties
4. Test thoroughly

## Priority 6: Integration Tests (PENDING)

### Tests to Create
**DefenceEngine Tests**:
- `tests/PhantomVault.Core.Tests/Services/Security/DefenceEngineIntegrationTests.cs`

Test scenarios:
- Threat detection and response
- Cooldown mechanisms
- Incident scoring
- Rule evaluation
- Policy integration

**Intrusion Detection Tests**:
- `tests/PhantomVault.Core.Tests/Services/IntrusionServiceIntegrationTests.cs`

Test scenarios:
- Failed attempt tracking
- Progressive lockout
- Cooldown timing
- Self-destruct trigger
- DefenceEngine integration

### Test Template
```csharp
[Fact]
public async Task DefenceEngine_DetectsThreat_TriggersLockdown()
{
    // Arrange
    var defenceEngine = new DefenceEngine(mockPolicy);
    var mockManifest = CreateTestManifest();

    // Act
    for (int i = 0; i < 6; i++)
    {
        await defenceEngine.RecordFailedUnlockAsync();
    }

    // Assert
    Assert.True(defenceEngine.IsLockedOut);
    Assert.Equal(ThreatLevel.High, defenceEngine.CurrentThreatLevel);
}
```

## Priority 7: Policy Validation UI (PENDING)

### Requirements
1. **Safe Defaults**: Pre-populate policy editor with secure defaults
2. **Validation**: Real-time validation of policy values
3. **Preview**: Show what policy changes will affect
4. **Warnings**: Alert user to risky configurations

### UI Components to Create
- `src/UI.Desktop/Views/Settings/PolicyEditorWindow.axaml`
- `src/UI.Desktop/ViewModels/Settings/PolicyEditorViewModel.cs`

### Features
```csharp
public class PolicyEditorViewModel : ViewModelBase
{
    // Validation
    public ObservableCollection<ValidationError> Errors { get; }

    // Safe defaults
    public void LoadSafeDefaults() { /* ... */ }

    // Preview
    public PolicyPreview GetPreview() { /* ... */ }

    // Warnings
    public IEnumerable<PolicyWarning> GetWarnings() { /* ... */ }
}
```

## Priority 8: VeraCrypt Integration (PENDING)

### Current Issues
- VeraCrypt must be manually installed
- Path must be manually configured
- No auto-detection
- Poor first-run experience

### Solutions

**Option A: Bundle VeraCrypt** (RECOMMENDED)
- Include VeraCrypt binaries in distribution
- License: VeraCrypt is Apache 2.0 compatible
- Update installer to include VeraCrypt
- Verify signatures on startup

**Option B: Auto-Detection**
```csharp
public class VeraCryptDetector
{
    private static readonly string[] CommonPaths = new[]
    {
        @"C:\Program Files\VeraCrypt\VeraCrypt.exe",
        @"C:\Program Files (x86)\VeraCrypt\VeraCrypt.exe",
        @"/usr/bin/veracrypt",
        @"/Applications/VeraCrypt.app/Contents/MacOS/VeraCrypt"
    };

    public static string? DetectVeraCryptPath()
    {
        return CommonPaths.FirstOrDefault(File.Exists);
    }
}
```

**Option C: Download on First Run**
- Detect if VeraCrypt missing
- Show download dialog with official VeraCrypt link
- Guide user through installation
- Retry detection after install

## Priority 9: Error Messages (PENDING)

### Current Issues
Generic error messages like:
- "Manifest file not found"
- "Decryption failed"
- "Invalid passphrase"

### Improved Messages
```csharp
// Before:
throw new Exception("Failed to unlock vault");

// After:
throw new VaultUnlockException(
    "Failed to unlock vault. Possible causes:\n" +
    "• Incorrect passphrase or keyfile\n" +
    "• USB drive not connected (if USB-bound)\n" +
    "• Vault file corrupted\n" +
    "• YubiKey not inserted (if required)\n" +
    $"Attempts remaining: {attemptsLeft}/5"
);
```

### Files to Update
- All service classes in `src/Core/Services/`
- Add custom exception types in `src/Core/Exceptions/`
- Update UI to display detailed errors

## Priority 10: First-Run Wizard (PENDING)

### Requirements
1. Welcome screen explaining PhantomVault
2. VeraCrypt installation check
3. Guided vault creation
4. Optional YubiKey setup
5. Import from existing password manager
6. Quick tutorial

### Implementation
Create `src/UI.Desktop/Views/FirstRunWizard/`:
- `WelcomeStep.axaml` - Introduction
- `VeraCryptCheckStep.axaml` - Verify VeraCrypt installed
- `VaultCreationStep.axaml` - Create first vault
- `SecuritySetupStep.axaml` - YubiKey, USB binding
- `ImportStep.axaml` - Import existing passwords
- `TutorialStep.axaml` - Quick feature tour

## Implementation Timeline

### Week 1 (HIGH PRIORITY)
- [ ] Complete secure password handling migration
- [ ] Remove developer bypass flags from production builds
- [ ] Add constant-time comparison throughout

### Week 2 (MEDIUM PRIORITY)
- [ ] Complete or remove YubiKey/biometric stubs
- [ ] Refactor large ViewModels
- [ ] Add integration tests

### Week 3 (MEDIUM-LOW PRIORITY)
- [ ] Implement policy validation UI
- [ ] Improve error messages
- [ ] VeraCrypt auto-detection/bundling

### Week 4 (POLISH)
- [ ] Create first-run wizard
- [ ] Documentation updates
- [ ] User testing and refinement

## Testing Checklist

After each priority completed:
- [ ] Unit tests pass
- [ ] Integration tests pass
- [ ] Manual testing completed
- [ ] Code review performed
- [ ] Documentation updated
- [ ] No new compiler warnings
- [ ] Performance impact assessed

## Rollback Plan

If issues arise:
1. Keep old string-based methods with `[Obsolete]` attribute
2. Allow gradual migration
3. Remove old methods only after 2-3 release cycles
4. Maintain backward compatibility in manifest format

## Notes

- All secure password methods use `using` pattern for automatic disposal
- Memory zeroization uses three-pass overwrite for extra security
- Obsolete methods will be removed in v7.0.0
- Document breaking changes in CHANGELOG.md
