using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia;
using ReactiveUI;
using PhantomVault.Core;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.Security;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.ViewModels
{

    public sealed class MainViewModel : ReactiveObject
    {
        private readonly UsbDetector _usbDetector;
        private readonly VaultService _vaultService;
        private readonly ManifestService _manifestService;
        private readonly EncryptionService _encryptionService;
        private readonly IdleLockService _idleLockService;
        private readonly YubiKeyService _yubiKeyService;
        private readonly UsbBindingService _usbBindingService;
        private readonly TotpService _totpService;
        private readonly AuditService _auditService;
        private readonly IntrusionService _intrusionService;
        private readonly PhantomVault.Core.Services.ZeroKnowledge.IZkVaultService _zkVaultService;
        private readonly IHybridEncryptionService _hybridEncryptionService;
        private readonly IDeviceFingerprintProvider? _deviceFingerprintProvider;
        private readonly IDefenceEngine? _defenceEngine;
        private readonly BlackSecureRawVolumeService _blackSecureRawVolumeService;
        private readonly UsbArtifactProtectionService _usbArtifactProtectionService;
        private readonly PhantomKeyBridgeValidator _phantomKeyBridgeValidator;

        private readonly ObservableCollection<string> _removableDrives = new();
        private string? _selectedDrive;
        private bool _isBusy;
        private string _status = string.Empty;
        private readonly DialogService _dialogService;
        private Window? _ownerWindow;

        public MainViewModel(
            UsbDetector usbDetector,
            VaultService vaultService,
            ManifestService manifestService,
            EncryptionService encryptionService,
            IdleLockService idleLockService,
            YubiKeyService yubiKeyService,
            UsbBindingService usbBindingService,
            TotpService totpService,
            AuditService auditService,
            IntrusionService intrusionService,
            PhantomVault.Core.Services.ZeroKnowledge.IZkVaultService zkVaultService,
            IHybridEncryptionService hybridEncryptionService,
            IDeviceFingerprintProvider? deviceFingerprintProvider = null,
            IDefenceEngine? defenceEngine = null)
        {
            _usbDetector = usbDetector;
            _vaultService = vaultService;
            _manifestService = manifestService;
            _encryptionService = encryptionService;
            _idleLockService = idleLockService;
            _yubiKeyService = yubiKeyService;
            _usbBindingService = usbBindingService;
            _totpService = totpService;
            _auditService = auditService;
            _intrusionService = intrusionService;
            _zkVaultService = zkVaultService;
            _hybridEncryptionService = hybridEncryptionService;
            _deviceFingerprintProvider = deviceFingerprintProvider;
            _defenceEngine = defenceEngine;
            _dialogService = new DialogService();
            _blackSecureRawVolumeService = new BlackSecureRawVolumeService();
            _usbArtifactProtectionService = new UsbArtifactProtectionService(_encryptionService);
            _phantomKeyBridgeValidator = new PhantomKeyBridgeValidator(_usbArtifactProtectionService);

            RefreshDriveSelections();

            _usbDetector.RemovableDriveInserted += _ => RefreshDriveSelections();
            _usbDetector.RemovableDriveRemoved += _ => RefreshDriveSelections();

            UnlockCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                if (string.IsNullOrEmpty(SelectedDrive))
                {
                    await _dialogService.ShowWarningAsync(
                        "USB Drive Required",
                        "Please select a USB drive to unlock your vault.",
                        _ownerWindow);
                    Status = "Please select a USB drive.";
                    return;
                }

                string? manifestPath = null;
                string? extractedVolumeRoot = null;
                string? selectedDriveRoot = _blackSecureRawVolumeService.IsRawSelection(SelectedDrive) ? null : SelectedDrive;
                string? selectedPhysicalDrivePath = _blackSecureRawVolumeService.IsRawSelection(SelectedDrive)
                    ? _blackSecureRawVolumeService.TryResolvePhysicalDevicePathFromSelection(SelectedDrive)
                    : null;

                string? masterVolumePath = ResolveMasterVolumePath(selectedDriveRoot);
                if (!string.IsNullOrWhiteSpace(masterVolumePath))
                {
                    extractedVolumeRoot = Path.Combine(Path.GetTempPath(), "PhantomObscuraSessions", Guid.NewGuid().ToString("N"));
                    var volumeService = new ObscuraVolumeService();
                    await volumeService.ExtractVolumeAsync(masterVolumePath, extractedVolumeRoot).ConfigureAwait(false);

                    var extractedRootContainer = Path.Combine(extractedVolumeRoot, "root", "root.pvault");
                    if (File.Exists(extractedRootContainer))
                        manifestPath = extractedRootContainer;
                }
                else if (!string.IsNullOrWhiteSpace(selectedPhysicalDrivePath) &&
                         await _blackSecureRawVolumeService.IsBlackSecureVolumeAsync(selectedPhysicalDrivePath).ConfigureAwait(false))
                {
                    extractedVolumeRoot = Path.Combine(Path.GetTempPath(), "PhantomObscuraSessions", Guid.NewGuid().ToString("N"));
                    await _blackSecureRawVolumeService.ExtractVolumeAsync(selectedPhysicalDrivePath, extractedVolumeRoot).ConfigureAwait(false);

                    var extractedRootContainer = Path.Combine(extractedVolumeRoot, "root", "root.pvault");
                    if (File.Exists(extractedRootContainer))
                        manifestPath = extractedRootContainer;
                }

                var rootDir = string.IsNullOrWhiteSpace(selectedDriveRoot) ? null : Path.Combine(selectedDriveRoot, ".phantom", "root");
                if (manifestPath == null && !string.IsNullOrWhiteSpace(rootDir) && Directory.Exists(rootDir))
                {
                    var rootContainers = Directory.GetFiles(rootDir, "*.pvault");
                    if (rootContainers.Length > 0)
                        manifestPath = rootContainers[0];
                }

                var vaultsDir = string.IsNullOrWhiteSpace(selectedDriveRoot) ? null : Path.Combine(selectedDriveRoot, ".phantom", "vaults");
                if (manifestPath == null && !string.IsNullOrWhiteSpace(vaultsDir) && Directory.Exists(vaultsDir))
                {
                    var pvaultFiles = Directory.GetFiles(vaultsDir, "*.pvault");
                    if (pvaultFiles.Length > 0)
                        manifestPath = pvaultFiles[0];
                }
                if (manifestPath == null)
                {
                    var legacyPath = string.IsNullOrWhiteSpace(selectedDriveRoot) ? null : Path.Combine(selectedDriveRoot, "vault.manifest");
                    if (!string.IsNullOrWhiteSpace(legacyPath) && File.Exists(legacyPath))
                        manifestPath = legacyPath;
                }
                if (manifestPath == null)
                {
                    var manifestsDir = string.IsNullOrWhiteSpace(selectedDriveRoot) ? null : Path.Combine(selectedDriveRoot, ".phantom", "manifests");
                    if (!string.IsNullOrWhiteSpace(manifestsDir) && Directory.Exists(manifestsDir))
                    {
                        var legacyFiles = Directory.GetFiles(manifestsDir, "*.manifest");
                        if (legacyFiles.Length > 0)
                            manifestPath = legacyFiles[0];
                    }
                }
                if (manifestPath == null)
                {
                    await _dialogService.ShowErrorAsync(
                        "Vault Not Found",
                        "No vault manifest found on the selected drive. Please ensure this is the correct USB drive with your vault.",
                        _ownerWindow);
                    Status = "No vault manifest found on selected drive.";
                    return;
                }

                var password = await AskForPasswordAsync();
                if (password == null) return;
                try
                {
                    IsBusy = true;
                    var manifest = _manifestService.ReadManifest(manifestPath, password);
                    if (!string.IsNullOrWhiteSpace(masterVolumePath) && !string.IsNullOrWhiteSpace(extractedVolumeRoot))
                    {
                        ResolveRuntimePaths(manifest, extractedVolumeRoot, masterVolumePath);
                    }

                    if (manifest.PhantomKeyBridgeEnabled && !string.IsNullOrWhiteSpace(rootDir))
                    {
                        var layoutRoot = Directory.GetParent(rootDir)?.FullName;
                        if (!string.IsNullOrWhiteSpace(layoutRoot))
                        {
                            PhantomKeyBridgeValidator.ResolveRuntimePaths(manifest, layoutRoot);
                        }
                    }

                    try
                    {

                        string versionString = $"{manifest.Version}.0.0";

                        bool signatureValid = true;
                        Program.PolicyService.EnforceManifestPolicy(versionString, signatureValid);
                    }
                    catch (PolicyViolationException pvEx)
                    {
                        await _dialogService.ShowErrorAsync(
                            "Policy Violation",
                            $"Manifest policy check failed: {pvEx.Message}",
                            _ownerWindow);
                        Status = "Manifest policy violation.";
                        return;
                    }

                    if (manifest.LockedUntilUtc.HasValue && DateTimeOffset.UtcNow < manifest.LockedUntilUtc.Value)
                    {
                        await _dialogService.ShowWarningAsync(
                            "Vault Locked",
                            $"This vault is temporarily locked due to repeated failed unlock attempts.\n\nPlease try again after:\n{manifest.LockedUntilUtc.Value.ToLocalTime():G}",
                            _ownerWindow);
                        Status = $"Vault is locked due to repeated failed attempts. Try again after {manifest.LockedUntilUtc.Value.ToLocalTime():G}.";
                        return;
                    }

                    string deviceId = _usbBindingService.ComputeDeviceId(selectedPhysicalDrivePath ?? selectedDriveRoot!);
                    if (!string.IsNullOrEmpty(manifest.DeviceId) && !string.Equals(manifest.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        await _dialogService.ShowErrorAsync(
                            "USB Device Mismatch",
                            "This vault is bound to a different USB device. Please insert the original USB drive that was used to create this vault.",
                            _ownerWindow);
                        Status = "The vault is bound to a different USB device. Please insert the original device.";

                        _intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, selectedPhysicalDrivePath ?? selectedDriveRoot!);
                        return;
                    }

                    _phantomKeyBridgeValidator.Validate(manifest, password, null);

                    if (!string.IsNullOrEmpty(manifest.TotpSecret))
                    {
                        var totpInput = await AskForTotpAsync();
                        if (totpInput == null) return;
                        string expected = _totpService.GenerateCode(manifest.TotpSecret);

                        byte[] totpInputBytes = Encoding.UTF8.GetBytes(totpInput ?? string.Empty);
                        byte[] expectedBytes = Encoding.UTF8.GetBytes(expected ?? string.Empty);

                        if (!CryptographicOperations.FixedTimeEquals(totpInputBytes, expectedBytes))
                        {
                            await _dialogService.ShowErrorAsync(
                                "Invalid TOTP Code",
                                "The one-time code you entered is incorrect. Please verify the code from your authenticator app and try again.",
                                _ownerWindow);
                            Status = "Invalid one‑time code.";
                            _intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, SelectedDrive!);
                            return;
                        }
                    }

                    if (manifest.RequiresHardwareToken)
                    {
                        try
                        {
                            if (!_yubiKeyService.IsTokenPresent())
                            {
                                await _dialogService.ShowWarningAsync(
                                    "Hardware Token Required",
                                    "This vault requires the configured hardware-token presence check. Insert the expected device and try again.",
                                    _ownerWindow);
                                Status = "Required hardware-token presence check failed.";
                                _intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, selectedPhysicalDrivePath ?? selectedDriveRoot!);
                                return;
                            }
                        }
                        catch (NotImplementedException)
                        {
                            await _dialogService.ShowWarningAsync(
                                "Hardware Token Check Unavailable",
                                "This vault requires the configured hardware-token presence check, but this device or build cannot perform it. Use a supported Windows build to unlock this vault.",
                                _ownerWindow);
                            Status = "Hardware-token presence verification unavailable on this device.";

                            return;
                        }
                    }

                    if (!string.IsNullOrEmpty(manifest.PasskeyId))
                    {
                        try
                        {
                            var services = (Avalonia.Application.Current as App)?.Services;
                            var passkeyService = services?.GetService(typeof(IPasskeyService)) as IPasskeyService
                                ?? services?.GetService(typeof(PasskeyService)) as IPasskeyService
                                ?? new PasskeyService();

                            if (!passkeyService.IsSupported)
                            {
                                Status = "Device authenticator required but unavailable on this device";
                                await _dialogService.ShowErrorAsync(
                                    "Device Authenticator Unavailable",
                                    $"This vault requires the linked local device authenticator, but it is not available right now.\n\nReported status: {passkeyService.AuthenticatorDescription}",
                                    _ownerWindow);
                                return;
                            }

                            byte[] challenge = new byte[32];
                            System.Security.Cryptography.RandomNumberGenerator.Fill(challenge);

                            byte[] credentialId = Convert.FromBase64String(manifest.PasskeyId);

                            Status = "Waiting for device-authenticator verification...";

                            bool passkeyVerified = await passkeyService.AuthenticateAsync(
                                credentialId,
                                "PhantomVault",
                                challenge);

                            if (!passkeyVerified)
                            {
                                Status = "Device-authenticator verification failed";
                            _intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, selectedPhysicalDrivePath ?? selectedDriveRoot!);
                                await _dialogService.ShowErrorAsync(
                                    "Authentication Failed",
                                    "The local device-authenticator verification was denied or failed. Please try again.",
                                    _ownerWindow);
                                return;
                            }

                            Status = "Device authenticator verified successfully";
                        }
                        catch (PlatformNotSupportedException ex)
                        {
                            Status = "Device authenticator not supported on this platform";
                            await _dialogService.ShowErrorAsync(
                                "Device Authenticator Error",
                                $"Device-authenticator verification failed: {ex.Message}",
                                _ownerWindow);
                            return;
                        }
                        catch (InvalidOperationException ex)
                        {
                            Status = $"Device-authenticator verification error: {ex.Message}";
                            await _dialogService.ShowErrorAsync(
                                "Authentication Error",
                                ex.Message,
                                _ownerWindow);
                            return;
                        }
                        catch (Exception ex)
                        {
                            Status = $"Device-authenticator verification error: {ex.Message}";
                            await _dialogService.ShowErrorAsync(
                                "Authentication Error",
                                $"An unexpected error occurred during device-authenticator verification: {ex.Message}",
                                _ownerWindow);
                            return;
                        }
                    }

                    byte[]? hybridDek = null;
                    if (!string.IsNullOrEmpty(manifest.KemCiphertextBase64) &&
                        !string.IsNullOrEmpty(manifest.KemPrivateKeyEncryptedBase64))
                    {
                        Status = "Deriving post-quantum hybrid encryption key...";

                        try
                        {

                            var encryptedPrivateKey = PhantomVault.Core.Utils.HybridKeyDerivation.DeserializeEncryptionResult(
                                manifest.KemPrivateKeyEncryptedBase64);

                            byte[] salt = Convert.FromBase64String(manifest.SaltBase64 ?? throw new InvalidOperationException("Missing manifest salt"));

                            string combinedSecret = password ?? string.Empty;
                            if (!string.IsNullOrEmpty(manifest.KeyfilePath) && File.Exists(manifest.KeyfilePath))
                            {
                                byte[] keyfileBytes = File.ReadAllBytes(manifest.KeyfilePath);
                                combinedSecret = combinedSecret + Convert.ToBase64String(keyfileBytes);
                                PhantomVault.Core.Utils.HybridKeyDerivation.ZeroMemory(keyfileBytes);
                            }

                            byte[] kek = _encryptionService.DeriveKey(combinedSecret.AsSpan(), salt);
                            byte[] aad = System.Text.Encoding.UTF8.GetBytes("KEM-PrivateKey-Phase2");
                            byte[] kemPrivateKey;
                            try
                            {
                                kemPrivateKey = _encryptionService.Decrypt(
                                    encryptedPrivateKey.Ciphertext,
                                    encryptedPrivateKey.Nonce,
                                    encryptedPrivateKey.Tag,
                                    kek,
                                    aad);
                            }
                            finally
                            {
                                System.Security.Cryptography.CryptographicOperations.ZeroMemory(aad);
                            }

                            byte[] kemCiphertext = Convert.FromBase64String(manifest.KemCiphertextBase64);
                            byte[] kemSharedSecret = _hybridEncryptionService.DecapsulateSecret(kemCiphertext, kemPrivateKey);

                            hybridDek = PhantomVault.Core.Utils.HybridKeyDerivation.DeriveHybridKey(kek, kemSharedSecret);

                            Status = "Unlocking vault with hybrid encryption key...";
                            bool zkUnlocked = await _zkVaultService.UnlockWithHybridKeyAsync(hybridDek);

                            if (!zkUnlocked)
                            {
                                Status = "Failed to unlock vault with hybrid key";
                                await _dialogService.ShowErrorAsync(
                                    "Unlock Failed",
                                    "Failed to unlock zero-knowledge vault service with hybrid encryption key.",
                                    _ownerWindow);

                                PhantomVault.Core.Utils.HybridKeyDerivation.ZeroMemory(kek, kemPrivateKey, kemSharedSecret, hybridDek);
                                hybridDek = null;
                            }
                            else
                            {

                                PhantomVault.Core.Utils.HybridKeyDerivation.ZeroMemory(kek, kemPrivateKey, kemSharedSecret);
                                Status = "Hybrid encryption key derived and vault unlocked successfully";
                            }
                        }
                        catch (Exception ex)
                        {
                            Status = $"Failed to derive hybrid key: {ex.Message}";
                            await _dialogService.ShowErrorAsync(
                                "Encryption Error",
                                $"Failed to derive post-quantum hybrid encryption key: {ex.Message}\n\nFalling back to traditional encryption.",
                                _ownerWindow);

                            hybridDek = null;
                        }
                    }

                    if (hybridDek == null && !_zkVaultService.IsUnlocked)
                    {
                        Status = "Unlocking vault with traditional encryption...";
                        string fallbackDeviceId = _usbBindingService.ComputeDeviceId(selectedPhysicalDrivePath ?? selectedDriveRoot!);
                        bool zkUnlocked = await _zkVaultService.UnlockMasterKeyAsync(password ?? string.Empty, manifest.KeyfilePath, fallbackDeviceId);

                        if (!zkUnlocked)
                        {
                            Status = "Failed to unlock vault";
                            await _dialogService.ShowErrorAsync(
                                "Unlock Failed",
                                "Failed to unlock zero-knowledge vault service.",
                                _ownerWindow);
                            return;
                        }
                    }

                    if (_deviceFingerprintProvider != null && _defenceEngine != null)
                    {
                        Status = "Checking device fingerprint...";

                        var currentFingerprint = _deviceFingerprintProvider.GetCurrentFingerprint();
                        bool deviceTrusted = manifest.TrustedDevices.Any(d =>
                            d.MachineId.Equals(currentFingerprint.MachineId, StringComparison.OrdinalIgnoreCase));

                        if (!deviceTrusted)
                        {

                            _defenceEngine.RaiseThreat(new ThreatEvent(
                                ThreatType.NewDeviceFingerprint,
                                ThreatLevel.Warning,
                                $"Vault '{manifest.VaultName}' accessed from unrecognized device: {currentFingerprint.Hostname} ({currentFingerprint.UserName})"
                            ));

                            var trustResult = await _dialogService.ShowConfirmationAsync(
                                "New Device Detected",
                                $"This vault is being accessed from a device that hasn't been seen before:\n\n" +
                                $"Hostname: {currentFingerprint.Hostname}\n" +
                                $"User: {currentFingerprint.UserName}\n" +
                                $"OS: {currentFingerprint.OsFamily} {currentFingerprint.OsVersion}\n\n" +
                                $"Would you like to trust this device for future access?",
                                _ownerWindow);

                            if (trustResult)
                            {

                                currentFingerprint.FriendlyName = $"{currentFingerprint.Hostname} ({currentFingerprint.UserName})";
                                manifest.TrustedDevices.Add(currentFingerprint);

                                try
                                {
                                    _manifestService.WriteManifest(manifest, manifestPath, password ?? string.Empty, null);
                                    Status = "Device trusted and manifest updated";
                                }
                                catch (Exception ex)
                                {

                                    await _dialogService.ShowWarningAsync(
                                        "Trust Save Failed",
                                        $"Failed to save trusted device to manifest: {ex.Message}\n\nVault will open but this device won't be remembered.",
                                        _ownerWindow);
                                }
                            }
                            else
                            {
                                Status = "Device not trusted (vault will still open)";
                            }
                        }
                        else
                        {

                            var existingDevice = manifest.TrustedDevices.FirstOrDefault(d =>
                                d.MachineId.Equals(currentFingerprint.MachineId, StringComparison.OrdinalIgnoreCase));

                            if (existingDevice != null)
                            {
                                existingDevice.LastAccessAt = DateTimeOffset.UtcNow;

                                try
                                {
                                    _manifestService.WriteManifest(manifest, manifestPath, password ?? string.Empty, null);
                                }
                                catch
                                {

                                }
                            }

                            Status = "Device fingerprint verified (trusted)";
                        }
                    }

                    if (manifest.RekeyRequired)
                    {
                        Status = "⚠️ Vault Compromised - Rekey Required";

                        var rekeyConfirm = await _dialogService.ShowConfirmationAsync(
                            "Security Alert: Rekey Required",
                            $"This vault has been marked as COMPROMISED by the Defence Engine.\n\n" +
                            $"Security State: {manifest.SecurityState}\n\n" +
                            $"Before you can access your vault, you must rotate the master encryption key. " +
                            $"This requires providing your current password and choosing a new password.\n\n" +
                            $"Would you like to rekey the vault now?",
                            _ownerWindow);

                        if (!rekeyConfirm)
                        {
                            Status = "Vault access blocked - rekey required";
                            return;
                        }

                        var newPassword = await AskForNewPasswordAsync();
                        if (newPassword == null)
                        {
                            Status = "Rekey cancelled";
                            return;
                        }

                        Status = "Performing rekey operation...";

                        var services = (Avalonia.Application.Current as App)?.Services;
                        var rekeyService = services?.GetService(typeof(RekeyService)) as RekeyService;

                        if (rekeyService == null)
                        {
                            await _dialogService.ShowErrorAsync(
                                "Rekey Failed",
                                "Rekey service is not available. Please contact support.",
                                _ownerWindow);
                            Status = "Rekey service unavailable";
                            return;
                        }

                        bool rekeySuccess = rekeyService.RekeyVault(
                            manifestPath,
                            password ?? string.Empty,
                            newPassword,
                            manifest.KeyfilePath,
                            manifest.KeyfilePath);

                        if (!rekeySuccess)
                        {
                            await _dialogService.ShowErrorAsync(
                                "Rekey Failed",
                                "Failed to rotate vault encryption keys. Please check logs and try again.",
                                _ownerWindow);
                            Status = "⚠️ Rekey failed";
                            return;
                        }

                        Status = "✅ Rekey successful - Vault access restored";
                        await _dialogService.ShowSuccessAsync(
                            "Rekey Complete",
                            "Vault encryption keys have been successfully rotated.\n\n" +
                            "Security state reset to Normal.\n" +
                            "Please use your new password for future access.",
                            _ownerWindow);

                        password = newPassword;

                        manifest = _manifestService.ReadManifest(manifestPath, password);
                    }

                    string containerAbs = Path.IsPathRooted(manifest.ContainerPath)
                        ? manifest.ContainerPath
                        : Path.Combine(selectedDriveRoot!, manifest.ContainerPath);
                    string mountName = "Vault";
                    string mountPath = !string.IsNullOrWhiteSpace(extractedVolumeRoot)
                        ? extractedVolumeRoot
                        : await _vaultService.MountVaultAsync(containerAbs, mountName, password ?? string.Empty);

                    if (string.IsNullOrEmpty(manifest.KemCiphertextBase64) &&
                        !string.IsNullOrEmpty(manifest.KemPublicKeyBase64))
                    {
                        Status = "Loading Phase 1 post-quantum keys...";
                        byte[]? kemPrivateKey = await LoadKemPrivateKeyAsync(mountPath, password ?? string.Empty, manifest.KeyfilePath);

                        if (kemPrivateKey != null)
                        {
                            Status = "Phase 1 post-quantum keys loaded (not yet used for encryption)";
                            System.Security.Cryptography.CryptographicOperations.ZeroMemory(kemPrivateKey);
                        }
                    }

                    await _dialogService.ShowSuccessAsync(
                        "Vault Unlocked",
                        $"Your vault has been successfully unlocked and mounted at:\n{mountPath}",
                        _ownerWindow);

                    Status = $"Vault mounted at {mountPath}";

                    _intrusionService.ResetAttempts(manifest, manifestPath, password ?? string.Empty, null);

                    try
                    {
                        string auditPath = !string.IsNullOrWhiteSpace(selectedDriveRoot)
                            ? PhantomVault.Core.Services.PhantomDeviceLayout.GetAuditLogPath(selectedDriveRoot)
                            : Path.Combine(mountPath, "vault.audit");
                        _auditService.LogEvent(auditPath, "unlock", $"Vault unlocked and mounted at {mountPath}");
                    }
                    catch
                    {

                    }

                    if (hybridDek != null)
                    {
                        PhantomVault.Core.Utils.HybridKeyDerivation.ZeroMemory(hybridDek);
                    }

                    OnRequestOpenVault?.Invoke(
                        this,
                        new VaultUnlockRequestedEventArgs(
                            mountPath,
                            password ?? string.Empty,
                            manifest.KeyfilePath,
                            selectedPhysicalDrivePath ?? selectedDriveRoot!,
                            manifestPath,
                            containerAbs));
                }
                catch (Exception ex)
                {
                    if (!string.IsNullOrWhiteSpace(extractedVolumeRoot) && Directory.Exists(extractedVolumeRoot))
                    {
                        try
                        {
                            Directory.Delete(extractedVolumeRoot, true);
                        }
                        catch
                        {

                        }
                    }
                    Status = ex.Message;

                }
                finally
                {
                    IsBusy = false;
                }
            }, this.WhenAnyValue(vm => vm.IsBusy).Select(b => !b));

            ProvisionCommand = ReactiveCommand.Create(() =>
            {

                OnRequestProvision?.Invoke();
            });
        }

        public ObservableCollection<string> RemovableDrives => _removableDrives;

        public string? SelectedDrive
        {
            get => _selectedDrive;
            set => this.RaiseAndSetIfChanged(ref _selectedDrive, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        public string Status
        {
            get => _status;
            private set => this.RaiseAndSetIfChanged(ref _status, value);
        }

        public ReactiveCommand<Unit, Unit> UnlockCommand { get; }

        public ReactiveCommand<Unit, Unit> ProvisionCommand { get; }

        public event Action? OnRequestProvision;
        public event EventHandler<VaultUnlockRequestedEventArgs>? OnRequestOpenVault;

        private async Task<string?> AskForPasswordAsync()
        {

            var dialog = new Window
            {
                Title = "Enter passphrase",
                Width = 400,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };
            var panel = new StackPanel { Margin = new Thickness(15), Spacing = 10 };

            var label = new TextBlock
            {
                Text = "Enter your vault passphrase:",
                FontWeight = Avalonia.Media.FontWeight.SemiBold
            };

            var box = new TextBox
            {
                Width = 350,
                PasswordChar = '●',
                Watermark = "Passphrase",
                Classes = { "SecureInput" }
            };

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Spacing = 10
            };

            var okButton = new Button
            {
                IsDefault = true,
                Content = new TextBlock { Text = "Unlock" },
                Width = 80
            };

            var cancelButton = new Button
            {
                IsCancel = true,
                Content = new TextBlock { Text = "Cancel" },
                Width = 80
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            panel.Children.Add(label);
            panel.Children.Add(box);
            panel.Children.Add(buttonPanel);
            dialog.Content = panel;

            string? result = null;
            okButton.Click += (_, __) => { result = box.Text; dialog.Close(); };
            cancelButton.Click += (_, __) => { dialog.Close(); };

            Window? owner = _ownerWindow;
            if (owner == null)
            {
                owner = (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            }

            if (owner != null)
            {
                await dialog.ShowDialog(owner);
            }
            else
            {
#pragma warning disable CS8625
                await dialog.ShowDialog((Window?)null);
#pragma warning restore CS8625
            }

            box.Text = string.Empty;

            return result;
        }

        private async Task<string?> AskForNewPasswordAsync()
        {
            var dialog = new Window
            {
                Title = "Enter New Password for Rekey",
                Width = 450,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var panel = new StackPanel { Margin = new Thickness(10) };

            panel.Children.Add(new TextBlock
            {
                Text = "Enter a new password to secure your vault:",
                Margin = new Thickness(0, 0, 0, 10)
            });

            var box1 = new TextBox { Width = 410, PasswordChar = '●', Watermark = "New password" };
            var box2 = new TextBox { Width = 410, PasswordChar = '●', Watermark = "Confirm password", Margin = new Thickness(0, 5, 0, 0) };

            var okButton = new Button { IsDefault = true };
            okButton.Content = new TextBlock { Text = "Rekey Vault" };
            var cancelButton = new Button { IsCancel = true };
            cancelButton.Content = new TextBlock { Text = "Cancel" };
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);

            panel.Children.Add(box1);
            panel.Children.Add(box2);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            string? result = null;
            okButton.Click += (_, __) =>
            {
                if (string.IsNullOrEmpty(box1.Text))
                {

                    return;
                }
                if (box1.Text != box2.Text)
                {

                    box2.Text = string.Empty;
                    return;
                }
                result = box1.Text;
                dialog.Close();
            };
            cancelButton.Click += (_, __) => { dialog.Close(); };

            Window? owner = _ownerWindow;
            if (owner == null)
            {
                owner = (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            }

            if (owner != null)
            {
                await dialog.ShowDialog(owner);
            }
            else
            {
#pragma warning disable CS8625
                await dialog.ShowDialog((Window?)null);
#pragma warning restore CS8625
            }

            box1.Text = string.Empty;
            box2.Text = string.Empty;

            return result;
        }

        private async Task<string?> AskForTotpAsync()
        {
            var dialog = new Window
            {
                Title = "Enter one‑time code",
                Width = 400,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var panel = new StackPanel { Margin = new Thickness(10) };
            var box = new TextBox { Width = 360 };
            var okButton = new Button { IsDefault = true };
            okButton.Content = new TextBlock { Text = "OK" };
            var cancelButton = new Button { IsCancel = true };
            cancelButton.Content = new TextBlock { Text = "Cancel" };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);
            panel.Children.Add(box);
            panel.Children.Add(buttons);
            dialog.Content = panel;
            string? result = null;
            okButton.Click += (_, __) => { result = box.Text?.Trim(); dialog.Close(); };
            cancelButton.Click += (_, __) => dialog.Close();

            Window? owner = _ownerWindow;
            if (owner == null)
            {
                owner = (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            }

            if (owner != null)
            {
                await dialog.ShowDialog(owner);
            }
            else
            {
#pragma warning disable CS8625
                await dialog.ShowDialog((Window?)null);
#pragma warning restore CS8625
            }
            return result;
        }

        public void SetOwnerWindow(Window window)
        {
            _ownerWindow = window;
        }

        private async System.Threading.Tasks.Task<byte[]?> LoadKemPrivateKeyAsync(string mountPath, string password, string? keyfilePath)
        {
            try
            {
                string kemKeyPath = Path.Combine(mountPath, "kem.key");
                if (!File.Exists(kemKeyPath))
                {

                    return null;
                }

                string payloadJson = await File.ReadAllTextAsync(kemKeyPath);
                var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
                var root = doc.RootElement;

                string saltBase64 = root.GetProperty("salt").GetString() ?? throw new FormatException("Missing salt");
                string nonceBase64 = root.GetProperty("nonce").GetString() ?? throw new FormatException("Missing nonce");
                string tagBase64 = root.GetProperty("tag").GetString() ?? throw new FormatException("Missing tag");
                string ciphertextBase64 = root.GetProperty("ciphertext").GetString() ?? throw new FormatException("Missing ciphertext");

                byte[] salt = Convert.FromBase64String(saltBase64);
                byte[] nonce = Convert.FromBase64String(nonceBase64);
                byte[] tag = Convert.FromBase64String(tagBase64);
                byte[] ciphertext = Convert.FromBase64String(ciphertextBase64);

                string combinedSecret = password;
                if (!string.IsNullOrEmpty(keyfilePath) && File.Exists(keyfilePath))
                {
                    byte[] keyfileBytes = await File.ReadAllBytesAsync(keyfilePath);
                    combinedSecret = combinedSecret + Convert.ToBase64String(keyfileBytes);
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(keyfileBytes);
                }

                byte[] masterKey = _encryptionService.DeriveKey(combinedSecret.AsSpan(), salt);

                try
                {

                    byte[] aad = System.Text.Encoding.UTF8.GetBytes("ML-KEM-768-PRIVATE-KEY");
                    byte[] privateKey = _encryptionService.Decrypt(ciphertext, nonce, tag, masterKey, aad);

                    if (privateKey.Length != 2400)
                    {
                        throw new InvalidOperationException($"Invalid KEM private key size: {privateKey.Length} bytes. Expected 2400 bytes.");
                    }

                    return privateKey;
                }
                finally
                {

                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(masterKey);
                }
            }
            catch (Exception ex)
            {
                Status = $"Warning: Could not load post-quantum encryption key: {ex.Message}";

                return null;
            }
        }

        private static string? ResolveMasterVolumePath(string? driveRoot)
        {
            if (string.IsNullOrWhiteSpace(driveRoot))
                return null;

            var candidates = new[]
            {
                PhantomDeviceLayout.GetSystemVolumePath(driveRoot),
                Path.Combine(driveRoot, "system.bin"),
                Path.Combine(driveRoot, ".phantom", "obscura.vol")
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private void RefreshDriveSelections()
        {
            _removableDrives.Clear();
            foreach (var drive in _usbDetector.GetRemovableDrives().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                _removableDrives.Add(drive);
            }

            foreach (var rawSelection in _blackSecureRawVolumeService.GetSelectableRawDevices())
            {
                if (!_removableDrives.Contains(rawSelection))
                    _removableDrives.Add(rawSelection);
            }
        }

        private static void ResolveRuntimePaths(VaultManifest manifest, string extractedRoot, string masterVolumePath)
        {
            manifest.MasterVolumePath = masterVolumePath;
            manifest.RootContainerPath = ResolveExtractedPath(extractedRoot, manifest.RootContainerPath);
            manifest.ContainerPath = ResolveExtractedPath(extractedRoot, manifest.ContainerPath) ?? manifest.ContainerPath;
            manifest.ObjectContainerPath = ResolveExtractedPath(extractedRoot, manifest.ObjectContainerPath);
            manifest.RecoveryContainerPath = ResolveExtractedPath(extractedRoot, manifest.RecoveryContainerPath);
            manifest.BindingRecordPath = ResolveExtractedPath(extractedRoot, manifest.BindingRecordPath);
            manifest.RecoveryRecordPath = ResolveExtractedPath(extractedRoot, manifest.RecoveryRecordPath);
            manifest.DecoyDatabasePath = ResolveExtractedPath(extractedRoot, manifest.DecoyDatabasePath);
            PhantomKeyBridgeValidator.ResolveRuntimePaths(manifest, extractedRoot);
        }

        private static string? ResolveExtractedPath(string extractedRoot, string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return relativePath;

            return Path.IsPathRooted(relativePath)
                ? relativePath
                : Path.Combine(extractedRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }
    }

    public sealed class VaultUnlockRequestedEventArgs : EventArgs
    {
        public VaultUnlockRequestedEventArgs(
            string mountPath,
            string password,
            string? keyfilePath,
            string usbRootPath,
            string manifestPath,
            string containerAbsPath)
        {
            MountPath = mountPath ?? throw new ArgumentNullException(nameof(mountPath));
            Password = password ?? string.Empty;
            KeyfilePath = keyfilePath;
            UsbRootPath = usbRootPath ?? throw new ArgumentNullException(nameof(usbRootPath));
            ManifestPath = manifestPath ?? throw new ArgumentNullException(nameof(manifestPath));
            ContainerAbsPath = containerAbsPath ?? throw new ArgumentNullException(nameof(containerAbsPath));
        }

        public string MountPath { get; }
        public string Password { get; }
        public string? KeyfilePath { get; }
        public string UsbRootPath { get; }
        public string ManifestPath { get; }
        public string ContainerAbsPath { get; }
    }
}

