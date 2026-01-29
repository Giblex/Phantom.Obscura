# Android Emulator Developer Bypass

## Overview

PhantomVault Mobile includes a comprehensive developer bypass system for testing on Android emulators. This allows development and testing without requiring physical PhantomKey hardware or USB binding.

## Features

### Automatic Emulator Detection

The app automatically detects when running on an Android emulator and enables developer mode:

- **Google Android Emulator (AVD)** - Standard Android Studio emulator
- **Genymotion** - Popular Android emulator for developers
- **Bluestacks** - Android gaming/app emulator
- **Generic Emulators** - Any emulator with standard signatures

### Developer Mode Capabilities

When running in DEBUG mode on an emulator, the following bypasses are enabled:

1. **PhantomKey Bypass** - Skips hardware key verification
2. **USB Binding Bypass** - Skips USB vault drive requirements
3. **Biometric Bypass** - Skips fingerprint/face authentication
4. **Auto-Credentials** - Automatically fills developer credentials
5. **Simulated Storage** - Creates virtual vault and USB directories

## Configuration

### Default Developer Credentials

```csharp
Master Password: DevMaster2025!
PIN: 1234
```

### Storage Paths

Developer mode creates isolated storage locations:

- **Vault Path**: `%LocalApplicationData%/PhantomVault_DevAndroid`
- **USB Path**: `%LocalApplicationData%/PhantomVault_SimulatedUSB_Android`

### MobileDeveloperMode Settings

You can customize developer mode behavior:

```csharp
// Enable/disable features
MobileDeveloperMode.IsEnabled = true;
MobileDeveloperMode.SkipUsbBindingCheck = true;
MobileDeveloperMode.SkipBiometricAuth = true;
MobileDeveloperMode.BypassPhantomKey = true;
MobileDeveloperMode.AutoCreateVault = true;
MobileDeveloperMode.VerboseLogging = true;

// Custom paths
MobileDeveloperMode.DeveloperVaultPath = "custom/path";
MobileDeveloperMode.DeveloperUsbPath = "custom/usb/path";
```

## Android Emulator Setup

### Using Android Studio AVD Manager

#### 1. Create New Virtual Device

```bash
# Open AVD Manager
Android Studio → Tools → Device Manager → Create Device
```

#### 2. Recommended Configuration

- **Device**: Pixel 5 or newer
- **System Image**: Android 12.0 (API 31) or higher
- **Graphics**: Hardware - GLES 2.0
- **RAM**: 4096 MB minimum
- **Internal Storage**: 8 GB minimum

#### 3. Advanced Settings

Enable the following for best PhantomVault testing:

```
✓ Hardware keyboard
✓ USB debugging
✓ SD card support (optional - for USB simulation)
```

### Using Command Line

```bash
# List available system images
sdkmanager --list | grep "system-images"

# Download Android 12 system image
sdkmanager "system-images;android-31;google_apis;x86_64"

# Create AVD
avdmanager create avd \
  -n PhantomVault_Test \
  -k "system-images;android-31;google_apis;x86_64" \
  -d "pixel_5" \
  -g "google_apis"

# Launch emulator
emulator -avd PhantomVault_Test
```

### Using Genymotion

1. **Download Genymotion Desktop**
   - Visit: <https://www.genymotion.com/download/>
   - Install for your platform

2. **Create Virtual Device**

   ```
   Device: Google Pixel 5
   Android Version: 12.0 (API 31)
   ```

3. **Configure Genymotion**
   - Enable ADB: Settings → ADB → Use custom Android SDK tools
   - Point to Android SDK location

## Building for Emulator

### Debug Build (Developer Mode Enabled)

```bash
# Navigate to mobile project
cd src/UI.Mobile

# Clean build
dotnet clean

# Build for Android Debug
dotnet build -c Debug -f net8.0-android

# Run on emulator
dotnet build -t:Run -f net8.0-android
```

### Release Build (Developer Mode Disabled)

```bash
# Release builds automatically disable developer mode
dotnet build -c Release -f net8.0-android
```

## Testing Developer Bypass

### 1. Launch App on Emulator

```bash
# Using Visual Studio
F5 (Debug) or Ctrl+F5 (Run without debugging)

# Using command line
dotnet build -t:Run -f net8.0-android -c Debug
```

### 2. Verify Developer Mode

Check the debug output for:

```
[MobileDeveloperMode] Developer mode auto-enabled for Android emulator
[MobileDeveloperMode] Device Info: Brand: google, Device: emu64a, ...
=== Android Device Detection ===
Type: Google Android Emulator (AVD)
Is Emulator: True
```

### 3. Test Unlock Flow

