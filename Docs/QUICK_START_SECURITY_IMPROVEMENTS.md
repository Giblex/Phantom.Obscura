# Quick Start: Using Security Improvements

**For Developers**: This guide shows how to use the new secure password handling system in your code.

---

## 🔐 Secure Password Handling

### Basic Usage

```csharp
using PhantomVault.Core.Utils;

// Get password from user (e.g., from UI TextBox)
string userPassword = passwordTextBox.Text;

// Convert to SecurePassword immediately
using var securePassword = SecurePassword.FromString(userPassword);

// Clear the original string reference (best effort)
userPassword = null;

// Use the secure password
manifestService.WriteManifestSecure(manifest, filePath, securePassword, keyfilePath);

// Password automatically zeroed when disposed
```

### Combining with Keyfile

```csharp
// The SecurePasswordCombiner handles this automatically
using var securePassword = SecurePassword.FromString(userInput);
using var combined = SecurePasswordCombiner.Combine(securePassword, keyfilePath);

// Derive encryption key
byte[] key = encryptionService.DeriveKey(combined.AsSpan(), salt);
try {
    // Use the key...
} finally {
    CryptographicOperations.ZeroMemory(key);
}
// combined and securePassword automatically zeroed
```

### Empty Password (Keyfile-Only)

```csharp
// For keyfile-only authentication
using var emptyPassword = SecurePassword.Empty();
manifestService.WriteManifestSecure(manifest, filePath, emptyPassword, keyfilePath);
```

---

## 🎯 Feature Availability Checking

### Check if Feature is Available

```csharp
var featureService = new FeatureAvailabilityService();

if (featureService.IsFeatureAvailable("YubiKey.FIDO2")) {
    // Enable YubiKey UI options
    yubiKeyButton.IsEnabled = true;
} else {
    // Show limitation message
    var message = featureService.GetFeatureLimitationMessage("YubiKey.FIDO2");
    yubiKeyButton.ToolTip = message;
    yubiKeyButton.IsEnabled = false;
}
```

### Get Feature Status

```csharp
var status = featureService.GetFeatureStatus("YubiKey.FIDO2");

if (status != null) {
    Console.WriteLine($"Available: {status.IsAvailable}");
    Console.WriteLine($"Implemented: {status.IsFullyImplemented}");
    Console.WriteLine($"Message: {status.LimitationMessage}");
    Console.WriteLine($"Docs: {status.DocumentationUrl}");
}
```

### Handle Feature Exceptions

```csharp
try {
    yubiKeyService.Authenticate(relyingParty, credentialId, challenge);
} catch (FeatureNotImplementedException ex) {
    // Show user-friendly error message
    MessageBox.Show(
        ex.Message,  // Already user-friendly
        "Feature Not Available",
        MessageBoxButton.OK,
        MessageBoxImage.Information
    );

    // Optionally show documentation link
    if (ex.Data.Contains("DocumentationUrl")) {
        var url = ex.Data["DocumentationUrl"];
        // Show "Learn More" button
    }
}
```

---

## 🔒 Migration from Old API

### Before (Insecure)

```csharp
// OLD CODE - DO NOT USE
public void UnlockVault(string passphrase) {
    manifestService.WriteManifest(
        manifest,
        filePath,
        passphrase,  // String stays in memory!
        keyfilePath
    );
}
```

### After (Secure)

```csharp
// NEW CODE - RECOMMENDED
public void UnlockVault(SecurePassword passphrase) {
    manifestService.WriteManifestSecure(
        manifest,
        filePath,
        passphrase,  // Will be zeroed after use
        keyfilePath
    );
}
```

### Quick Migration Pattern

```csharp
// If you have existing string-based code:
public void ExistingMethod(string password) {
    // Wrap immediately in SecurePassword
    using var securePassword = SecurePassword.FromString(password);

    // Clear original string reference
    password = null;

    // Call new secure method
    NewSecureMethod(securePassword);
}

public void NewSecureMethod(SecurePassword password) {
    // Use secure password...
}
```

