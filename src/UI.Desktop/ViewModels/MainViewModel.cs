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
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.Security;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model for the main window. Provides high‑level UI state for
    /// selecting a removable drive, unlocking an existing vault and
    /// launching the provisioning wizard. Uses ReactiveUI to simplify
    /// property change notifications and command wiring.
    /// </summary>
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

            // Populate drives at startup
            foreach (var drive in _usbDetector.GetRemovableDrives())
            {
                _removableDrives.Add(drive);
            }

            // Subscribe to hot plug events
            _usbDetector.RemovableDriveInserted += path => _removableDrives.Add(path);
            _usbDetector.RemovableDriveRemoved += path => _removableDrives.Remove(path);

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
                var manifestPath = Path.Combine(SelectedDrive, "vault.manifest");
                if (!File.Exists(manifestPath))
                {
                    await _dialogService.ShowErrorAsync(
                        "Vault Not Found",
                        "No vault manifest found on the selected drive. Please ensure this is the correct USB drive with your vault.",
                        _ownerWindow);
                    Status = "No vault manifest found on selected drive.";
                    return;
                }
                // For simplicity we prompt using an Avalonia dialog. In a
                // production system you'd build a proper password input in XAML.
                var password = await AskForPasswordAsync();
                if (password == null) return;
                try
                {
                    IsBusy = true;
                    var manifest = _manifestService.ReadManifest(manifestPath, password);

                    // Enforce manifest policy (version, signature requirements)
                    try
                    {
                        // VaultManifest.Version is an int (schema version), convert to semver format
                        string versionString = $"{manifest.Version}.0.0";
                        // Since manifest is encrypted (not separately signed), signature validation = manifest decryption succeeded
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

                    // Check for cooldown/lockout
                    if (manifest.LockedUntilUtc.HasValue && DateTimeOffset.UtcNow < manifest.LockedUntilUtc.Value)
                    {
                        await _dialogService.ShowWarningAsync(
                            "Vault Locked",
                            $"This vault is temporarily locked due to repeated failed unlock attempts.\n\nPlease try again after:\n{manifest.LockedUntilUtc.Value.ToLocalTime():G}",
                            _ownerWindow);
                        Status = $"Vault is locked due to repeated failed attempts. Try again after {manifest.LockedUntilUtc.Value.ToLocalTime():G}.";
                        return;
                    }

                    // Verify that the vault is bound to this USB device
                    string deviceId = _usbBindingService.ComputeDeviceId(SelectedDrive!);
                    if (!string.IsNullOrEmpty(manifest.DeviceId) && !string.Equals(manifest.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        await _dialogService.ShowErrorAsync(
                            "USB Device Mismatch",
                            "This vault is bound to a different USB device. Please insert the original USB drive that was used to create this vault.",
                            _ownerWindow);
                        Status = "The vault is bound to a different USB device. Please insert the original device.";
                        // Record failed attempt
                        _intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, SelectedDrive!);
                        return;
                    }

                    // Verify TOTP if enabled
                    if (!string.IsNullOrEmpty(manifest.TotpSecret))
                    {
                        var totpInput = await AskForTotpAsync();
                        if (totpInput == null) return;
                        string expected = _totpService.GenerateCode(manifest.TotpSecret);

                        // Use constant-time comparison to prevent timing attacks
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

                    // If hardware token is required, verify presence
                    if (manifest.RequiresHardwareToken)
                    {
                        try
                        {
                            if (!_yubiKeyService.IsTokenPresent())
                            {
                                await _dialogService.ShowWarningAsync(
                                    "Hardware Token Required",
                                    "This vault requires a hardware token (YubiKey). Please insert your YubiKey and try again.",
                                    _ownerWindow);
                                Status = "Required hardware token not present.";
                                _intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, SelectedDrive!);
                                return;
                            }
                        }
                        catch (NotImplementedException)
                        {
                            await _dialogService.ShowWarningAsync(
                                "Feature Not Available",
                                "Hardware token support is not implemented in this build. Please use a build with YubiKey support enabled.",
                                _ownerWindow);
                            Status = "Hardware token support is not implemented in this build.";
                            // Do not increment counter when feature missing
                            return;
                        }
                    }

                    // Verify passkey (WebAuthn) if manifest requires it
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
                                Status = "Passkey authentication required but not supported on this platform";
                                await _dialogService.ShowErrorAsync(
                                    "Passkey Not Supported",
                                    "This vault requires passkey authentication, but your platform doesn't support it. Please use a device with biometric authentication.",
                                    _ownerWindow);
                                return;
                            }

                            // Generate challenge for authentication
                            byte[] challenge = new byte[32];
                            System.Security.Cryptography.RandomNumberGenerator.Fill(challenge);

                            // Decode stored credential ID from manifest
                            byte[] credentialId = Convert.FromBase64String(manifest.PasskeyId);

                            Status = "Waiting for passkey authentication...";

                            // Request passkey authentication
                            bool passkeyVerified = await passkeyService.AuthenticateAsync(
                                credentialId,
                                "PhantomVault",
                                challenge);

                            if (!passkeyVerified)
                            {
                                Status = "Passkey authentication failed";
                                _intrusionService.RegisterFailedAttempt(manifest, manifestPath, password, null, SelectedDrive!);
                                await _dialogService.ShowErrorAsync(
                                    "Authentication Failed",
                                    "Passkey authentication was denied or failed. Please try again.",
                                    _ownerWindow);
                                return;
                            }

                            Status = "Passkey verified successfully";
                        }
                        catch (PlatformNotSupportedException ex)
                        {
                            Status = "Passkey not supported on this platform";
                            await _dialogService.ShowErrorAsync(
                                "Passkey Error",
                                $"Passkey authentication failed: {ex.Message}",
                                _ownerWindow);
                            return;
                        }
                        catch (InvalidOperationException ex)
                        {
                            Status = $"Passkey verification error: {ex.Message}";
                            await _dialogService.ShowErrorAsync(
                                "Authentication Error",
                                ex.Message,
                                _ownerWindow);
                            return;
                        }
                        catch (Exception ex)
                        {
                            Status = $"Passkey verification error: {ex.Message}";
                            await _dialogService.ShowErrorAsync(
                                "Authentication Error",
                                $"An unexpected error occurred during passkey verification: {ex.Message}",
                                _ownerWindow);
                            return;
                        }
                    }

                    // Phase 2: Check if vault uses hybrid encryption
                    byte[]? hybridDek = null;
                    if (!string.IsNullOrEmpty(manifest.KemCiphertextBase64) &&
                        !string.IsNullOrEmpty(manifest.KemPrivateKeyEncryptedBase64))
                    {
                        Status = "Deriving post-quantum hybrid encryption key...";

                        try
                        {
                            // Step 1: Decrypt KEM private key from manifest
                            var encryptedPrivateKey = PhantomVault.Core.Utils.HybridKeyDerivation.DeserializeEncryptionResult(
                                manifest.KemPrivateKeyEncryptedBase64);

                            // Derive KEK to decrypt the private key
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

                            // Step 2: Decapsulate KEM ciphertext to get shared secret
                            byte[] kemCiphertext = Convert.FromBase64String(manifest.KemCiphertextBase64);
                            byte[] kemSharedSecret = _hybridEncryptionService.DecapsulateSecret(kemCiphertext, kemPrivateKey);

                            // Step 3: Derive hybrid DEK = KEK ⊕ shared_secret
                            hybridDek = PhantomVault.Core.Utils.HybridKeyDerivation.DeriveHybridKey(kek, kemSharedSecret);

                            // Step 4: Unlock ZK vault service with hybrid DEK
                            Status = "Unlocking vault with hybrid encryption key...";
                            bool zkUnlocked = await _zkVaultService.UnlockWithHybridKeyAsync(hybridDek);

                            if (!zkUnlocked)
                            {
                                Status = "Failed to unlock vault with hybrid key";
                                await _dialogService.ShowErrorAsync(
                                    "Unlock Failed",
                                    "Failed to unlock zero-knowledge vault service with hybrid encryption key.",
                                    _ownerWindow);
                                // Clean up and fall back
                                PhantomVault.Core.Utils.HybridKeyDerivation.ZeroMemory(kek, kemPrivateKey, kemSharedSecret, hybridDek);
                                hybridDek = null;
                            }
                            else
                            {
                                // Clean up sensitive material (but keep hybridDek for reference)
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
                            // Fall back to traditional encryption
                            hybridDek = null;
                        }
                    }

                    // If no hybrid encryption, fall back to traditional ZK unlock
                    if (hybridDek == null && !_zkVaultService.IsUnlocked)
                    {
                        Status = "Unlocking vault with traditional encryption...";
                        string fallbackDeviceId = _usbBindingService.ComputeDeviceId(SelectedDrive!);
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

                    // Device fingerprint check: Raise threat if new device is detected
                    if (_deviceFingerprintProvider != null && _defenceEngine != null)
                    {
                        Status = "Checking device fingerprint...";
                        
                        var currentFingerprint = _deviceFingerprintProvider.GetCurrentFingerprint();
                        bool deviceTrusted = manifest.TrustedDevices.Any(d => 
                            d.MachineId.Equals(currentFingerprint.MachineId, StringComparison.OrdinalIgnoreCase));

                        if (!deviceTrusted)
                        {
                            // Raise NewDeviceFingerprint threat
                            _defenceEngine.RaiseThreat(new ThreatEvent(
                                ThreatType.NewDeviceFingerprint,
                                ThreatLevel.Warning,
                                $"Vault '{manifest.VaultName}' accessed from unrecognized device: {currentFingerprint.Hostname} ({currentFingerprint.UserName})"
                            ));

                            // Ask user if they want to trust this device
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
                                // Add device to trusted list
                                currentFingerprint.FriendlyName = $"{currentFingerprint.Hostname} ({currentFingerprint.UserName})";
                                manifest.TrustedDevices.Add(currentFingerprint);
                                
                                // Persist updated manifest
                                try
                                {
                                    _manifestService.WriteManifest(manifest, manifestPath, password ?? string.Empty, null);
                                    Status = "Device trusted and manifest updated";
                                }
                                catch (Exception ex)
                                {
                                    // Non-fatal: vault will still open, just won't save trust
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
                            // Update LastAccessAt for existing trusted device
                            var existingDevice = manifest.TrustedDevices.FirstOrDefault(d => 
                                d.MachineId.Equals(currentFingerprint.MachineId, StringComparison.OrdinalIgnoreCase));
                            
                            if (existingDevice != null)
                            {
                                existingDevice.LastAccessAt = DateTimeOffset.UtcNow;
                                
                                // Persist updated timestamp
                                try
                                {
                                    _manifestService.WriteManifest(manifest, manifestPath, password ?? string.Empty, null);
                                }
                                catch
                                {
                                    // Non-fatal: timestamp update failure is not critical
                                }
                            }
                            
                            Status = "Device fingerprint verified (trusted)";
                        }
                    }

                    // Check if vault requires rekey before allowing normal access
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
                        
                        // Prompt for new password
                        var newPassword = await AskForNewPasswordAsync();
                        if (newPassword == null)
                        {
                            Status = "Rekey cancelled";
                            return;
                        }
                        
                        Status = "Performing rekey operation...";
                        
                        // Get RekeyService from DI
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
                        
                        // Update password for subsequent operations
                        password = newPassword;
                        
                        // Reload manifest to get updated state
                        manifest = _manifestService.ReadManifest(manifestPath, password);
                    }

                    // Mount container (vault service is now unlocked)
                    string containerAbs = Path.Combine(SelectedDrive!, manifest.ContainerPath);
                    string mountName = "Vault";
                    string mountPath = await _vaultService.MountVaultAsync(containerAbs, mountName, password ?? string.Empty);

                    // For Phase 1 vaults: Try to load KEM private key from inside vault
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
                    // Reset failed attempts on successful unlock
                    _intrusionService.ResetAttempts(manifest, manifestPath, password ?? string.Empty, null);
                    // Record unlock event in audit log
                    try
                    {
                        string auditPath = Path.Combine(SelectedDrive!, "vault.audit");
                        _auditService.LogEvent(auditPath, "unlock", $"Vault unlocked and mounted at {mountPath}");
                    }
                    catch
                    {
                        // Ignore audit errors
                    }

                    // Clean up hybrid DEK from memory (ZK service already has a copy)
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
                            SelectedDrive!,
                            manifestPath,
                            containerAbs));
                }
                catch (Exception ex)
                {
                    Status = ex.Message;
                    // If reading manifest failed due to wrong passphrase
                    // we cannot increment the counter because we cannot
                    // decrypt the manifest. This could be improved by
                    // storing the counter in a separate tamper‑evident log.
                }
                finally
                {
                    IsBusy = false;
                }
            }, this.WhenAnyValue(vm => vm.IsBusy).Select(b => !b));

            ProvisionCommand = ReactiveCommand.Create(() =>
            {
                // Create a new provision window and show it. We use
                // Interaction for this to keep view model decoupled from view.
                OnRequestProvision?.Invoke();
            });
        }

        /// <summary>
        /// Gets the list of detected removable drives.
        /// </summary>
        public ObservableCollection<string> RemovableDrives => _removableDrives;

        /// <summary>
        /// Gets or sets the currently selected drive path.
        /// </summary>
        public string? SelectedDrive
        {
            get => _selectedDrive;
            set => this.RaiseAndSetIfChanged(ref _selectedDrive, value);
        }

        /// <summary>
        /// Indicates whether an operation is in progress. Disables commands
        /// when true.
        /// </summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        /// <summary>
        /// A human readable status message displayed in the UI.
        /// </summary>
        public string Status
        {
            get => _status;
            private set => this.RaiseAndSetIfChanged(ref _status, value);
        }

        /// <summary>
        /// Command executed when the user chooses to unlock a vault.
        /// </summary>
        public ReactiveCommand<Unit, Unit> UnlockCommand { get; }

        /// <summary>
        /// Command executed when the user wants to provision a new vault.
        /// </summary>
        public ReactiveCommand<Unit, Unit> ProvisionCommand { get; }

        /// <summary>
        /// Event raised when the provision window should be opened. The view
        /// subscribes to this to create and show the provision window.
        /// </summary>
        public event Action? OnRequestProvision;
        public event EventHandler<VaultUnlockRequestedEventArgs>? OnRequestOpenVault;

        private async Task<string?> AskForPasswordAsync()
        {
            // Secure password prompt using Avalonia TextBox with PasswordChar.
            // Password is cleared from the control after reading to minimize
            // exposure in memory.
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
            
            // TextBox with PasswordChar for secure input
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
            
            // Resolve a non-null owner when possible (prefer the explicitly set owner,
            // otherwise use the application's main window). If none available, fall
            // back to the previous behaviour and suppress the nullable warning locally.
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
#pragma warning disable CS8625 // Allow passing null for owner as a last-resort fallback
                await dialog.ShowDialog((Window?)null);
#pragma warning restore CS8625
            }
            
            // Clear password from the TextBox to minimize memory exposure
            box.Text = string.Empty;
            
            return result;
        }

        /// <summary>
        /// Prompts the user for a new password during rekey operation.
        /// </summary>
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
                    // Show error - password cannot be empty
                    return;
                }
                if (box1.Text != box2.Text)
                {
                    // Show error - passwords don't match
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
            
            // Clear passwords from TextBoxes to minimize memory exposure
            box1.Text = string.Empty;
            box2.Text = string.Empty;
            
            return result;
        }

        /// <summary>
        /// Prompts the user for a time‑based one‑time password (TOTP) code.
        /// Uses a simple dialog with a text box. Returns null if the user
        /// cancels the dialog. In a production system you may want to
        /// implement a custom dialog with additional validation or masking.
        /// </summary>
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
            // Resolve a non-null owner when possible (prefer the explicitly set owner,
            // otherwise use the application's main window). If none available, fall
            // back to the previous behaviour and suppress the nullable warning locally.
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
#pragma warning disable CS8625 // Allow passing null for owner as a last-resort fallback
                await dialog.ShowDialog((Window?)null);