1. App opens to VaultUnlockPage
2. **Password field auto-filled** with `DevMaster2025!`
3. Status shows: `Developer Mode: Credentials auto-filled`
4. Click **Unlock** button
5. Developer bypass activates:
   - Status: `Developer Mode: Bypassing PhantomKey verification...`
   - Status: `Developer Mode: Skipping USB binding check`
   - Status: `Developer Mode: Vault unlocked successfully!`

### 4. Verify Storage

Developer vault created at:

```
/data/user/0/com.phantomvault.mobile/files/.local/share/PhantomVault_DevAndroid
```

Check using ADB:

```bash
adb shell
cd /data/data/com.phantomvault.mobile
ls -la
```

## Debugging Tips

### Enable Verbose Logging

Developer mode includes extensive logging:

```csharp
MobileDeveloperMode.Log("Custom debug message");
```

View logs:

```bash
# Android Logcat
adb logcat | grep "MobileDeveloperMode"

# Visual Studio Output Window
Debug → Windows → Output → Show output from: Debug
```

### Check Emulator Detection

```csharp
// Get device info programmatically
var info = MobileDeveloperMode.GetDeviceInfo();
var type = EmulatorDetector.GetEmulatorType();
var isEmulator = MobileDeveloperMode.IsAndroidEmulator;
```

### Common Issues

**Issue**: Developer mode not enabling

```bash
Solution: Ensure building in DEBUG configuration
Verify: Check Build.Brand contains "generic"
```

**Issue**: Storage path errors

```bash
Solution: Check app has storage permissions
Verify: AndroidManifest.xml includes storage permissions
```

**Issue**: Auto-fill not working

```bash
Solution: Restart emulator and rebuild
Verify: Check MobileDeveloperMode.IsEnabled == true
```

## Security Notes

⚠️ **Developer mode is automatically disabled in Release builds**

The `#if DEBUG` preprocessor directives ensure:

- Developer bypass code is excluded from release builds
- No developer credentials in production
- Emulator detection disabled in production

### Production Safety Checks

```csharp
#if DEBUG
    // Developer code only compiled in debug builds
    if (MobileDeveloperMode.IsEnabled)
    {
        // Never executes in release
    }
#endif
```

## Advanced Usage

### Manual Developer Mode Activation

Force enable (DEBUG only):

```csharp
#if DEBUG
    MobileDeveloperMode.IsEnabled = true;
    MobileDeveloperMode.InitializeDeveloperPaths();
#endif
```

### Custom Test Scenarios

```csharp
#if DEBUG
    // Simulate specific vault state
    MobileDeveloperMode.IsEnabled = true;
    MobileDeveloperMode.DeveloperVaultPath = "custom/test/vault";
    
    // Create test vault with specific data
    var testVault = CreateTestVault();
    testVault.SaveTo(MobileDeveloperMode.DeveloperVaultPath);
#endif
```

### Cleanup Test Data

```csharp
#if DEBUG
    // Clear all developer data
    MobileDeveloperMode.CleanupDeveloperData();
#endif
```

## CI/CD Integration

### GitHub Actions Example

```yaml
- name: Run Android Emulator Tests
  uses: reactivecircus/android-emulator-runner@v2
  with:
    api-level: 31
    target: google_apis
    arch: x86_64
    profile: pixel_5
    script: |
      dotnet test \
        --configuration Debug \
        --filter "Category=EmulatorTests"
```

### Test Detection

Tests can detect emulator mode:

```csharp
[Test]
[Category("EmulatorTests")]
public void TestDeveloperMode()
{
    Assert.IsTrue(MobileDeveloperMode.IsAndroidEmulator);
    Assert.IsTrue(MobileDeveloperMode.IsEnabled);
}
```

## FAQ

**Q: Why doesn't developer mode work on physical devices?**
A: Developer mode only activates on detected emulators for security.

**Q: Can I disable auto-fill?**
A: Yes, set `MobileDeveloperMode.BypassPhantomKey = false` before app starts.

**Q: How do I test USB binding?**
A: Set `MobileDeveloperMode.SkipUsbBindingCheck = false` and use DeveloperUsbPath.

**Q: Will this affect release builds?**
A: No, all developer code is excluded via `#if DEBUG` preprocessor directives.

**Q: Can I use this on iOS Simulator?**
A: The code structure supports it, but iOS-specific emulator detection needs implementation.

## References

- [Android Emulator Documentation](https://developer.android.com/studio/run/emulator)
- [AVD Manager Guide](https://developer.android.com/studio/run/managing-avds)
- [MAUI Android Deployment](https://learn.microsoft.com/en-us/dotnet/maui/android/deployment/)
- [ADB Documentation](https://developer.android.com/tools/adb)
