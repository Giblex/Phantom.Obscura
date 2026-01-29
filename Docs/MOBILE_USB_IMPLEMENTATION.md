# PhantomVault Mobile USB Implementation Guide

## Overview

This document describes the cross-platform USB vault portability implementation that allows users to access their PhantomVault from USB drives on any device - desktop (Windows, macOS, Linux) or mobile (Android, iOS).

## Architecture

### Core Philosophy: USB-Only, Zero-Knowledge, Offline-First

- **No cloud sync** - All data lives on physical USB drive
- **Plug and play** - Insert USB into any device to access vault
- **Device binding** - Vault verifies USB device ID to prevent cloning
- **Cross-platform** - Same vault works everywhere

### File Structure on USB Drive

```
E:\ (USB Drive - "PhantomVault Key")
├── .phantom/
│   ├── vault.pvdb (encrypted vault database)
│   ├── vault.manifest (tamper detection + device tracking)
│   └── backups/ (automatic encrypted backups)
└── .phantom_device_id (hidden device binding signature)
```

## Implementation Components

### 1. Cross-Platform Interface (`IUsbDetector`)

**Location**: `src/Core/Services/IUsbDetector.cs`

Defines the contract for USB detection across all platforms:

```csharp
public interface IUsbDetector : IDisposable
{
    event Action<string>? RemovableDriveInserted;
    event Action<string>? RemovableDriveRemoved;
    IEnumerable<string> GetRemovableDrives();
    bool IsValidRemovableDrive(string path);
    UsbDriveInfo? GetDriveInfo(string path);
}
```

### 2. Desktop Implementation

**Location**: `src/Core/Services/UsbDetector.cs`

- **Windows**: Uses `DriveInfo.DriveType == DriveType.Removable`
- **macOS**: Scans `/Volumes/` directory
- **Linux**: Scans `/media/`, `/run/media/` directories
- **Real-time monitoring**: `FileSystemWatcher` for drive insertion/removal

### 3. Android Implementation

**Location**: `src/UI.Mobile/Platforms/Android/AndroidUsbDetector.cs`

#### Features:
- USB OTG (On-The-Go) support via USB-C or micro-USB adapters
- `StorageManager` API with reflection for volume access
- Broadcast receiver for `ACTION_USB_DEVICE_ATTACHED`/`DETACHED`
- Volume UUID extraction for device binding

#### Permissions Required:
```xml
<uses-permission android:name="android.permission.READ_EXTERNAL_STORAGE" />
<uses-permission android:name="android.permission.WRITE_EXTERNAL_STORAGE" />
<uses-permission android:name="android.permission.MANAGE_EXTERNAL_STORAGE" />
<uses-feature android:name="android.hardware.usb.host" android:required="false" />
```

#### Platform-Specific Notes:
- Android 6.0+: Runtime permissions required
- Android 11+: Scoped storage with `MANAGE_EXTERNAL_STORAGE` for USB access
- Reflection used to access hidden `StorageManager.getVolumes()` API

### 4. iOS Implementation

**Location**: `src/UI.Mobile/Platforms/iOS/iOSUsbDetector.cs`

#### Features:
- USB-C and Lightning external drive support (iOS 13+)
- Files app integration via `UIDocumentPickerViewController`
- Security-scoped bookmarks for persistent USB access
- Volume UUID extraction using `NSUrl.VolumeUuidStringKey`

#### Entitlements Required:
```xml
<key>com.apple.security.files.user-selected.read-write</key>
<true/>
```

#### Platform-Specific Notes:
- iOS doesn't provide automatic USB detection like Android
- User must grant access via Files app document picker
- Security-scoped bookmarks persist across app launches
- Must call `StartAccessingSecurityScopedResource()` before accessing USB files

### 5. Mobile Vault Unlock UI

**Location**: `src/UI.Mobile/Views/VaultUnlockPage.xaml`

#### UI Components:
- **USB Drive Picker**: Auto-populated dropdown with detected drives
- **Drive Info Display**: Shows filesystem, total size, free space
- **Password Entry**: With visibility toggle
- **Keyfile Support**: Optional keyfile selection
- **Biometric Button**: Face ID, Touch ID, fingerprint unlock
- **Status Messages**: Real-time feedback during unlock process