---

## 🛡️ Bypass Flag Restrictions

### Development Mode (DEBUG builds)

```bash
# Set bypass flag for local development
set PHANTOM_DEV_BYPASS_POLICY=1
dotnet run --configuration Debug

# Output: ⚠️ DEVELOPMENT MODE: Policy bypass enabled!
```

### Production Mode (RELEASE builds)

```bash
# Bypass flag ignored in production
set PHANTOM_DEV_BYPASS_POLICY=1
dotnet run --configuration Release

# Bypass flag has no effect, policies enforced normally
```

### Checking in Code

```csharp
#if DEBUG
    // This code only exists in DEBUG builds
    var devMode = Environment.GetEnvironmentVariable("PHANTOM_DEV_BYPASS_POLICY") == "1";
#else
    // In RELEASE builds, always false
    var devMode = false;
#endif

if (devMode) {
    Log.Warning("⚠️ Development mode active!");
} else {
    // Enforce security policies
    policyService.EnforceAll();
}
```

---

## 📋 Common Patterns

### Pattern 1: UI Password Input

```csharp
public class UnlockViewModel : ViewModelBase {
    private string _passphraseInput = string.Empty;

    public async Task UnlockAsync() {
        // Convert to SecurePassword immediately
        using var securePassword = SecurePassword.FromString(_passphraseInput);

        // Clear the UI field
        _passphraseInput = string.Empty;
        OnPropertyChanged(nameof(PassphraseInput));

        try {
            var manifest = manifestService.ReadManifestSecure(
                vaultPath,
                securePassword,
                keyfilePath
            );

            // Success!
        } catch (Exception ex) {
            // Handle error
        }
        // securePassword automatically zeroed
    }
}
```

### Pattern 2: Service Method with Secure Password

```csharp
public class VaultService {
    public async Task<VaultManifest> UnlockVaultAsync(
        string vaultPath,
        SecurePassword passphrase,  // Use SecurePassword parameter
        string? keyfilePath,
        CancellationToken ct = default)
    {
        // Combine with keyfile
        using var combined = SecurePasswordCombiner.Combine(
            passphrase,
            keyfilePath,
            requireKeyfile: false
        );

        // Derive key
        byte[] key = _encryptionService.DeriveKey(combined.AsSpan(), salt);
        try {
            // Use key for decryption...
            return manifest;
        } finally {
            // Always zero the key
            CryptographicOperations.ZeroMemory(key);
        }
        // combined automatically zeroed
    }
}
```

### Pattern 3: Multiple Operations with Same Password

```csharp
public async Task PerformMultipleOperationsAsync(string userPassword) {
    // Create SecurePassword once
    using var securePassword = SecurePassword.FromString(userPassword);
    userPassword = null;  // Clear original

    // Use for multiple operations
    await UnlockVaultAsync(securePassword);
    await LoadCredentialsAsync(securePassword);
    await SynchronizeAsync(securePassword);

    // All operations use the same SecurePassword
    // Automatically zeroed at the end
}
```

---

## ⚠️ Common Mistakes to Avoid

### ❌ DON'T: Keep string after conversion

```csharp
// BAD - original string still in memory
var securePassword = SecurePassword.FromString(password);
Console.WriteLine(password);  // Original still accessible!
```

### ✅ DO: Clear string reference immediately

```csharp
// GOOD - clear original immediately
using var securePassword = SecurePassword.FromString(password);
password = null;  // Clear reference
```

### ❌ DON'T: Forget to dispose

```csharp
// BAD - memory never zeroed
var securePassword = SecurePassword.FromString(password);
SomeMethod(securePassword.AsSpan());
// Password still in memory!
```

### ✅ DO: Always use 'using' statement

