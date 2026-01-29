# PhantomVault Mobile - Quick Start Guide

## Prerequisites

### Required Software:
- ✅ .NET 8 SDK ([download](https://dotnet.microsoft.com/download/dotnet/8.0))
- ✅ Visual Studio 2022 17.8+ or JetBrains Rider 2023.3+
- ✅ Android SDK (via Visual Studio installer or Android Studio)
- ✅ Xcode 15+ (macOS only, for iOS development)

### Visual Studio Workloads:
- .NET Multi-platform App UI development
- Android SDK (API 21-34)
- iOS development tools (macOS only)

## Project Structure

```
src/UI.Mobile/
├── MauiProgram.cs                    # DI configuration
├── PhantomVault.Mobile.csproj        # Project file
├── Platforms/
│   ├── Android/
│   │   ├── AndroidManifest.xml       # Permissions
│   │   └── AndroidUsbDetector.cs     # USB OTG detection
│   └── iOS/
│       ├── Entitlements.plist        # Capabilities
│       └── iOSUsbDetector.cs         # USB-C/Lightning detection
├── Views/
│   ├── VaultUnlockPage.xaml          # Unlock UI
│   └── VaultUnlockPage.xaml.cs       # Code-behind
└── ViewModels/
    └── VaultUnlockViewModel.cs       # Business logic
```

## Building the App

### Command Line:

```bash
# Navigate to mobile project
cd "G:\Users\Giblex\Build Projects\PhantomObscuraV6\src\UI.Mobile"

# Restore NuGet packages
dotnet restore

# Build for Android
dotnet build -f net8.0-android

# Build for iOS (macOS only)
dotnet build -f net8.0-ios
```

### Visual Studio:

1. Open `PhantomObscuraV6.sln`
2. Right-click `PhantomVault.Mobile` project
3. Select "Set as Startup Project"
4. Choose target framework:
   - **Android**: net8.0-android
   - **iOS**: net8.0-ios
5. Click "Build Solution" (Ctrl+Shift+B)

## Running on Emulator/Simulator

### Android Emulator:

```bash
# List available emulators
dotnet build -t:Run -f net8.0-android

# Or use Visual Studio:
# 1. Click target device dropdown
# 2. Select "Android Emulator"
# 3. Choose device (e.g., Pixel 8 API 34)
# 4. Press F5 to run
```

**Note**: Android Emulator doesn't support USB passthrough. Use physical device for USB testing.

### iOS Simulator (macOS only):

```bash
# Run on iOS simulator
dotnet build -t:Run -f net8.0-ios

# Or use Visual Studio/Xcode:
# 1. Select iOS Simulator device (e.g., iPhone 15)
# 2. Press F5 to run
```

**Note**: iOS Simulator doesn't support USB drives. Use physical device for USB testing.

## Running on Physical Devices

### Android Device:

1. **Enable Developer Options**:
   - Go to Settings → About Phone
   - Tap "Build Number" 7 times
   - Go back to Settings → System → Developer Options
   - Enable "USB Debugging"

2. **Connect Device**:
   - Connect via USB cable
   - Accept "Allow USB debugging" prompt on phone
   - Select device in Visual Studio dropdown
   - Press F5 to deploy

3. **Grant Permissions** (first run):
   - Tap "Allow" when prompted for storage access
   - Go to Settings → Apps → PhantomVault → Permissions
   - Enable "Files and media" or "Storage"

### iOS Device (macOS only):

1. **Register Device**:
   - Connect iPhone/iPad via USB
   - Open Xcode → Window → Devices and Simulators
   - Click "+" to add device
   - Trust computer on device when prompted

2. **Provisioning Profile**:
   - Create Apple Developer account (free for testing)
   - In Visual Studio, go to iOS Bundle Signing
   - Select your Apple ID
   - Let Xcode automatically manage signing

3. **Deploy**:
   - Select your physical device in Visual Studio
   - Press F5 to build and deploy
   - First time: Go to Settings → General → VPN & Device Management
   - Trust your developer certificate

## Testing USB Vault Access

### Prepare Test USB Drive:

```bash
# 1. Create vault on USB drive using desktop app first
# 2. USB structure should look like:
E:\
├── .phantom\
│   ├── vault.pvdb
│   ├── vault.manifest
│   └── backups\
└── .phantom_device_id
```

### Android USB OTG Testing:

1. **Hardware**:
   - USB OTG adapter (USB-C or micro-USB)
   - USB flash drive with PhantomVault
   - Physical Android phone/tablet

2. **Steps**:
   - Connect USB drive to phone via OTG adapter
   - Phone may show "USB device detected" notification
   - Open PhantomVault app
   - Drive should appear in picker dropdown
   - Select drive and enter password
   - Tap "Unlock Vault"

3. **Troubleshooting**:
   - If drive not detected: Check OTG adapter compatibility
   - If permission denied: Grant storage permissions in Settings
   - If format not supported: USB must be FAT32, exFAT, or NTFS

### iOS USB Testing:

1. **Hardware**:
   - USB-C drive (iPad Pro, iPhone 15+) or Lightning drive (older devices)
   - Physical iPhone/iPad (iOS 13+)

2. **Steps**:
   - Connect USB drive to iPhone/iPad
   - Open PhantomVault app
   - Tap "Browse" button
   - Files app opens → Select USB drive
   - Navigate to `.phantom` folder
   - App detects vault automatically
   - Enter password or use Face ID/Touch ID
   - Tap "Unlock Vault"

3. **Troubleshooting**:
   - If Files app doesn't show USB: Disconnect and reconnect drive
   - If access denied: Grant Files access in Settings → Privacy
   - If bookmark stale: Re-select drive via Browse button

## Debugging

### Enable Logging:

```csharp
// In MauiProgram.cs
#if DEBUG
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Debug);
#endif
```

### View Logs:

**Android**:
```bash
# Via ADB
adb logcat | grep "PhantomVault"

# Or in Visual Studio:
# View → Output → Show output from: Android
```

**iOS**:
```bash
# Via Xcode
# Window → Devices and Simulators → Select device → View Device Logs

# Or in Visual Studio:
# View → Output → Show output from: iOS Device Log
```

### Common Debug Points:

```csharp
// VaultUnlockViewModel.cs
private void OnUsbDriveInserted(string drivePath)
{
    System.Diagnostics.Debug.WriteLine($"USB Inserted: {drivePath}");
    // Breakpoint here
}

// AndroidUsbDetector.cs
public IEnumerable<string> GetRemovableDrives()
{
    var drives = new List<string>();
    // Breakpoint here to inspect volumes
}
```

## Performance Testing

### Metrics to Monitor:

- **USB Detection Time**: <2 seconds (should be instant on desktop)
- **Manifest Decryption**: ~50-100ms (Argon2id key derivation)
- **Vault Load Time**: <500ms for typical vault (depends on credential count)
- **Memory Usage**: <100MB during unlock, <50MB idle

### Profiling Tools:

**Android**:
- Android Studio Profiler (CPU, Memory, Network)
- Visual Studio Diagnostic Tools

**iOS**:
- Xcode Instruments (Time Profiler, Allocations)
- Visual Studio Memory Profiler

## Next Steps

### Phase 1: Core Functionality
- [ ] Build and run app on Android emulator
- [ ] Build and run app on iOS simulator (macOS)
- [ ] Deploy to physical Android device
- [ ] Deploy to physical iPhone/iPad (macOS)
- [ ] Test USB detection on real hardware

### Phase 2: USB Integration
- [ ] Create test vault on USB drive
- [ ] Test USB OTG on Android device
- [ ] Test USB-C/Lightning on iOS device
- [ ] Verify device binding works correctly
- [ ] Test vault unlock flow end-to-end

### Phase 3: Features
- [ ] Implement biometric authentication (Face ID, Touch ID, fingerprint)
- [ ] Add credential browsing UI
- [ ] Implement autofill service (Android)
- [ ] Implement credential provider (iOS)
- [ ] Add TOTP code generation

### Phase 4: Polish
- [ ] Add app icons and splash screens
- [ ] Localization (multiple languages)
- [ ] Accessibility testing
- [ ] Performance optimization
- [ ] Battery usage testing

## Resources

- [.NET MAUI Documentation](https://learn.microsoft.com/en-us/dotnet/maui/)
- [Android USB Host Guide](https://developer.android.com/guide/topics/connectivity/usb/host)
- [iOS File Provider](https://developer.apple.com/documentation/fileprovider)
- [PhantomVault Mobile Implementation](./MOBILE_USB_IMPLEMENTATION.md)
- [Phase 2 Roadmap](./ROADMAP_PHASE2.md)

## Getting Help

If you encounter issues:

1. **Check logs** - Enable debug logging and inspect output
2. **Verify permissions** - Ensure app has storage/USB access
3. **Test on emulator first** - Rule out hardware issues
4. **Compare with desktop** - Does vault work on desktop app?
5. **Check USB format** - Must be FAT32, exFAT, or NTFS

## Known Limitations

- **Android Emulator**: No USB passthrough support - use physical device
- **iOS Simulator**: No USB support - use physical device
- **Android 11+**: Scoped storage restrictions - requires `MANAGE_EXTERNAL_STORAGE`
- **iOS**: No automatic USB detection - user must select via Files app
- **All Platforms**: USB eject while vault open - implement safe eject warning

---

**Ready to build?**

```bash
cd "G:\Users\Giblex\Build Projects\PhantomObscuraV6\src\UI.Mobile"
dotnet build
```

Happy coding! 🚀