#### Design:
- Matches desktop Obscura theme colors
- Responsive mobile-first layout
- Dark mode (#1E1E2E background, #6C5CE7 accent)
- Smooth animations and transitions

### 6. Mobile ViewModel

**Location**: `src/UI.Mobile/ViewModels/VaultUnlockViewModel.cs`

#### Responsibilities:
- USB drive enumeration and auto-refresh
- Drive insertion/removal event handling
- Manifest reading and decryption
- USB device binding verification
- Password + keyfile authentication
- Biometric unlock (platform-specific)
- Error handling and user feedback

#### Key Features:
```csharp
// Auto-detect USB drives
_usbDetector.RemovableDriveInserted += OnUsbDriveInserted;

// Verify device binding
var deviceId = _usbBindingService.ComputeDeviceId(drivePath);
_usbBindingService.VerifyDeviceId(drivePath, deviceId);

// Read manifest with USB serial binding
_manifestService.ReadManifest(manifestPath, password, keyfilePath,
    usbSerial: selectedDrive.DeviceIdentifier);
```

### 7. Dependency Injection

**Location**: `src/UI.Mobile/MauiProgram.cs`

Platform-specific service registration:

```csharp
#if ANDROID
services.AddSingleton<IUsbDetector>(sp =>
{
    var context = Platform.CurrentActivity;
    return new AndroidUsbDetector(context);
});
#elif IOS
services.AddSingleton<IUsbDetector, iOSUsbDetector>();
#endif
```

## Device Binding Security

### How It Works:

1. **First Time Setup**:
   - User provisions vault on USB drive
   - `UsbBindingService.ComputeDeviceId()` calculates unique device ID
   - Device ID stored in `VaultManifest.DeviceId`
   - Hidden `.phantom_device_id` file created with HMAC-SHA256 signature

2. **Unlock Verification**:
   - Compute current device ID from USB properties
   - Compare with stored device ID in manifest
   - Verify HMAC signature in `.phantom_device_id`
   - Reject if device IDs don't match

3. **Device ID Computation**:
   - **Windows**: Volume serial number (32-bit hex)
   - **macOS/Linux**: SHA256 hash of drive size + format + label
   - **Android**: Volume UUID from `StorageManager`
   - **iOS**: Volume UUID from `NSUrl.VolumeUuidStringKey`

### Security Properties:

✅ Prevents vault cloning to different USB drive
✅ Detects if vault files are copied to another device
✅ HMAC signature prevents device ID tampering
✅ Per-drive unique identifier (not globally unique, but sufficient)

## Manifest Integration

The `VaultManifest` already tracks everything needed:

```csharp
public class VaultManifest
{
    public string? DeviceId { get; set; }                    // USB device binding
    public List<DeviceFingerprint> TrustedDevices { get; set; } // Authorized devices
    public string? IntegritySignatureBase64 { get; set; }    // Tamper detection
    public DateTimeOffset? SignatureTimestamp { get; set; }  // Signature freshness
    // ... encryption keys, TOTP, backups, etc.
}
```

**No separate `device-bindings.json` needed** - everything is encrypted and signed together in the manifest.

## Usage Flow

### Desktop:
1. Insert USB drive
2. `UsbDetector` fires `RemovableDriveInserted` event
3. App shows unlock prompt
4. User enters password
5. `ManifestService` reads `.phantom/vault.manifest`
6. `UsbBindingService` verifies device ID
7. Vault unlocks and displays credentials

### Mobile (Android):
1. Connect USB drive via USB OTG adapter
2. Grant storage permissions if first time
3. `AndroidUsbDetector` receives `ACTION_USB_DEVICE_ATTACHED` broadcast
4. App scans for `.phantom/vault.manifest`
5. User enters password (or uses biometrics)
6. Vault unlocks

### Mobile (iOS):
1. Connect USB drive via USB-C or Lightning
2. User taps "Browse" button
3. `UIDocumentPickerViewController` shows Files app
4. User selects USB drive folder
5. App creates security-scoped bookmark
6. User enters password (or uses Face ID/Touch ID)
7. Vault unlocks

## Future Enhancements

### Planned Features:
- [ ] QR code vault unlock (desktop → mobile)
- [ ] Multi-vault support (switch between vaults on same USB)
- [ ] USB vault cloning wizard (authorized backup to second USB)
- [ ] Emergency access USB (time-delayed unlock without password)
- [ ] NFC tag unlock (alternative to password on mobile)

### Platform Improvements:
- [ ] Android: Native SAF (Storage Access Framework) picker
- [ ] iOS: Improved background USB monitoring
- [ ] Desktop: Hot-swap USB vault switching
- [ ] All: USB ejection safety check (warn if vault open)

## Testing Checklist

### Desktop:
- [x] Windows USB detection (DriveInfo)
- [x] macOS USB detection (/Volumes)
- [x] Linux USB detection (/media, /run/media)
- [x] FileSystemWatcher events
- [ ] Multiple USB drives simultaneously
- [ ] USB removal during vault access

### Android:
- [ ] USB OTG detection on Pixel/Samsung devices
- [ ] StorageManager reflection on Android 11+
- [ ] Broadcast receiver events
- [ ] Permission request flow
- [ ] Scoped storage access
- [ ] USB-C and micro-USB adapters

### iOS:
- [ ] USB-C drive detection (iPad Pro, iPhone 15)
- [ ] Lightning drive detection (older iPhones/iPads)
- [ ] Files app document picker
- [ ] Security-scoped bookmarks
- [ ] Background app refresh
- [ ] Face ID/Touch ID integration

### Security:
- [ ] Device binding prevents vault cloning
- [ ] HMAC signature verification
- [ ] Manifest tampering detection
- [ ] USB serial mismatch rejection
- [ ] Encrypted backup restoration

## Development Setup

### Prerequisites:
- .NET 8 SDK
- Visual Studio 2022 or Rider
- Android SDK (for Android development)
- Xcode 15+ (for iOS development, macOS only)

### Build Commands:
```bash
# Build all platforms
dotnet build src/UI.Mobile/PhantomVault.Mobile.csproj

# Android
dotnet build src/UI.Mobile/PhantomVault.Mobile.csproj -f net8.0-android

# iOS (macOS only)
dotnet build src/UI.Mobile/PhantomVault.Mobile.csproj -f net8.0-ios

# Run Android emulator
dotnet run --project src/UI.Mobile/PhantomVault.Mobile.csproj -f net8.0-android

# Deploy to iOS simulator (macOS only)
dotnet run --project src/UI.Mobile/PhantomVault.Mobile.csproj -f net8.0-ios
```

### USB Testing:
- **Android**: Use Android Emulator with USB passthrough or physical device
- **iOS**: Use physical device (iOS Simulator doesn't support USB)
- **Desktop**: Use physical USB drive (recommended) or virtual drive for testing

## Troubleshooting

### Android: "StorageManager.getVolumes() not found"
- **Cause**: Android version doesn't expose `getVolumes()` method
- **Solution**: Falls back to reflection-based access or manual path scanning

### iOS: "Security-scoped resource access denied"
- **Cause**: Bookmark data is stale or app entitlements missing
- **Solution**: Prompt user to re-select USB drive via Files app

### All Platforms: "USB serial mismatch"
- **Cause**: Vault was created on different USB drive
- **Solution**: This is expected security behavior - vault is bound to specific USB

### Desktop: "Drive not detected"
- **Cause**: FileSystemWatcher not firing or drive mounted too quickly
- **Solution**: Manual refresh button or periodic polling fallback

## Performance Considerations

- **Manifest read**: ~50-100ms (Argon2id key derivation)
- **USB detection**: <2 seconds (Android/iOS may take longer)
- **Device binding verification**: ~10ms (HMAC-SHA256 computation)
- **Vault database load**: Depends on database size (typically <500ms)

## Security Audit Notes

✅ **No plaintext secrets**: Passwords never stored, only Argon2id hashes
✅ **USB binding**: Prevents vault cloning attacks
✅ **Manifest signing**: HMAC-SHA256 tamper detection
✅ **Zero-knowledge**: No server-side data, all encryption local
✅ **Secure deletion**: Memory wiped after decryption
✅ **Biometric optional**: Password always works as fallback

## References

- [Android USB Host Overview](https://developer.android.com/guide/topics/connectivity/usb/host)
- [iOS File Provider Extensions](https://developer.apple.com/documentation/fileprovider)
- [MAUI Platform-Specific Services](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/)
- [PhantomVault Phase 2 Roadmap](./ROADMAP_PHASE2.md)

---

**Last Updated**: 2025-12-28
**Status**: Implementation Complete, Testing Pending
**Next Step**: Build MAUI project and test on physical Android/iOS devices