```csharp
// GOOD - automatic disposal
using var securePassword = SecurePassword.FromString(password);
SomeMethod(securePassword.AsSpan());
// Automatically zeroed
```

### ❌ DON'T: Access after disposal

```csharp
// BAD - ObjectDisposedException
using var securePassword = SecurePassword.FromString(password);
securePassword.Dispose();
var span = securePassword.AsSpan();  // THROWS!
```

### ✅ DO: Keep in scope while needed

```csharp
// GOOD - use within scope
using var securePassword = SecurePassword.FromString(password);
{
    var span = securePassword.AsSpan();
    SomeMethod(span);
}  // Disposed here
```

---

## 🧪 Testing Secure Password Handling

### Unit Test Example

```csharp
[Fact]
public void SecurePassword_ProperlyZerosMemory() {
    const string testPassword = "TestPassword123!";
    byte[] memorySnapshot;

    // Create and immediately dispose
    using (var securePassword = SecurePassword.FromString(testPassword)) {
        // Password is in memory
        Assert.False(securePassword.IsEmpty);
        Assert.Equal(testPassword.Length, securePassword.Length);
    }
    // Password should be zeroed now

    // Memory should not contain the password anymore
    // (In real tests, use memory profiler to verify)
}
```

### Integration Test Example

```csharp
[Fact]
public async Task ManifestService_SecurePassword_WorksCorrectly() {
    // Arrange
    using var securePassword = SecurePassword.FromString("test123");
    var manifest = new VaultManifest { /* ... */ };
    var tempFile = Path.GetTempFileName();

    try {
        // Act
        manifestService.WriteManifestSecure(
            manifest,
            tempFile,
            securePassword,
            keyfilePath: null
        );

        // Assert
        Assert.True(File.Exists(tempFile));

        // Verify can read back
        using var securePassword2 = SecurePassword.FromString("test123");
        var loaded = manifestService.ReadManifestSecure(
            tempFile,
            securePassword2,
            keyfilePath: null
        );

        Assert.NotNull(loaded);
    } finally {
        File.Delete(tempFile);
    }
}
```

---

## 📚 Additional Resources

- **Implementation Guide**: `SECURITY_IMPROVEMENTS_IMPLEMENTATION.md`
- **Complete Summary**: `IMPLEMENTATION_COMPLETE_SUMMARY.md`
- **Feature Documentation**: Query `FeatureAvailabilityService.GetAllFeatures()`
- **Code Examples**: See inline XML documentation in source files

---

## 🆘 Troubleshooting

### Problem: Obsolete Warnings

```
warning CS0618: 'ManifestService.WriteManifest(...)' is obsolete
```

**Solution**: Migrate to the secure overload:
```csharp
// Change this:
manifestService.WriteManifest(manifest, path, password, keyfile);

// To this:
using var securePassword = SecurePassword.FromString(password);
password = null;
manifestService.WriteManifestSecure(manifest, path, securePassword, keyfile);
```

### Problem: ObjectDisposedException

```
ObjectDisposedException: Cannot access a disposed object
```

**Solution**: Don't access SecurePassword after disposal:
```csharp
// BAD:
var securePassword = SecurePassword.FromString(password);
securePassword.Dispose();
var span = securePassword.AsSpan();  // THROWS!

// GOOD:
using (var securePassword = SecurePassword.FromString(password)) {
    var span = securePassword.AsSpan();  // OK - still in scope
}
```

### Problem: FeatureNotImplementedException

```
FeatureNotImplementedException: YubiKey FIDO2 authentication is not yet implemented
```

**Solution**: Use the suggested workaround in the error message:
```csharp
try {
    yubiKeyService.Authenticate(...);
} catch (FeatureNotImplementedException ex) {
    // Use keyfile + passphrase instead as suggested
    UseKefileAuthentication();
}
```

---

**Last Updated**: January 13, 2026
**Version**: PhantomVault V6
**Status**: Production Ready (after code review)
