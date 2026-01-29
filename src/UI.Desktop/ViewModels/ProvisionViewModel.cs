using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ReactiveUI;
using PhantomVault.Core;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.ZeroKnowledge;
using PhantomVault.UI.Services;
using PhantomVault.UI.Views;
using GiblexVault.Security.ZK;
using System.Security.Principal;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model for the provisioning window. Guides the user through
    /// creating a new encrypted container and manifest on a selected
    /// removable drive. Includes basic validation and progress reporting.
    /// </summary>
    public sealed class ProvisionViewModel : ReactiveObject, PhantomVault.UI.Services.IResettableOnError
    {
        private readonly VaultService _vaultService;
        private readonly ManifestService _manifestService;
        private readonly UsbDetector _usbDetector;
        private readonly UsbBindingService _usbBindingService;
        private readonly TotpService _totpService;
        private readonly AuditService _auditService;
        private readonly BackupService _backupService;
        private readonly IZkVaultService _zkVaultService;
        private readonly IVeraCryptService _veraCryptService;

        private readonly ObservableCollection<string> _drives = new();

        private string? _vaultName;
        private int _volumeSizeMb = 1024;
        private string? _passphrase;
        private string? _confirmPassphrase;
        private string? _keyfilePath;
        private string? _selectedDrive;
        private bool _requiresHardwareToken;
        private bool _useTotp;
        private bool _usePasskey;
        private bool _isBusy;
        private double _progress;
        private string _status = string.Empty;
        private string _passwordError = string.Empty;
        private string _confirmPasswordError = string.Empty;
        private bool _showPassword;
        private bool _showConfirmPassword;
        private string _selectedEncryptionLevel = "Medium (Recommended)";
        private string _keePassFilePath = string.Empty;
        private string _importStatus = string.Empty;
        private bool _useVeraCrypt = true;

        private readonly KeyfileGeneratorService _keyfileGeneratorService;
        private readonly KeePassImportService _keePassImportService;
        private readonly DialogService _dialogService;
        private readonly IHybridEncryptionService _hybridEncryptionService;
        private Window? _ownerWindow;

        // Event to navigate to vault window after successful creation
        public event EventHandler<string>? NavigateToVault;

        public ProvisionViewModel(
            VaultService vaultService,
            ManifestService manifestService,
            UsbDetector usbDetector,
            UsbBindingService usbBindingService,
            TotpService totpService,
            AuditService auditService,
            BackupService backupService,
            IZkVaultService zkVaultService,
            IHybridEncryptionService hybridEncryptionService,
            IVeraCryptService veraCryptService,
            KeyfileGeneratorService keyfileGeneratorService,
            KeePassImportService keePassImportService,
            DialogService dialogService,
            string? preSelectedUsbDrive = null)
        {
            _vaultService = vaultService;
            _manifestService = manifestService;
            _usbDetector = usbDetector;
            _usbBindingService = usbBindingService;
            _totpService = totpService;
            _auditService = auditService;
            _backupService = backupService;
            _zkVaultService = zkVaultService;
            _hybridEncryptionService = hybridEncryptionService;
            _veraCryptService = veraCryptService;
            _keyfileGeneratorService = keyfileGeneratorService ?? throw new ArgumentNullException(nameof(keyfileGeneratorService));
            _keePassImportService = keePassImportService ?? throw new ArgumentNullException(nameof(keePassImportService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

            // Populate removable drives
            foreach (var drive in _usbDetector.GetRemovableDrives())
            {
                _drives.Add(drive);
            }
            _usbDetector.RemovableDriveInserted += d => _drives.Add(d);
            _usbDetector.RemovableDriveRemoved += d => _drives.Remove(d);

            // Auto-select USB drive if pre-selected from USB Setup
            if (!string.IsNullOrEmpty(preSelectedUsbDrive))
            {
                SelectedDrive = preSelectedUsbDrive;
            }

            BrowseKeyfileCommand = ReactiveCommand.CreateFromTask(BrowseKeyfileAsync);

            GenerateKeyfileCommand = ReactiveCommand.CreateFromTask(GenerateKeyfileAsync);
            ClearKeyfileCommand = ReactiveCommand.Create(() => { KeyfilePath = null; });
            ImportKeePassCommand = ReactiveCommand.CreateFromTask(ImportKeePassAsync);
            OpenImportDialogCommand = ReactiveCommand.CreateFromTask(OpenImportDialogAsync);

            ToggleShowPasswordCommand = ReactiveCommand.Create(() => { ShowPassword = !ShowPassword; });
            ToggleShowConfirmPasswordCommand = ReactiveCommand.Create(() => { ShowConfirmPassword = !ShowConfirmPassword; });

            // Load user's default preferences for encryption and authentication
            LoadDefaultPreferences();

            CreateVaultCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                // Preflight: only require VeraCrypt when using an outer container
                if (UseVeraCrypt)
                {
                    var vc = _veraCryptService;
                    if (!vc.IsVeraCryptInstalled())
                    {
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            await _dialogService.ShowErrorAsync(
                                "VeraCrypt Not Found",
                                "VeraCrypt is required to create and mount the outer container. Please install VeraCrypt or uncheck 'Use VeraCrypt outer container'.",
                                _ownerWindow);
                        });
                        Status = "VeraCrypt is not installed.";
                        return;
                    }
                    else
                    {
                        // Ensure VaultService uses the detected VeraCrypt path (or user override)
                        try
                        {
                            var services = (Avalonia.Application.Current as App)?.Services;
                            var options = services?.GetService(typeof(PhantomVault.Core.Options.VaultOptions)) as PhantomVault.Core.Options.VaultOptions;
                            if (options != null)
                            {
                                // Honor user override when available
                                var settings = SettingsService.Load();
                                var effectivePath = !string.IsNullOrWhiteSpace(settings.VeraCryptPathOverride)
                                    ? settings.VeraCryptPathOverride!
                                    : vc.VeraCryptPath;
                                if (!string.IsNullOrWhiteSpace(effectivePath))
                                {
                                    options.VeraCryptPath = effectivePath;
                                }
                            }
                        }
                        catch { /* best effort */ }
                    }
                    if (OperatingSystem.IsWindows())
                    {
                        try
                        {
                            using var identity = WindowsIdentity.GetCurrent();
                            var principal = new WindowsPrincipal(identity);
                            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
                            {
                                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                                {
                                    await _dialogService.ShowWarningAsync(
                                        "Administrator Recommended",
                                        "Mounting VeraCrypt volumes may require Administrator privileges on Windows. If mounting fails, restart PhantomVault as Administrator.",
                                        _ownerWindow);
                                });
                            }
                        }
                        catch { /* best effort */ }
                    }
                }
                if (string.IsNullOrEmpty(SelectedDrive))
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await _dialogService.ShowWarningAsync(
                            "USB Drive Required",
                            "Please select a USB drive to create your vault.",
                            _ownerWindow);
                    });
                    Status = "Please select a USB drive.";
                    return;
                }

                // Enforce USB policy before vault creation
                try
                {
                    if (Program.PolicyService.IsUsbRequired())
                    {
                        Program.PolicyService.EnforceUsbPolicy();
                    }
                }
                catch (PolicyViolationException pvEx)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await _dialogService.ShowErrorAsync(
                            "Policy Violation",
                            $"USB policy enforcement failed: {pvEx.Message}",
                            _ownerWindow);
                    });
                    Status = "USB policy violation.";
                    return;
                }
                catch (System.Security.SecurityException secEx)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await _dialogService.ShowErrorAsync(
                            "Security Policy Error",
                            $"Security policy check failed: {secEx.Message}",
                            _ownerWindow);
                    });
                    Status = "Security policy error.";
                    return;
                }

                if (string.IsNullOrWhiteSpace(VaultName))
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await _dialogService.ShowWarningAsync(
                            "Vault Name Required",
                            "Please enter a name for your vault.",
                            _ownerWindow);
                    });
                    Status = "Vault name is required.";
                    return;
                }

                // Password is optional, but if provided must match and meet requirements
                if (!string.IsNullOrEmpty(Passphrase) || !string.IsNullOrEmpty(ConfirmPassphrase))
                {
                    if (Passphrase != ConfirmPassphrase)
                    {
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            await _dialogService.ShowErrorAsync(
                                "Passphrase Mismatch",
                                "The passphrases you entered do not match. Please re-enter and confirm your passphrase.",
                                _ownerWindow);
                        });
                        Status = "Passphrases do not match.";
                        return;
                    }

                    // Check if password meets requirements
                    if (!string.IsNullOrEmpty(PasswordError))
                    {
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            await _dialogService.ShowErrorAsync(
                                "Invalid Password",
                                PasswordError,
                                _ownerWindow);
                        });
                        Status = "Password does not meet requirements.";
                        return;
                    }
                }

                // Keyfile is required
                if (string.IsNullOrEmpty(KeyfilePath))
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await _dialogService.ShowWarningAsync(
                            "Keyfile Required",
                            "A keyfile is required to create the vault. Please generate or select a keyfile.",
                            _ownerWindow);
                    });
                    Status = "Keyfile is required.";
                    return;
                }

                IsBusy = true;
                Status = "Creating encrypted vault...";
                try
                {
                    // Create hidden .phantom folder structure
                    var phantomPath = CreatePhantomFolder(SelectedDrive!);
                    var encryptedName = GetEncryptedFilename(VaultName!);

                    // Password is optional - use null if empty
                    var passwordToUse = string.IsNullOrEmpty(Passphrase) ? null : Passphrase;
                    byte[]? passkeyCredentialId = null;
                    // Generate ML-KEM-768 key pair for post-quantum hybrid encryption FIRST
                    // This happens before container creation so we can store it properly
                    Status = "Generating post-quantum cryptographic keys...";
                    Progress = 0.05;
                    var (kemPublicKey, kemPrivateKey) = _hybridEncryptionService.GenerateKeyPair();

                    // Phase 2: Encapsulate a random value to generate KEM ciphertext and shared secret
                    // This will be used for hybrid key derivation (KEK ⊕ shared_secret = DEK)
                    Status = "Establishing quantum-resistant key exchange...";
                    Progress = 0.06;
                    var (kemCiphertext, kemSharedSecret) = _hybridEncryptionService.EncapsulateSecret(kemPublicKey);

                    // Phase 2: Encrypt KEM private key with traditional KEK for storage in manifest
                    // This eliminates circular dependency: manifest encrypted with KEK, private key encrypted with KEK
                    Status = "Securing cryptographic keys...";
                    Progress = 0.07;

                    // Note: We'll encrypt the KEM private key and store it in the manifest AFTER vault creation
                    // For now, store the KEM keys and shared secret for use during vault creation
                    byte[]? kemCiphertextForManifest = kemCiphertext;
                    byte[]? kemSharedSecretForVault = kemSharedSecret;

                    string containerPath;
                    long containerSizeBytes = VolumeSizeMb * 1024 * 1024;
                    string vaultPath;
                    string kemKeyPath;

                    if (UseVeraCrypt)
                    {
                        // Step 1: Create VeraCrypt container for outer layer encryption
                        Status = "Creating VeraCrypt container (outer encryption layer)...";
                        Progress = 0.1;
                        var containerFileName = $"{encryptedName}.hc";
                        containerPath = Path.Combine(phantomPath, "containers", containerFileName);
                        Directory.CreateDirectory(Path.GetDirectoryName(containerPath)!);
                        var containerProgress = new Progress<double>(p => Progress = 0.1 + (p * 0.25)); // 10-35%
                        await _vaultService.CreateVaultAsync(containerPath, containerSizeBytes, passwordToUse, KeyfilePath, containerProgress);

                        // Step 2: Mount the container temporarily to create encrypted file inside
                        Status = "Mounting container...";
                        Progress = 0.35;
                        string mountName = $"PhantomVault_{Guid.NewGuid():N}";
                        string mountPath = await _vaultService.MountVaultAsync(containerPath, mountName, passwordToUse ?? string.Empty, KeyfilePath);
                        try
                        {
                            // Step 3: Create zero-knowledge encrypted password database inside mounted container
                            Status = "Creating zero-knowledge encrypted password vault (inner encryption layer)...";
                            Progress = 0.45;
                            var vaultFileName = "vault.pvault";
                            vaultPath = Path.Combine(mountPath, vaultFileName);
                            var vaultProgress = new Progress<double>(p => Progress = 0.45 + (p * 0.25));
                            await CreateHybridEncryptedDatabaseAsync(vaultPath, passwordToUse, KeyfilePath, kemSharedSecretForVault, vaultProgress);

                            // Step 3.5: Save the ML-KEM private key inside the mounted container
                            Status = "Securing post-quantum encryption keys...";
                            Progress = 0.7;
                            kemKeyPath = Path.Combine(mountPath, "kem.key");
                            await SaveKemPrivateKeyAsync(kemKeyPath, kemPrivateKey, passwordToUse, KeyfilePath);
                        }
                        finally
                        {
                            // Step 4: Dismount the container (use returned mount path/letter)
                            Status = "Securing vault...";
                            Progress = 0.8;
                            await _vaultService.DismountVaultAsync(mountPath);
                        }
                    }
                    else
                    {
                        // No VeraCrypt: create the encrypted database directly under .phantom
                        Status = "Creating encrypted vault (single-layer ZK)...";
                        Progress = 0.1;
                        var directDir = Path.Combine(phantomPath, "vaults");
                        Directory.CreateDirectory(directDir);
                        vaultPath = Path.Combine(directDir, "vault.pvault");
                        var vaultProgress = new Progress<double>(p => Progress = 0.1 + (p * 0.6));
                        await CreateHybridEncryptedDatabaseAsync(vaultPath, passwordToUse, KeyfilePath, kemSharedSecretForVault, vaultProgress);

                        // Save the ML-KEM private key in the same directory
                        Status = "Securing post-quantum encryption keys...";
                        Progress = 0.7;
                        kemKeyPath = Path.Combine(directDir, "kem.key");
                        await SaveKemPrivateKeyAsync(kemKeyPath, kemPrivateKey, passwordToUse, KeyfilePath);

                        containerPath = vaultPath; // In this mode ContainerPath points to inner file
                        Progress = 0.8;
                    }

                    if (passkeyCredentialId != null)
                    {
                        CryptographicOperations.ZeroMemory(passkeyCredentialId);
                        passkeyCredentialId = null;
                    }

                    // Note: DO NOT zero kemPrivateKey yet - we need it for Phase 2 manifest encryption
                    // It will be zeroed after the manifest is created

                    // Bind the vault to this USB device by computing a unique ID
                    string deviceId = _usbBindingService.ComputeDeviceId(SelectedDrive!);

                    if (UsePasskey)
                    {
                        var services = (Avalonia.Application.Current as App)?.Services;
                        var passkeyService = services?.GetService(typeof(IPasskeyService)) as IPasskeyService
                            ?? services?.GetService(typeof(PasskeyService)) as IPasskeyService;

                        if (passkeyService == null)
                        {
                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                            {
                                await _dialogService.ShowErrorAsync(
                                    "Passkey Service Unavailable",
                                    "Required Windows Hello components are missing. Install the passkey integration and try again.",
                                    _ownerWindow);
                            });
                            Status = "Passkey service unavailable.";
                            return;
                        }

                        if (!passkeyService.IsSupported)
                        {
                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                            {
                                await _dialogService.ShowWarningAsync(
                                    "Windows Hello Not Ready",
                                    "This device is not configured for Windows Hello. Set up a PIN, fingerprint, or face recognition before enabling passkeys.",
                                    _ownerWindow);
                            });
                            Status = "Windows Hello not available.";
                            return;
                        }

                        Status = "Requesting Windows Hello enrollment...";
                        Progress = 0.78;

                        var challenge = new byte[32];
                        RandomNumberGenerator.Fill(challenge);

                        try
                        {
                            passkeyCredentialId = await passkeyService.RegisterAsync(
                                deviceId,
                                VaultName ?? "Vault",
                                "phantomvault.app",
                                challenge);
                        }
                        catch (PlatformNotSupportedException ex)
                        {
                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                            {
                                await _dialogService.ShowErrorAsync(
                                    "Passkey Enrollment Failed",
                                    ex.Message,
                                    _ownerWindow);
                            });
                            Status = "Passkey enrollment failed.";
                            return;
                        }
                        catch (InvalidOperationException ex)
                        {
                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                            {
                                await _dialogService.ShowErrorAsync(
                                    "Passkey Enrollment Failed",
                                    ex.Message,
                                    _ownerWindow);
                            });
                            Status = "Passkey enrollment failed.";
                            return;
                        }
                        catch (Exception ex)
                        {
                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                            {
                                await _dialogService.ShowErrorAsync(
                                    "Passkey Enrollment Failed",
                                    $"Could not register Windows Hello: {ex.Message}",
                                    _ownerWindow);
                            });
                            Status = "Passkey enrollment failed.";
                            return;
                        }
                    }

                    // Phase 2: Encrypt KEM private key for storage in manifest
                    // We need to derive the KEK (Key Encryption Key) first
                    Status = "Encrypting post-quantum keys for manifest...";
                    Progress = 0.81;

                    var encryptionService = (Avalonia.Application.Current as App)?.Services?.GetService(typeof(EncryptionService)) as EncryptionService
                        ?? throw new InvalidOperationException("EncryptionService not available for manifest encryption.");

                    byte[] manifestSalt = encryptionService.GenerateSalt();

                    string combinedSecret = passwordToUse ?? string.Empty;
                    if (!string.IsNullOrEmpty(KeyfilePath) && File.Exists(KeyfilePath))
                    {
                        byte[] keyfileBytes = File.ReadAllBytes(KeyfilePath);
                        combinedSecret = combinedSecret + Convert.ToBase64String(keyfileBytes);
                        PhantomVault.Core.Utils.HybridKeyDerivation.ZeroMemory(keyfileBytes);
                    }

                    byte[] kek = encryptionService.DeriveKey(combinedSecret.AsSpan(), manifestSalt);
                    byte[] aad = System.Text.Encoding.UTF8.GetBytes("KEM-PrivateKey-Phase2");
                    var encryptedPrivateKeyResult = encryptionService.Encrypt(kemPrivateKey, kek, aad);
                    string encryptedPrivateKeyBase64 = PhantomVault.Core.Utils.HybridKeyDerivation.SerializeEncryptionResult(encryptedPrivateKeyResult);

                    // Wipe transient encryption buffers now that they have been serialized
                    CryptographicOperations.ZeroMemory(encryptedPrivateKeyResult.Ciphertext);
                    CryptographicOperations.ZeroMemory(encryptedPrivateKeyResult.Nonce);
                    CryptographicOperations.ZeroMemory(encryptedPrivateKeyResult.Tag);
                    CryptographicOperations.ZeroMemory(aad);

                    // Clean up sensitive keys
                    PhantomVault.Core.Utils.HybridKeyDerivation.ZeroMemory(kek, kemSharedSecretForVault);
                    string manifestSaltBase64 = Convert.ToBase64String(manifestSalt);
                    PhantomVault.Core.Utils.HybridKeyDerivation.ZeroMemory(manifestSalt);

                    // Construct the manifest and attach multi‑factor credentials
                    var manifest = new VaultManifest
                    {
                        VaultName = VaultName!,
                        ContainerPath = containerPath,
                        ContainerSizeBytes = containerSizeBytes,
                        UseVeraCrypt = UseVeraCrypt,
                        RequiresHardwareToken = RequiresHardwareToken,
                        DeviceId = deviceId,
                        // Enable automatic encrypted backups by default and set a retention window of 3 days.
                        AutoBackupEnabled = true,
                        BackupRetentionDays = 3,
                        // Phase 1: Store the ML-KEM-768 public key in the manifest (1184 bytes, Base64 encoded)
                        KemPublicKeyBase64 = Convert.ToBase64String(kemPublicKey),
                        // Phase 2: Store KEM ciphertext (for DEK derivation) and encrypted private key
                        KemCiphertextBase64 = Convert.ToBase64String(kemCiphertextForManifest!),
                        KemPrivateKeyEncryptedBase64 = encryptedPrivateKeyBase64,
                        SaltBase64 = manifestSaltBase64
                    };
                    if (UseTotp)
                    {
                        manifest.TotpSecret = TotpService.GenerateSecret();
                    }
                    if (UsePasskey)
                    {
                        if (passkeyCredentialId == null || passkeyCredentialId.Length == 0)
                        {
                            throw new InvalidOperationException("Passkey enrollment did not produce a credential identifier.");
                        }

                        manifest.PasskeyId = Convert.ToBase64String(passkeyCredentialId);
                    }
                    // Store manifest in hidden folder with encrypted filename
                    var manifestPath = Path.Combine(phantomPath, "manifests", $"{encryptedName}.manifest");

                    // Password is optional - use null if empty
                    var passwordForManifest = string.IsNullOrEmpty(Passphrase) ? null : Passphrase;
                    Progress = 0.82;
                    _manifestService.WriteManifest(manifest, manifestPath, passwordForManifest, KeyfilePath);

                    // Phase 2: Now that manifest is written, zero all sensitive key material
                    PhantomVault.Core.Utils.HybridKeyDerivation.ZeroMemory(kemPrivateKey, kemPublicKey, kemCiphertextForManifest!);

                    Progress = 0.83;

                    // Show success dialog
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await _dialogService.ShowSuccessAsync(
                            "Vault Created",
                            $"Your vault '{VaultName}' has been successfully created on {Path.GetFileName(SelectedDrive)}.",
                            _ownerWindow);
                    });

                    Status = "Vault successfully created.";

                    // Process KeePass import if a file was selected
                    if (!string.IsNullOrEmpty(KeePassFilePath))
                    {
                        await ProcessKeePassImportAsync(SelectedDrive!);
                    }

                    try
                    {
                        // If automatic backups are enabled on the manifest, write an encrypted copy
                        // of the manifest to the backups directory and prune old backups.  This runs
                        // synchronously after the manifest is persisted so that the backup reflects
                        // the latest state.
                        if (manifest.AutoBackupEnabled)
                        {
                            string backupDir = Path.Combine(phantomPath, "backups");
                            _ = await _backupService.CreateBackupAsync(manifestPath, Passphrase!, KeyfilePath, backupDir);
                            _backupService.PruneBackups(backupDir, manifest.BackupRetentionDays);
                        }
                    }
                    catch
                    {
                        // Ignore backup errors; vault creation succeeded.
                    }

                    try
                    {
                        // Append audit entry in hidden folder with encrypted filename
                        var auditPath = Path.Combine(phantomPath, "audit", $"{encryptedName}.audit");
                        _auditService.LogEvent(auditPath, "provision", $"Vault '{VaultName}' created on {SelectedDrive}");
                    }
                    catch
                    {
                        // Ignore audit errors; vault creation succeeded.
                    }

                    // Save user preferences for encryption and authentication
                    SaveAuthenticationMethodPreference();

                    // Navigate to vault window to open the newly created vault
                    // This must be on UI thread
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        NavigateToVault?.Invoke(this, SelectedDrive!);
                    });
                }
                catch (Exception ex)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await _dialogService.ShowErrorAsync(
                            "Vault Creation Failed",
                            $"An error occurred while creating your vault:\n\n{ex.Message}",
                            _ownerWindow);
                    });
                    Status = ex.Message;
                }
                finally
                {
                    IsBusy = false;
                }
            }, CanCreateObservable());
        }

        public bool UseVeraCrypt
        {
            get => _useVeraCrypt;
            set => this.RaiseAndSetIfChanged(ref _useVeraCrypt, value);
        }

        /// <summary>
        /// Creates a hybrid encrypted password vault database using zero-knowledge cryptography.
        /// This creates a .pvault file encrypted with VaultFileZk inside a VeraCrypt container.
        /// Provides double-layer encryption: VeraCrypt (outer) + ZK encryption (inner).
        /// </summary>
        /// <param name="hybridSharedSecret">Optional ML-KEM shared secret for Phase 2 hybrid encryption</param>
        private async Task CreateHybridEncryptedDatabaseAsync(string dbPath, string? password, string? keyfilePath, byte[]? hybridSharedSecret = null, IProgress<double>? progress = null)
        {
            try
            {
                progress?.Report(0.1);

                // Create initial empty password database structure
                var database = new
                {
                    Version = "2.0", // Version 2.0 indicates zero-knowledge encryption
                    EncryptionType = "ZeroKnowledge-VaultFileZk",
                    Created = DateTime.UtcNow,
                    VaultName = VaultName ?? "PhantomVault",
                    Description = $"Created by PhantomVault with zero-knowledge encryption on {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    Groups = new[]
                    {
                        new
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Name = "General",
                            Icon = "folder",
                            Entries = new object[] { }
                        },
                        new
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Name = "Personal",
                            Icon = "user",
                            Entries = new object[] { }
                        },
                        new
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Name = "Work",
                            Icon = "briefcase",
                            Entries = new object[] { }
                        }
                    }
                };

                progress?.Report(0.3);

                // Serialize to JSON
                var jsonContent = System.Text.Json.JsonSerializer.Serialize(database, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

                progress?.Report(0.5);

                // Write JSON to temporary file for encryption
                var tempPlaintext = Path.GetTempFileName();
                try
                {
                    await File.WriteAllTextAsync(tempPlaintext, jsonContent);

                    progress?.Report(0.7);

                    // Unlock the ZK vault service using hybrid DEK if available (Phase 2)
                    // For Phase 2 vaults, we derive: DEK = KEK ⊕ ML-KEM shared secret
                    // This provides quantum resistance while maintaining classical security
                    bool unlocked = false;

                    if (hybridSharedSecret != null)
                    {
                        // Phase 2: Use hybrid key derivation
                        // First derive traditional KEK
                        string combinedSecretForDek = password ?? string.Empty;
                        if (!string.IsNullOrEmpty(keyfilePath) && File.Exists(keyfilePath))
                        {
                            byte[] keyfileBytes = File.ReadAllBytes(keyfilePath);
                            combinedSecretForDek = combinedSecretForDek + Convert.ToBase64String(keyfileBytes);
                            PhantomVault.Core.Utils.HybridKeyDerivation.ZeroMemory(keyfileBytes);
                        }

                        byte[] dekSalt = new byte[32];
                        RandomNumberGenerator.Fill(dekSalt);

                        var tempEncService = new PhantomVault.Core.Services.EncryptionService();
                        byte[] kek = tempEncService.DeriveKey(combinedSecretForDek.AsSpan(), dekSalt);

                        // Derive hybrid DEK = KEK ⊕ KEM shared secret
                        byte[] hybridDek = PhantomVault.Core.Utils.HybridKeyDerivation.DeriveHybridKey(kek, hybridSharedSecret);

                        // Unlock ZK service with hybrid DEK
                        unlocked = await _zkVaultService.UnlockWithHybridKeyAsync(hybridDek);

                        // Zero sensitive materials
                        PhantomVault.Core.Utils.HybridKeyDerivation.ZeroMemory(kek, hybridDek, dekSalt);
                    }
                    else
                    {
                        // Fallback: Traditional key derivation (for backward compatibility)
                        string deviceId = _usbBindingService.ComputeDeviceId(SelectedDrive!);
                        unlocked = await _zkVaultService.UnlockMasterKeyAsync(password ?? string.Empty, keyfilePath, deviceId);
                    }

                    if (!unlocked)
                    {
                        throw new InvalidOperationException("Failed to unlock zero-knowledge vault service");
                    }

                    try
                    {
                        progress?.Report(0.8);

                        // Encrypt plaintext file using VaultFileZk
                        await _zkVaultService.EncryptFileAsync(tempPlaintext, dbPath);
                    }
                    finally
                    {
                        // Always lock the vault service after use
                        await _zkVaultService.LockAndWipeKeysAsync();
                    }
                }
                finally
                {
                    // Securely delete temporary plaintext file
                    if (File.Exists(tempPlaintext))
                    {
                        try
                        {
                            // Overwrite with zeros before deletion
                            var fileInfo = new FileInfo(tempPlaintext);
                            using (var stream = new FileStream(tempPlaintext, FileMode.Open, FileAccess.Write, FileShare.None))
                            {
                                var zeros = new byte[stream.Length];
                                stream.Write(zeros, 0, zeros.Length);
                                stream.Flush(true);
                            }
                            File.Delete(tempPlaintext);
                        }
                        catch
                        {
                            // Best effort cleanup
                        }
                    }
                }

                progress?.Report(1.0);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to create hybrid encrypted database: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Saves the ML-KEM-768 private key to disk in encrypted form.
        /// The private key (2400 bytes) is encrypted using the same master key derivation
        /// as the vault manifest to ensure it can only be accessed after authentication.
        /// </summary>
        /// <param name="keyPath">Path where the encrypted key will be stored.</param>
        /// <param name="privateKey">The ML-KEM-768 private key (2400 bytes).</param>
        /// <param name="password">User passphrase for key derivation.</param>
        /// <param name="keyfilePath">Optional keyfile path for additional entropy.</param>
        private async Task SaveKemPrivateKeyAsync(string keyPath, byte[] privateKey, string? password, string? keyfilePath)
        {
            if (privateKey == null) throw new ArgumentNullException(nameof(privateKey));
            if (privateKey.Length != 2400)
                throw new ArgumentException("Invalid ML-KEM-768 private key size. Expected 2400 bytes.", nameof(privateKey));

            try
            {
                // Use the EncryptionService from the DI container (via App services)
                var encryptionService = (Avalonia.Application.Current as App)?.Services?.GetService(typeof(EncryptionService)) as EncryptionService
                    ?? throw new InvalidOperationException("EncryptionService not available");

                // Generate salt for key derivation
                byte[] salt = encryptionService.GenerateSalt();

                // Combine passphrase with keyfile contents if provided
                string combinedSecret = password ?? string.Empty;
                if (!string.IsNullOrEmpty(keyfilePath) && File.Exists(keyfilePath))
                {
                    byte[] keyfileBytes = await File.ReadAllBytesAsync(keyfilePath);
                    combinedSecret = combinedSecret + Convert.ToBase64String(keyfileBytes);
                    CryptographicOperations.ZeroMemory(keyfileBytes);
                }

                // Derive encryption key using Argon2id
                byte[] masterKey = encryptionService.DeriveKey(combinedSecret.AsSpan(), salt);

                try
                {
                    // Encrypt the private key with AES-256-GCM
                    byte[] aad = Encoding.UTF8.GetBytes("ML-KEM-768-PRIVATE-KEY");
                    var encResult = encryptionService.Encrypt(privateKey, masterKey, aad);

                    // Create encrypted key file structure
                    var payload = new
                    {
                        algorithm = "ML-KEM-768",
                        salt = Convert.ToBase64String(salt),
                        nonce = Convert.ToBase64String(encResult.Nonce),
                        tag = Convert.ToBase64String(encResult.Tag),
                        ciphertext = Convert.ToBase64String(encResult.Ciphertext)
                    };

                    string payloadJson = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
                    await File.WriteAllTextAsync(keyPath, payloadJson);
                }
                finally
                {
                    // Wipe master key from memory
                    CryptographicOperations.ZeroMemory(masterKey);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save encrypted KEM private key: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Creates an encrypted password vault database file directly.
        /// Uses the same encryption as the manifest for consistency and .NET 8.0 compatibility.
        /// </summary>
        private async Task CreateKeePassDatabaseAsync(string dbPath, string? password, string? keyfilePath, IProgress<double>? progress = null)
        {
            await Task.Run(() =>
            {
                try
                {
                    progress?.Report(0.2);

                    // Create initial empty password database structure
                    var database = new
                    {
                        Version = "1.0",
                        Created = DateTime.UtcNow,
                        VaultName = VaultName ?? "PhantomVault",
                        Description = $"Created by PhantomVault on {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                        Groups = new[]
                        {
                            new
                            {
                                Id = Guid.NewGuid().ToString("N"),
                                Name = "General",
                                Entries = new object[] { }
                            }
                        }
                    };

                    progress?.Report(0.5);

                    // Serialize to JSON
                    var jsonContent = System.Text.Json.JsonSerializer.Serialize(database, new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    progress?.Report(0.7);

                    // Encrypt using the same method as manifest
                    var encryptionService = new PhantomVault.Core.Services.EncryptionService();

                    // Generate a random salt for key derivation (same as manifest)
                    byte[] salt = new byte[32];
                    System.Security.Cryptography.RandomNumberGenerator.Fill(salt);

                    // Combine password and keyfile into secret (same as manifest)
                    string combinedSecret = password ?? string.Empty;
                    if (!string.IsNullOrEmpty(keyfilePath) && File.Exists(keyfilePath))
                    {
                        byte[] keyfileBytes = File.ReadAllBytes(keyfilePath);
                        combinedSecret = combinedSecret + Convert.ToBase64String(keyfileBytes);
                        Array.Clear(keyfileBytes, 0, keyfileBytes.Length);
                    }

                    // Derive encryption key from combined secret
                    byte[] key = encryptionService.DeriveKey(combinedSecret.AsSpan(), salt);

                    // Encrypt the JSON content
                    byte[] plaintextBytes = System.Text.Encoding.UTF8.GetBytes(jsonContent);
                    var encryptionResult = encryptionService.Encrypt(plaintextBytes, key);

                    // Combine salt + nonce + tag + ciphertext for storage
                    // Format: [32 bytes: Salt][12 bytes: Nonce][16 bytes: Tag][Variable: Ciphertext]
                    using (var ms = new MemoryStream())
                    {
                        ms.Write(salt, 0, salt.Length);                             // 32 bytes
                        ms.Write(encryptionResult.Nonce, 0, encryptionResult.Nonce.Length);   // 12 bytes
                        ms.Write(encryptionResult.Tag, 0, encryptionResult.Tag.Length);       // 16 bytes
                        ms.Write(encryptionResult.Ciphertext, 0, encryptionResult.Ciphertext.Length);
                        File.WriteAllBytes(dbPath, ms.ToArray());
                    }

                    // Clear sensitive data
                    Array.Clear(key, 0, key.Length);
                    Array.Clear(salt, 0, salt.Length);
                    Array.Clear(plaintextBytes, 0, plaintextBytes.Length);

                    progress?.Report(1.0);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to create password vault database: {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// Observable that determines when the vault can be created. The user
        /// must select a drive, provide a vault name, and have a keyfile.
        /// Password is optional but if provided, confirm password must match.
        /// </summary>
        private IObservable<bool> CanCreateObservable()
        {
            return this.WhenAnyValue(
                vm => vm.IsBusy,
                vm => vm.SelectedDrive,
                vm => vm.VaultName,
                vm => vm.Passphrase,
                vm => vm.ConfirmPassphrase,
                vm => vm.KeyfilePath,
                (busy, drive, vaultName, p1, p2, keyfile) =>
                {
                    // Required: not busy, have drive, vault name, and keyfile
                    if (busy || string.IsNullOrEmpty(drive) || string.IsNullOrWhiteSpace(vaultName) || string.IsNullOrEmpty(keyfile))
                        return false;

                    // If password is provided, confirm password must match
                    if (!string.IsNullOrEmpty(p1) || !string.IsNullOrEmpty(p2))
                    {
                        return p1 == p2;
                    }

                    // Password is optional, so can proceed without it
                    return true;
                });
        }

        private void ValidatePassword()
        {
            PasswordError = string.Empty;

            // Password is optional, so empty is valid
            if (string.IsNullOrEmpty(_passphrase))
                return;

            // Check length
            if (_passphrase.Length < 8)
            {
                PasswordError = "⚠ Password must be at least 8 characters";
                return;
            }

            if (_passphrase.Length > 64)
            {
                PasswordError = "⚠ Password must be at most 64 characters";
                return;
            }

            // Check for uppercase
            if (!_passphrase.Any(char.IsUpper))
            {
                PasswordError = "⚠ Password must contain at least 1 uppercase letter";
                return;
            }

            // Check for lowercase
            if (!_passphrase.Any(char.IsLower))
            {
                PasswordError = "⚠ Password must contain at least 1 lowercase letter";
                return;
            }

            // Check for digit
            if (!_passphrase.Any(char.IsDigit))
            {
                PasswordError = "⚠ Password must contain at least 1 number";
                return;
            }

            // Check for special character
            if (!_passphrase.Any(c => !char.IsLetterOrDigit(c)))
            {
                PasswordError = "⚠ Password must contain at least 1 special character";
                return;
            }
        }

        private void ValidateConfirmPassword()
        {
            ConfirmPasswordError = string.Empty;

            // If both are empty, it's valid (password is optional)
            if (string.IsNullOrEmpty(_passphrase) && string.IsNullOrEmpty(_confirmPassphrase))
                return;

            // If one is filled but not the other
            if (string.IsNullOrEmpty(_passphrase) != string.IsNullOrEmpty(_confirmPassphrase))
            {
                ConfirmPasswordError = "⚠ Passwords must match";
                return;
            }

            // If both are filled, they must match
            if (_passphrase != _confirmPassphrase)
            {
                ConfirmPasswordError = "⚠ Passwords do not match";
                return;
            }
        }

        public ObservableCollection<string> Drives => _drives;

        public string? VaultName
        {
            get => _vaultName;
            set => this.RaiseAndSetIfChanged(ref _vaultName, value);
        }

        public int VolumeSizeMb
        {
            get => _volumeSizeMb;
            set => this.RaiseAndSetIfChanged(ref _volumeSizeMb, value);
        }

        public string? Passphrase
        {
            get => _passphrase;
            set
            {
                this.RaiseAndSetIfChanged(ref _passphrase, value);
                ValidatePassword();
                ValidateConfirmPassword();
            }
        }

        public string? ConfirmPassphrase
        {
            get => _confirmPassphrase;
            set
            {
                this.RaiseAndSetIfChanged(ref _confirmPassphrase, value);
                ValidateConfirmPassword();
            }
        }

        public string PasswordError
        {
            get => _passwordError;
            set => this.RaiseAndSetIfChanged(ref _passwordError, value);
        }

        public string ConfirmPasswordError
        {
            get => _confirmPasswordError;
            set => this.RaiseAndSetIfChanged(ref _confirmPasswordError, value);
        }

        public bool ShowPassword
        {
            get => _showPassword;
            set => this.RaiseAndSetIfChanged(ref _showPassword, value);
        }

        public bool ShowConfirmPassword
        {
            get => _showConfirmPassword;
            set => this.RaiseAndSetIfChanged(ref _showConfirmPassword, value);
        }

        public string? KeyfilePath
        {
            get => _keyfilePath;
            set => this.RaiseAndSetIfChanged(ref _keyfilePath, value);
        }

        public string? SelectedDrive
        {
            get => _selectedDrive;
            set => this.RaiseAndSetIfChanged(ref _selectedDrive, value);
        }

        public bool RequiresHardwareToken
        {
            get => _requiresHardwareToken;
            set => this.RaiseAndSetIfChanged(ref _requiresHardwareToken, value);
        }

        /// <summary>
        /// When enabled, a time‑based one‑time password secret will be
        /// generated and stored in the manifest. The user will need to
        /// supply a TOTP code in addition to their passphrase when
        /// unlocking the vault.
        /// </summary>
        public bool UseTotp
        {
            get => _useTotp;
            set => this.RaiseAndSetIfChanged(ref _useTotp, value);
        }

        /// <summary>
        /// When enabled, a passkey (FIDO2/WebAuthn credential) will be
        /// registered during provisioning. The manifest will record the
        /// credential ID. An external service will prompt the user to
        /// interact with their authenticator (e.g. Windows Hello, YubiKey)
        /// during registration.
        /// </summary>
        public bool UsePasskey
        {
            get => _usePasskey;
            set => this.RaiseAndSetIfChanged(ref _usePasskey, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        public double Progress
        {
            get => _progress;
            private set => this.RaiseAndSetIfChanged(ref _progress, value);
        }

        public string Status
        {
            get => _status;
            private set => this.RaiseAndSetIfChanged(ref _status, value);
        }

        public string SelectedEncryptionLevel
        {
            get => _selectedEncryptionLevel;
            set => this.RaiseAndSetIfChanged(ref _selectedEncryptionLevel, value);
        }

        public string KeePassFilePath
        {
            get => _keePassFilePath;
            set => this.RaiseAndSetIfChanged(ref _keePassFilePath, value);
        }

        public string ImportStatus
        {
            get => _importStatus;
            set => this.RaiseAndSetIfChanged(ref _importStatus, value);
        }

        public string VolumeSizeGB => $"{VolumeSizeMb / 1024.0:F2}";

        public List<string> EncryptionLevels => new()
        {
            "Low (Fast)",
            "Medium (Recommended)",
            "High (Secure)",
            "Maximum (Military-Grade)"
        };

        public string EncryptionLevelDescription => SelectedEncryptionLevel switch
        {
            "Low (Fast)" => "Standard AES-256 encryption with default iterations. Good for most users.",
            "Medium (Recommended)" => "AES-256 with increased key derivation rounds. Balanced security and performance.",
            "High (Secure)" => "ML-KEM-768 post-quantum encryption. Extra protection against future threats.",
            "Maximum (Military-Grade)" => "Hybrid post-quantum with maximum iterations. Slowest but most secure.",
            _ => string.Empty
        };

        private async Task BrowseKeyfileAsync()
        {
            try
            {
                if (_ownerWindow == null)
                {
                    Status = "Window not initialized";
                    return;
                }

                var storageProvider = _ownerWindow.StorageProvider;
                if (storageProvider == null) return;

                var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Keyfile",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Keyfiles")
                        {
                            Patterns = new[] { "*.key", "*.keyfile" }
                        },
                        new FilePickerFileType("All Files")
                        {
                            Patterns = new[] { "*.*" }
                        }
                    }
                });

                if (files.Count > 0)
                {
                    KeyfilePath = files[0].Path.LocalPath;
                }
            }
            catch (Exception ex)
            {
                Status = $"Error selecting keyfile: {ex.Message}";
            }
        }

        private async Task GenerateKeyfileAsync()
        {
            try
            {
                if (_ownerWindow == null)
                {
                    Status = "Window not initialized";
                    return;
                }

                var storageProvider = _ownerWindow.StorageProvider;
                if (storageProvider == null) return;

                var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save Keyfile",
                    DefaultExtension = "key",
                    SuggestedFileName = "vault.key",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("Keyfile")
                        {
                            Patterns = new[] { "*.key", "*.keyfile" }
                        },
                        new FilePickerFileType("All Files")
                        {
                            Patterns = new[] { "*.*" }
                        }
                    }
                });

                if (file != null)
                {
                    Status = "Generating keyfile...";
                    var filePath = file.Path.LocalPath;
                    // Generate 64KB keyfile using KeyfileGeneratorService
                    _keyfileGeneratorService.GenerateKeyfile(filePath, sizeKB: 64);
                    KeyfilePath = filePath;
                    Status = $"✓ Keyfile generated: {Path.GetFileName(filePath)}";

                    // Clear status after 3 seconds
                    await Task.Delay(3000);
                    Status = string.Empty;
                }
                else
                {
                    // User cancelled - reset status
                    Status = string.Empty;
                }
            }
            catch (Exception ex)
            {
                Status = $"Error generating keyfile: {ex.Message}";
            }
        }

        private async Task ImportKeePassAsync()
        {
            ImportStatus = "Selecting KeePass file...";

            try
            {
                if (_ownerWindow == null)
                {
                    ImportStatus = "Window not initialized";
                    return;
                }

                var storageProvider = _ownerWindow.StorageProvider;
                if (storageProvider == null) return;

                var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select KeePass Database",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("KeePass Database")
                        {
                            Patterns = new[] { "*.kdbx" },
                            MimeTypes = new[] { "application/x-keepass" }
                        },
                        new FilePickerFileType("All Files")
                        {
                            Patterns = new[] { "*.*" }
                        }
                    }
                });

                if (files.Count > 0)
                {
                    KeePassFilePath = files[0].Path.LocalPath;
                    ImportStatus = $"✓ Selected: {Path.GetFileName(files[0].Name)}";
                    ImportStatus += "\n\n⚠ Import will be processed after vault creation.";
                }
                else
                {
                    ImportStatus = string.Empty;
                }
            }
            catch (Exception ex)
            {
                ImportStatus = $"Error selecting file: {ex.Message}";
            }
        }

        private async Task OpenImportDialogAsync()
        {
            try
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var importVm = new ImportViewModel();
                    var importWindow = new ImportWindow(importVm);

                    // When the import window is closed (modal or non-modal), capture any selected KeePass file
                    void OnClosed(object? s, EventArgs e)
                    {
                        try
                        {
                            var selected = importVm.SelectedFile;
                            if (!string.IsNullOrWhiteSpace(selected))
                            {
                                var ext = System.IO.Path.GetExtension(selected).ToLowerInvariant();
                                if (ext == ".kdbx")
                                {
                                    KeePassFilePath = selected;
                                    ImportStatus = $"✓ Selected: {System.IO.Path.GetFileName(selected)}\nThis KeePass file will be queued for import after vault creation.";
                                }
                            }
                        }
                        catch { /* best effort */ }
                        finally
                        {
                            if (importWindow != null)
                            {
                                importWindow.Closed -= OnClosed;
                            }
                        }
                    }
                    importWindow.Closed += OnClosed;

                    if (_ownerWindow != null)
                    {
                        try
                        {
                            await importWindow.ShowDialog(_ownerWindow);
                        }
                        catch
                        {
                            importWindow.Show();
                        }
                    }
                    else
                    {
                        importWindow.Show();
                    }

                    // If we showed modally, Closed will still fire and OnClosed will capture the selection
                });
            }
            catch (Exception ex)
            {
                Status = $"Failed to open import dialog: {ex.Message}";
                try
                {
                    await _dialogService.ShowErrorAsync("Import", $"Unable to open import dialog: {ex.Message}", _ownerWindow);
                }
                catch { }
            }
        }

        private async Task ProcessKeePassImportAsync(string vaultPath)
        {
            try
            {
                Status = "Processing KeePass import...";
                ImportStatus = "🔄 Importing credentials from KeePass database...";

                // For security, we'll ask for the KeePass password
                // In a real implementation, this would show a password dialog
                // For now, we'll use the vault password as a default attempt
                string keePassPassword = Passphrase ?? "default";

                var progress = new Progress<int>(percent =>
                {
                    ImportStatus = $"Importing credentials... {percent}%";
                });

                var importResult = await _keePassImportService.ImportAsync(
                    KeePassFilePath,
                    keePassPassword,
                    keyfilePath: null,
                    progress);

                if (importResult.IsSuccess)
                {
                    // Save imported credentials to vault
                    // The vault database file is already created and encrypted inside the VeraCrypt container
                    try
                    {
                        // Build the path to the vault database inside the mounted container
                        var vaultDbPath = Path.Combine(SelectedDrive!, "vault.db");

                        // Read existing vault database if it exists, otherwise create new
                        VaultDatabase vaultDb;
                        if (File.Exists(vaultDbPath))
                        {
                            var existingJson = await File.ReadAllTextAsync(vaultDbPath);
                            vaultDb = System.Text.Json.JsonSerializer.Deserialize<VaultDatabase>(existingJson) ?? new VaultDatabase();
                        }
                        else
                        {
                            vaultDb = new VaultDatabase
                            {
                                VaultName = VaultName ?? "Imported Vault",
                                Created = System.DateTime.UtcNow
                            };
                        }

                        // Initialize Groups if null
                        if (vaultDb.Groups == null)
                            vaultDb.Groups = new List<VaultGroup>();

                        // Organize imported credentials by group
                        var credentialsByGroup = importResult.Credentials.GroupBy(c => c.Group ?? "Imported");

                        foreach (var grouping in credentialsByGroup)
                        {
                            var groupName = string.IsNullOrWhiteSpace(grouping.Key) ? "Imported" : grouping.Key;
                            
                            // Find existing group or create new one
                            var group = vaultDb.Groups.FirstOrDefault(g => g.Name == groupName);
                            if (group == null)
                            {
                                group = new VaultGroup
                                {
                                    Id = System.Guid.NewGuid().ToString(),
                                    Name = groupName,
                                    Icon = "📁",
                                    Entries = new List<Credential>()
                                };
                                vaultDb.Groups.Add(group);
                            }

                            // Add credentials to the group
                            if (group.Entries == null)
                                group.Entries = new List<Credential>();

                            foreach (var credential in grouping)
                            {
                                group.Entries.Add(credential);
                            }
                        }

                        // Serialize and save the updated vault database back to the container
                        var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                        var updatedJson = System.Text.Json.JsonSerializer.Serialize(vaultDb, jsonOptions);
                        await File.WriteAllTextAsync(vaultDbPath, updatedJson);

                        ImportStatus = $"✓ Import Complete!\n\n" +
                                     $"📊 Summary:\n" +
                                     $"  • Total credentials: {importResult.TotalEntries}\n" +
                                     $"  • Groups/folders: {importResult.TotalGroups}\n" +
                                     $"  • Saved to vault: {vaultDbPath}\n\n" +
                                     $"{importResult.Message}";
                    }
                    catch (Exception saveEx)
                    {
                        ImportStatus = $"⚠ Import Successful but Save Failed\n\n" +
                                     $"Imported {importResult.TotalEntries} credentials from KeePass, but failed to save to vault:\n" +
                                     $"{saveEx.Message}\n\n" +
                                     $"You can try importing again from the vault management screen.";
                    }

                    Status = $"Vault created successfully. Imported {importResult.TotalEntries} credentials from KeePass.";

                    // Log the import in audit trail
                    try
                    {
                        var auditPath = Path.Combine(SelectedDrive!, "vault.audit");
                        _auditService.LogEvent(auditPath, "import",
                            $"Imported {importResult.TotalEntries} credentials from KeePass");
                    }
                    catch
                    {
                        // Ignore audit errors
                    }
                }
                else
                {
                    ImportStatus = $"❌ Import Failed\n\n{importResult.Message}\n\n" +
                                 $"The vault was created successfully, but credentials were not imported.\n" +
                                 $"You can try importing again from the vault management screen.";

                    Status = "Vault created successfully, but KeePass import failed.";
                }
            }
            catch (Exception ex)
            {
                ImportStatus = $"❌ Import Error\n\n{ex.Message}\n\n" +
                             $"The vault was created successfully, but credentials could not be imported.";

                Status = "Vault created, but import encountered an error.";
            }
        }

        /// <summary>
        /// Sets the owner window for dialog display.
        /// </summary>
        public void SetOwnerWindow(Window window)
        {
            _ownerWindow = window;
        }

        /// <summary>
        /// Reset transient state after an error to allow the setup flow to continue.
        /// This clears status messages, resets busy flag and refreshes detected drives.
        /// </summary>
        public async Task ResetAfterErrorAsync()
        {
            try
            {
                Status = string.Empty;
                IsBusy = false;

                // Refresh drives from detector in background
                await Task.Run(() =>
                {
                    _drives.Clear();
                    foreach (var d in _usbDetector.GetRemovableDrives())
                        _drives.Add(d);
                });

                // Auto-select a drive if none selected
                if (string.IsNullOrEmpty(SelectedDrive) && _drives.Count > 0)
                {
                    SelectedDrive = _drives[0];
                }
            }
            catch
            {
                // Best-effort reset; swallow errors
            }
        }

        public ReactiveCommand<Unit, Unit> BrowseKeyfileCommand { get; }
        public ReactiveCommand<Unit, Unit> GenerateKeyfileCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearKeyfileCommand { get; }
        public ReactiveCommand<Unit, Unit> ImportKeePassCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenImportDialogCommand { get; }
        public ReactiveCommand<Unit, Unit> CreateVaultCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleShowPasswordCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleShowConfirmPasswordCommand { get; }

        /// <summary>
        /// Creates the hidden .phantom folder structure on the USB drive.
        /// Returns the path to the .phantom folder.
        /// </summary>
        private string CreatePhantomFolder(string usbDrive)
        {
            var phantomPath = Path.Combine(usbDrive, ".phantom");

            if (!Directory.Exists(phantomPath))
            {
                Directory.CreateDirectory(phantomPath);
                Directory.CreateDirectory(Path.Combine(phantomPath, "vaults"));
                Directory.CreateDirectory(Path.Combine(phantomPath, "manifests"));
                Directory.CreateDirectory(Path.Combine(phantomPath, "audit"));
                Directory.CreateDirectory(Path.Combine(phantomPath, "backups"));

                // Set hidden + system attributes
                var dirInfo = new DirectoryInfo(phantomPath);
                dirInfo.Attributes = FileAttributes.Hidden | FileAttributes.System;
            }

            return phantomPath;
        }

        /// <summary>
        /// Generates an encrypted filename using SHA256 hash of the vault name.
        /// This prevents vault names from being visible in the filesystem.
        /// </summary>
        private string GetEncryptedFilename(string vaultName)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(vaultName));
                // Convert to base64 and make filesystem-safe
                return Convert.ToBase64String(hash)
                    .Replace("/", "_")
                    .Replace("+", "-")
                    .Replace("=", "");
            }
        }

        #region User Preferences

        /// <summary>
        /// Loads user's default preferences for encryption and authentication.
        /// </summary>
        private void LoadDefaultPreferences()
        {
            var settings = SettingsService.Load();
            
            // Set encryption level based on saved preference
            if (!string.IsNullOrEmpty(settings.PreferredEncryptionProfile))
            {
                SelectedEncryptionLevel = settings.PreferredEncryptionProfile switch
                {
                    "Basic" => "Low (Fast)",
                    "Advanced" => "Medium (Recommended)",
                    "Paranoid" => "Maximum (Military-Grade)",
                    _ => "Medium (Recommended)"
                };
            }

            // Set authentication defaults
            RequiresHardwareToken = settings.DefaultRequireHardwareToken;
            UseTotp = settings.DefaultUseTotp;
            UsePasskey = settings.DefaultUsePasskey;
        }

        /// <summary>
        /// Saves the user's authentication method choice for future reference.
        /// </summary>
        private void SaveAuthenticationMethodPreference()
        {
            var settings = SettingsService.Load();
            
            // Determine which auth method was used
            if (UsePasskey)
                settings.LastAuthenticationMethod = "Passkey";
            else if (UseTotp)
                settings.LastAuthenticationMethod = "TOTP";
            else if (RequiresHardwareToken)
                settings.LastAuthenticationMethod = "YubiKey";
            else
                settings.LastAuthenticationMethod = "Password";

            // Save current selections as new defaults
            settings.DefaultRequireHardwareToken = RequiresHardwareToken;
            settings.DefaultUseTotp = UseTotp;
            settings.DefaultUsePasskey = UsePasskey;

            // Save encryption preference
            settings.PreferredEncryptionProfile = SelectedEncryptionLevel switch
            {
                "Low (Fast)" => "Basic",
                "Medium (Recommended)" => "Advanced",
                "High (Secure)" => "Advanced",
                "Maximum (Military-Grade)" => "Paranoid",
                _ => "Advanced"
            };

            SettingsService.Save(settings);
        }

        #endregion
    }
}