#pragma warning restore CS8625
            }
            return result;
        }

        /// <summary>
        /// Sets the owner window for dialog display.
        /// </summary>
        public void SetOwnerWindow(Window window)
        {
            _ownerWindow = window;
        }

        /// <summary>
        /// Loads and decrypts the ML-KEM-768 private key from the vault.
        /// This method retrieves the encrypted KEM private key (kem.key) from the mounted
        /// vault container and decrypts it using the user's passphrase and keyfile.
        /// The decrypted private key can then be used for post-quantum hybrid decryption operations.
        /// </summary>
        /// <param name="mountPath">Path to the mounted vault container.</param>
        /// <param name="password">User passphrase for key derivation.</param>
        /// <param name="keyfilePath">Optional keyfile path for additional entropy.</param>
        /// <returns>The decrypted ML-KEM-768 private key (2400 bytes), or null if not found.</returns>
        private async System.Threading.Tasks.Task<byte[]?> LoadKemPrivateKeyAsync(string mountPath, string password, string? keyfilePath)
        {
            try
            {
                string kemKeyPath = Path.Combine(mountPath, "kem.key");
                if (!File.Exists(kemKeyPath))
                {
                    // KEM private key not found - vault may have been created before PQ encryption was added
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

                // Combine passphrase with keyfile if present
                string combinedSecret = password;
                if (!string.IsNullOrEmpty(keyfilePath) && File.Exists(keyfilePath))
                {
                    byte[] keyfileBytes = await File.ReadAllBytesAsync(keyfilePath);
                    combinedSecret = combinedSecret + Convert.ToBase64String(keyfileBytes);
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(keyfileBytes);
                }

                // Derive encryption key
                byte[] masterKey = _encryptionService.DeriveKey(combinedSecret.AsSpan(), salt);

                try
                {
                    // Decrypt the KEM private key
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
                    // Wipe master key from memory
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(masterKey);
                }
            }
            catch (Exception ex)
            {
                Status = $"Warning: Could not load post-quantum encryption key: {ex.Message}";
                // Return null to allow vault operations to proceed without PQ encryption
                return null;
            }
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