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
using PhantomVault.Core.Utils;
using PhantomVault.Core.Services.ZeroKnowledge;
using PhantomVault.UI.Services;
using PhantomVault.UI.Views;
using GiblexVault.Security.ZK;
using System.Security.Principal;

namespace PhantomVault.UI.ViewModels
{

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

        private readonly ObservableCollection<string> _drives = new();

        private string? _vaultName;
        private int _volumeSizeMb = 5120;
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

        private readonly KeyfileGeneratorService _keyfileGeneratorService;
        private readonly KeePassImportService _keePassImportService;
        private readonly DialogService _dialogService;
        private readonly IHybridEncryptionService _hybridEncryptionService;
        private Window? _ownerWindow;

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
            _keyfileGeneratorService = keyfileGeneratorService ?? throw new ArgumentNullException(nameof(keyfileGeneratorService));
            _keePassImportService = keePassImportService ?? throw new ArgumentNullException(nameof(keePassImportService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

            foreach (var drive in _usbDetector.GetRemovableDrives())
            {
                _drives.Add(drive);
            }
            _usbDetector.RemovableDriveInserted += d => _drives.Add(d);
            _usbDetector.RemovableDriveRemoved += d => _drives.Remove(d);

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

            LoadDefaultPreferences();

            CreateVaultCommand = ReactiveCommand.CreateFromTask(async () =>
            {
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

                    var phantomPath = CreatePhantomFolder(SelectedDrive!);
                    var encryptedName = GetEncryptedFilename(VaultName!);

                    var passwordToUse = string.IsNullOrEmpty(Passphrase) ? null : Passphrase;

                    byte[]? passkeyCredentialId = null;

                    Status = "Generating post-quantum cryptographic keys...";
                    Progress = 0.05;
                    var (kemPublicKey, kemPrivateKey) = _hybridEncryptionService.GenerateKeyPair();

                    Status = "Establishing quantum-resistant key exchange...";
                    Progress = 0.06;
                    var (kemCiphertext, kemSharedSecret) = _hybridEncryptionService.EncapsulateSecret(kemPublicKey);

                    Status = "Securing cryptographic keys...";
                    Progress = 0.07;

                    byte[]? kemCiphertextForManifest = kemCiphertext;
                    byte[]? kemSharedSecretForVault = kemSharedSecret;

                    string containerPath;
                    long containerSizeBytes = VolumeSizeMb * 1024 * 1024;
                    string vaultPath;
                    string kemKeyPath;

                    Status = "Creating encrypted vault (zero-knowledge)...";
                    Progress = 0.1;
                    var directDir = Path.Combine(phantomPath, "vaults");
                    Directory.CreateDirectory(directDir);
                    vaultPath = Path.Combine(directDir, "vault.pvault");
                    var vaultProgress = new Progress<double>(p => Progress = 0.1 + (p * 0.6));
                    await CreateHybridEncryptedDatabaseAsync(vaultPath, passwordToUse, KeyfilePath, kemSharedSecretForVault, vaultProgress);

                    Status = "Securing post-quantum encryption keys...";
                    Progress = 0.7;
                    kemKeyPath = Path.Combine(directDir, "kem.key");
                    await SaveKemPrivateKeyAsync(kemKeyPath, kemPrivateKey, passwordToUse, KeyfilePath);

                    containerPath = vaultPath;
                    Progress = 0.8;

                    if (passkeyCredentialId != null)
                    {
                        CryptographicOperations.ZeroMemory(passkeyCredentialId);
                        passkeyCredentialId = null;
                    }

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

                    CryptographicOperations.ZeroMemory(encryptedPrivateKeyResult.Ciphertext);
                    CryptographicOperations.ZeroMemory(encryptedPrivateKeyResult.Nonce);
                    CryptographicOperations.ZeroMemory(encryptedPrivateKeyResult.Tag);
                    CryptographicOperations.ZeroMemory(aad);

                    PhantomVault.Core.Utils.HybridKeyDerivation.ZeroMemory(kek, kemSharedSecretForVault);
                    string manifestSaltBase64 = Convert.ToBase64String(manifestSalt);
                    PhantomVault.Core.Utils.HybridKeyDerivation.ZeroMemory(manifestSalt);

                    var manifest = new VaultManifest
                    {
                        VaultName = VaultName!,
                        ContainerPath = containerPath,
                        ContainerSizeBytes = containerSizeBytes,
                        RequiresHardwareToken = RequiresHardwareToken,
                        DeviceId = deviceId,

                        AutoBackupEnabled = true,
                        BackupRetentionDays = 3,

                        KemPublicKeyBase64 = Convert.ToBase64String(kemPublicKey),

                        KemCiphertextBase64 = Convert.ToBase64String(kemCiphertextForManifest!),
                        KemPrivateKeyEncryptedBase64 = encryptedPrivateKeyBase64,
                        SaltBase64 = manifestSaltBase64,
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

                    var manifestPath = Path.Combine(phantomPath, "manifests", $"{encryptedName}.manifest");

                    var passwordForManifest = string.IsNullOrEmpty(Passphrase) ? null : Passphrase;
                    Progress = 0.82;
                    using (var manifestPassphrase = SecurePassword.FromString(passwordForManifest))
                        _manifestService.WriteManifestSecure(manifest, manifestPath, manifestPassphrase, KeyfilePath);

                    PhantomVault.Core.Utils.HybridKeyDerivation.ZeroMemory(kemPrivateKey, kemPublicKey, kemCiphertextForManifest!);

                    Progress = 0.83;

                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await _dialogService.ShowSuccessAsync(
                            "Vault Created",
                            $"Your vault '{VaultName}' has been successfully created on {Path.GetFileName(SelectedDrive)}.",
                            _ownerWindow);
                    });

                    Status = "Vault successfully created.";

                    if (!string.IsNullOrEmpty(KeePassFilePath))
                    {
                        await ProcessKeePassImportAsync(SelectedDrive!);
                    }

                    try
                    {

                        if (manifest.AutoBackupEnabled)
                        {
                            string backupDir = Path.Combine(phantomPath, "backups");
                            _ = await _backupService.CreateBackupAsync(manifestPath, Passphrase!, KeyfilePath, backupDir);
                            _backupService.PruneBackups(backupDir, manifest.BackupRetentionDays);
                        }
                    }
                    catch (Exception ex)
                    {
                        // A failed first backup must not abort a successfully provisioned
                        // vault, but it should not vanish either.
                        Serilog.Log.Warning(ex, "Initial vault backup failed after provisioning");
                    }

                    try
                    {

                        var auditPath = Path.Combine(phantomPath, "audit", $"{encryptedName}.audit");
                        _auditService.LogEvent(auditPath, "provision", $"Vault '{VaultName}' created on {SelectedDrive}");
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning(ex, "Failed to write the provisioning audit entry");
                    }

                    SaveAuthenticationMethodPreference();

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

        private async Task CreateHybridEncryptedDatabaseAsync(string dbPath, string? password, string? keyfilePath, byte[]? hybridSharedSecret = null, IProgress<double>? progress = null)
        {
            try
            {
                progress?.Report(0.1);

                var database = new
                {
                    Version = "2.0",
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

                var jsonContent = System.Text.Json.JsonSerializer.Serialize(database, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

                progress?.Report(0.5);

                var tempPlaintext = Path.GetTempFileName();
                try
                {
                    await File.WriteAllTextAsync(tempPlaintext, jsonContent);

                    progress?.Report(0.7);

                    bool unlocked = false;

                    if (hybridSharedSecret != null)
                    {

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

                        byte[] hybridDek = PhantomVault.Core.Utils.HybridKeyDerivation.DeriveHybridKey(kek, hybridSharedSecret);

                        unlocked = await _zkVaultService.UnlockWithHybridKeyAsync(hybridDek);

                        PhantomVault.Core.Utils.HybridKeyDerivation.ZeroMemory(kek, hybridDek, dekSalt);
                    }
                    else
                    {

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

                        await _zkVaultService.EncryptFileAsync(tempPlaintext, dbPath);
                    }
                    finally
                    {

                        await _zkVaultService.LockAndWipeKeysAsync();
                    }
                }
                finally
                {

                    if (File.Exists(tempPlaintext))
                    {
                        try
                        {

                            var fileInfo = new FileInfo(tempPlaintext);
                            using (var stream = new FileStream(tempPlaintext, FileMode.Open, FileAccess.Write, FileShare.None))
                            {
                                var zeros = new byte[stream.Length];
                                stream.Write(zeros, 0, zeros.Length);
                                stream.Flush(true);
                            }
                            File.Delete(tempPlaintext);
                        }
                        catch (Exception ex)
                        {
                            // The plaintext temp file may survive; that is worth knowing.
                            Serilog.Log.Warning(ex, "Failed to shred the temporary plaintext import file");
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

        private async Task SaveKemPrivateKeyAsync(string keyPath, byte[] privateKey, string? password, string? keyfilePath)
        {
            if (privateKey == null) throw new ArgumentNullException(nameof(privateKey));
            if (privateKey.Length != 2400)
                throw new ArgumentException("Invalid ML-KEM-768 private key size. Expected 2400 bytes.", nameof(privateKey));

            try
            {

                var encryptionService = (Avalonia.Application.Current as App)?.Services?.GetService(typeof(EncryptionService)) as EncryptionService
                    ?? throw new InvalidOperationException("EncryptionService not available");

                byte[] salt = encryptionService.GenerateSalt();

                string combinedSecret = password ?? string.Empty;
                if (!string.IsNullOrEmpty(keyfilePath) && File.Exists(keyfilePath))
                {
                    byte[] keyfileBytes = await File.ReadAllBytesAsync(keyfilePath);
                    combinedSecret = combinedSecret + Convert.ToBase64String(keyfileBytes);
                    CryptographicOperations.ZeroMemory(keyfileBytes);
                }

                byte[] masterKey = encryptionService.DeriveKey(combinedSecret.AsSpan(), salt);

                try
                {

                    byte[] aad = Encoding.UTF8.GetBytes("ML-KEM-768-PRIVATE-KEY");
                    var encResult = encryptionService.Encrypt(privateKey, masterKey, aad);

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

                    CryptographicOperations.ZeroMemory(masterKey);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save encrypted KEM private key: {ex.Message}", ex);
            }
        }

        private async Task CreateKeePassDatabaseAsync(string dbPath, string? password, string? keyfilePath, IProgress<double>? progress = null)
        {
            await Task.Run(() =>
            {
                try
                {
                    progress?.Report(0.2);

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

                    var jsonContent = System.Text.Json.JsonSerializer.Serialize(database, new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    progress?.Report(0.7);

                    var encryptionService = new PhantomVault.Core.Services.EncryptionService();

                    byte[] salt = new byte[32];
                    System.Security.Cryptography.RandomNumberGenerator.Fill(salt);

                    string combinedSecret = password ?? string.Empty;
                    if (!string.IsNullOrEmpty(keyfilePath) && File.Exists(keyfilePath))
                    {
                        byte[] keyfileBytes = File.ReadAllBytes(keyfilePath);
                        combinedSecret = combinedSecret + Convert.ToBase64String(keyfileBytes);
                        Array.Clear(keyfileBytes, 0, keyfileBytes.Length);
                    }

                    byte[] key = encryptionService.DeriveKey(combinedSecret.AsSpan(), salt);

                    byte[] plaintextBytes = System.Text.Encoding.UTF8.GetBytes(jsonContent);
                    var encryptionResult = encryptionService.Encrypt(plaintextBytes, key);

                    using (var ms = new MemoryStream())
                    {
                        ms.Write(salt, 0, salt.Length);
                        ms.Write(encryptionResult.Nonce, 0, encryptionResult.Nonce.Length);
                        ms.Write(encryptionResult.Tag, 0, encryptionResult.Tag.Length);
                        ms.Write(encryptionResult.Ciphertext, 0, encryptionResult.Ciphertext.Length);
                        File.WriteAllBytes(dbPath, ms.ToArray());
                    }

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

                    if (busy || string.IsNullOrEmpty(drive) || string.IsNullOrWhiteSpace(vaultName) || string.IsNullOrEmpty(keyfile))
                        return false;

                    if (!string.IsNullOrEmpty(p1) || !string.IsNullOrEmpty(p2))
                    {
                        return p1 == p2;
                    }

                    return true;
                });
        }

        private void ValidatePassword()
        {
            PasswordError = string.Empty;

            if (string.IsNullOrEmpty(_passphrase))
                return;

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

            if (!_passphrase.Any(char.IsUpper))
            {
                PasswordError = "⚠ Password must contain at least 1 uppercase letter";
                return;
            }

            if (!_passphrase.Any(char.IsLower))
            {
                PasswordError = "⚠ Password must contain at least 1 lowercase letter";
                return;
            }

            if (!_passphrase.Any(char.IsDigit))
            {
                PasswordError = "⚠ Password must contain at least 1 number";
                return;
            }

            if (!_passphrase.Any(c => !char.IsLetterOrDigit(c)))
            {
                PasswordError = "⚠ Password must contain at least 1 special character";
                return;
            }
        }

        private void ValidateConfirmPassword()
        {
            ConfirmPasswordError = string.Empty;

            if (string.IsNullOrEmpty(_passphrase) && string.IsNullOrEmpty(_confirmPassphrase))
                return;

            if (string.IsNullOrEmpty(_passphrase) != string.IsNullOrEmpty(_confirmPassphrase))
            {
                ConfirmPasswordError = "⚠ Passwords must match";
                return;
            }

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

        public bool UseTotp
        {
            get => _useTotp;
            set => this.RaiseAndSetIfChanged(ref _useTotp, value);
        }

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

                    _keyfileGeneratorService.GenerateKeyfile(filePath, sizeKB: 64);
                    KeyfilePath = filePath;
                    Status = $"✓ Keyfile generated: {Path.GetFileName(filePath)}";

                    await Task.Delay(3000);
                    Status = string.Empty;
                }
                else
                {

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
                        catch {  }
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

                    try
                    {

                        var phantomVaultsDir = Path.Combine(SelectedDrive!, ".phantom", "vaults");
                        Directory.CreateDirectory(phantomVaultsDir);
                        var vaultDbPath = Path.Combine(phantomVaultsDir, "vault.db");

                        var encService = (Avalonia.Application.Current as App)?.Services?.GetService(typeof(EncryptionService)) as EncryptionService
                            ?? throw new InvalidOperationException("EncryptionService not available for credential encryption.");

                        VaultDatabase vaultDb;
                        if (File.Exists(vaultDbPath))
                        {
                            vaultDb = DecryptVaultDatabase(vaultDbPath, encService) ?? new VaultDatabase();
                        }
                        else
                        {

                            var legacyPath = Path.Combine(SelectedDrive!, "vault.db");
                            if (File.Exists(legacyPath))
                            {

                                try
                                {
                                    var legacyJson = await File.ReadAllTextAsync(legacyPath);
                                    vaultDb = System.Text.Json.JsonSerializer.Deserialize<VaultDatabase>(legacyJson) ?? new VaultDatabase();

                                    File.Delete(legacyPath);
                                }
                                catch
                                {
                                    vaultDb = new VaultDatabase
                                    {
                                        VaultName = VaultName ?? "Imported Vault",
                                        Created = System.DateTime.UtcNow
                                    };
                                }
                            }
                            else
                            {
                                vaultDb = new VaultDatabase
                                {
                                    VaultName = VaultName ?? "Imported Vault",
                                    Created = System.DateTime.UtcNow
                                };
                            }
                        }

                        if (vaultDb.Groups == null)
                            vaultDb.Groups = new List<VaultGroup>();

                        var credentialsByGroup = importResult.Credentials.GroupBy(c => c.Group ?? "Imported");

                        foreach (var grouping in credentialsByGroup)
                        {
                            var groupName = string.IsNullOrWhiteSpace(grouping.Key) ? "Imported" : grouping.Key;

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

                            if (group.Entries == null)
                                group.Entries = new List<Credential>();

                            foreach (var credential in grouping)
                            {
                                group.Entries.Add(credential);
                            }
                        }

                        EncryptVaultDatabase(vaultDbPath, vaultDb, encService);

                        ImportStatus = $"✓ Import Complete!\n\n" +
                                     $"📊 Summary:\n" +
                                     $"  • Total credentials: {importResult.TotalEntries}\n" +
                                     $"  • Groups/folders: {importResult.TotalGroups}\n" +
                                     $"  • Saved to vault (encrypted)\n\n" +
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

                    try
                    {
                        var auditDir = Path.Combine(SelectedDrive!, ".phantom", "audit");
                        Directory.CreateDirectory(auditDir);
                        var auditPath = Path.Combine(auditDir, "import.audit");
                        _auditService.LogEvent(auditPath, "import",
                            $"Imported {importResult.TotalEntries} credentials from KeePass");
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning(ex, "Failed to write the KeePass import audit entry");
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

        public void SetOwnerWindow(Window window)
        {
            _ownerWindow = window;
        }

        public async Task ResetAfterErrorAsync()
        {
            try
            {
                Status = string.Empty;
                IsBusy = false;

                await Task.Run(() =>
                {
                    _drives.Clear();
                    foreach (var d in _usbDetector.GetRemovableDrives())
                        _drives.Add(d);
                });

                if (string.IsNullOrEmpty(SelectedDrive) && _drives.Count > 0)
                {
                    SelectedDrive = _drives[0];
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to enumerate removable drives for provisioning");
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

        private static void EncryptVaultDatabase(string path, VaultDatabase vaultDb, EncryptionService encService)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(vaultDb, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = false
            });

            var plaintext = Encoding.UTF8.GetBytes(json);
            var nonce = new byte[12];
            RandomNumberGenerator.Fill(nonce);

            byte[] key;
            using (var sha = SHA256.Create())
            {

                var info = Encoding.UTF8.GetBytes("phantom.vaultdb.encryption.v1:" + Path.GetDirectoryName(path));
                var ikm = sha.ComputeHash(Encoding.UTF8.GetBytes(path));
                key = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, salt: nonce, info: info);
            }

            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[16];

            using (var aes = new AesGcm(key, 16))
            {
                aes.Encrypt(nonce, plaintext, ciphertext, tag);
            }

            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            fs.Write(nonce);
            fs.Write(tag);
            fs.Write(ciphertext);
        }

        private static VaultDatabase? DecryptVaultDatabase(string path, EncryptionService encService)
        {
            try
            {
                var raw = File.ReadAllBytes(path);

                if (raw.Length < 29)
                    return TryReadPlaintextVaultDb(path);

                var nonce = raw.AsSpan(0, 12);
                var tag = raw.AsSpan(12, 16);
                var ciphertext = raw.AsSpan(28);

                byte[] key;
                using (var sha = SHA256.Create())
                {
                    var info = Encoding.UTF8.GetBytes("phantom.vaultdb.encryption.v1:" + Path.GetDirectoryName(path));
                    var ikm = sha.ComputeHash(Encoding.UTF8.GetBytes(path));
                    key = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, salt: nonce.ToArray(), info: info);
                }

                var plaintext = new byte[ciphertext.Length];
                using (var aes = new AesGcm(key, 16))
                {
                    aes.Decrypt(nonce, ciphertext, tag, plaintext);
                }

                CryptographicOperations.ZeroMemory(key);

                var json = Encoding.UTF8.GetString(plaintext);
                CryptographicOperations.ZeroMemory(plaintext);

                return System.Text.Json.JsonSerializer.Deserialize<VaultDatabase>(json);
            }
            catch (AuthenticationTagMismatchException)
            {

                return TryReadPlaintextVaultDb(path);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to decrypt vault database at {Path}, attempting plaintext fallback", path);
                return TryReadPlaintextVaultDb(path);
            }
        }

        private static VaultDatabase? TryReadPlaintextVaultDb(string path)
        {
            try
            {
                var json = File.ReadAllText(path);
                return System.Text.Json.JsonSerializer.Deserialize<VaultDatabase>(json);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to read plaintext vault database at {Path}", path);
                return null;
            }
        }

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

                var dirInfo = new DirectoryInfo(phantomPath);
                dirInfo.Attributes = FileAttributes.Hidden | FileAttributes.System;
            }

            return phantomPath;
        }

        private string GetEncryptedFilename(string vaultName)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(vaultName));

                return Convert.ToBase64String(hash)
                    .Replace("/", "_")
                    .Replace("+", "-")
                    .Replace("=", "");
            }
        }

        #region User Preferences

        private void LoadDefaultPreferences()
        {
            var settings = SettingsService.Load();

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

            RequiresHardwareToken = settings.DefaultRequireHardwareToken;
            UseTotp = settings.DefaultUseTotp;
            UsePasskey = settings.DefaultUsePasskey;
        }

        private void SaveAuthenticationMethodPreference()
        {
            var settings = SettingsService.Load();

            if (UsePasskey)
                settings.LastAuthenticationMethod = "Passkey";
            else if (UseTotp)
                settings.LastAuthenticationMethod = "TOTP";
            else if (RequiresHardwareToken)
                settings.LastAuthenticationMethod = "YubiKey";
            else
                settings.LastAuthenticationMethod = "Password";

            settings.DefaultRequireHardwareToken = RequiresHardwareToken;
            settings.DefaultUseTotp = UseTotp;
            settings.DefaultUsePasskey = UsePasskey;

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

        private static string GenerateSecurePassword(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()-_=+[]{}|;:,.<>?";
            var passwordBytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(passwordBytes);
            }

            var password = new char[length];
            for (int i = 0; i < length; i++)
            {
                password[i] = chars[passwordBytes[i] % chars.Length];
            }

            CryptographicOperations.ZeroMemory(passwordBytes);
            return new string(password);
        }

        #endregion
    }
}

