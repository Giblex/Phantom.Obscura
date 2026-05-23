using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reactive;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using PhantomVault.UI;
using PhantomVault.UI.Services;
using PhantomVault.UI.ViewModels;
using PhantomVault.UI.Views;
using PhantomVault.UI.Views.Dialogs;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels.Settings
{
    public class BackupSettingsViewModel : ReactiveObject
    {
        private string _backupLocation = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "PhantomVault",
            "Backups");
        private string _lastBackupTime = "Never";
        private ObservableCollection<string> _backupHistory = new();
        private bool _isVaultContextAvailable;
        private string _vaultContextStatus = "Open settings from an unlocked vault to create or restore backups.";
        private bool _isRecoveryVaultAvailable;
        private string _recoveryVaultStatus = "Recovery vault backup becomes available when the current unlocked vault can resolve its PhantomRecovery workspace.";
        private string _backupHistoryStatus = "History shown here only reflects backups created during this session.";
        private bool _enableAutomatedBackups;
        private int _selectedFrequency;
        private bool _encryptBackups = true;
        private int _selectedRetention = 2;
        private bool _isInitializing;

        // Issue: shared draft tracker so the overlay-bottom Save button drives
        // Backup persistence too. Baseline captured at LoadPersistedSettings
        // for Discard rollback.
        private readonly SettingsDraftTracker _draft = ((Application.Current as App)?.Services?.GetService(typeof(SettingsDraftTracker)) as SettingsDraftTracker) ?? new SettingsDraftTracker();
        private BackupBaseline _baseline;

        private readonly struct BackupBaseline
        {
            public string BackupLocation { get; init; }
            public bool EnableAutomatedBackups { get; init; }
            public int SelectedFrequency { get; init; }
            public bool EncryptBackups { get; init; }
            public int SelectedRetention { get; init; }
        }

        public bool SupportsScheduledBackups => true;
        public bool SupportsSnapshotManager => true;

        public string BackupLocation
        {
            get => _backupLocation;
            set
            {
                if (!string.Equals(_backupLocation, value, StringComparison.Ordinal))
                {
                    this.RaiseAndSetIfChanged(ref _backupLocation, value);
                    RefreshAvailability();
                }
            }
        }

        public string LastBackupTime
        {
            get => _lastBackupTime;
            set => this.RaiseAndSetIfChanged(ref _lastBackupTime, value);
        }

        public ObservableCollection<string> BackupHistory
        {
            get => _backupHistory;
            set => this.RaiseAndSetIfChanged(ref _backupHistory, value);
        }

        public bool IsVaultContextAvailable
        {
            get => _isVaultContextAvailable;
            private set => this.RaiseAndSetIfChanged(ref _isVaultContextAvailable, value);
        }

        public string VaultContextStatus
        {
            get => _vaultContextStatus;
            private set => this.RaiseAndSetIfChanged(ref _vaultContextStatus, value);
        }

        public bool IsRecoveryVaultAvailable
        {
            get => _isRecoveryVaultAvailable;
            private set => this.RaiseAndSetIfChanged(ref _isRecoveryVaultAvailable, value);
        }

        public string RecoveryVaultStatus
        {
            get => _recoveryVaultStatus;
            private set => this.RaiseAndSetIfChanged(ref _recoveryVaultStatus, value);
        }

        public string BackupHistoryStatus
        {
            get => _backupHistoryStatus;
            private set => this.RaiseAndSetIfChanged(ref _backupHistoryStatus, value);
        }

        public bool EnableAutomatedBackups
        {
            get => _enableAutomatedBackups;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _enableAutomatedBackups, value) && !_isInitializing)
                {
                    PersistBackupSettings();
                    _ = PersistManifestBackupSettingsAsync();
                    RefreshAvailability();
                }
            }
        }

        public int SelectedFrequency
        {
            get => _selectedFrequency;
            set
            {
                var normalizedValue = Math.Clamp(value, 0, 3);
                if (_selectedFrequency != normalizedValue)
                {
                    this.RaiseAndSetIfChanged(ref _selectedFrequency, normalizedValue);
                    if (!_isInitializing)
                    {
                        PersistBackupSettings();
                        RefreshAvailability();
                    }
                }
            }
        }

        public bool EncryptBackups
        {
            get => _encryptBackups;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _encryptBackups, value) && !_isInitializing)
                {
                    PersistBackupSettings();
                    RefreshAvailability();
                }
            }
        }

        public int SelectedRetention
        {
            get => _selectedRetention;
            set
            {
                var normalizedValue = Math.Clamp(value, 0, 4);
                if (_selectedRetention != normalizedValue)
                {
                    this.RaiseAndSetIfChanged(ref _selectedRetention, normalizedValue);
                    if (!_isInitializing)
                    {
                        PersistBackupSettings();
                        _ = PersistManifestBackupSettingsAsync();
                        RefreshAvailability();
                    }
                }
            }
        }

        public string ScheduledBackupStatus
        {
            get
            {
                if (!EnableAutomatedBackups)
                {
                    return "Automated backups are disabled.";
                }

                var lastRun = SettingsService.Load().LastAutomatedBackupUtc;
                var cadence = SelectedFrequency switch
                {
                    0 => "daily",
                    1 => "weekly",
                    2 => "monthly",
                    3 => "on every vault close",
                    _ => "daily"
                };

                if (lastRun is null)
                {
                    return $"Automated backups are enabled and will run {cadence}.";
                }

                return $"Automated backups are enabled and will run {cadence}. Last automated backup: {lastRun.Value.LocalDateTime:yyyy-MM-dd HH:mm:ss}.";
            }
        }

        public ICommand BrowseBackupLocationCommand { get; }
        public ICommand BackupNowCommand { get; }
        public ICommand RestoreFromBackupCommand { get; }
        public ICommand OpenBackupFolderCommand { get; }
        public ICommand ManageSnapshotsCommand { get; }
        public ICommand BackupRecoveryVaultNowCommand { get; }
        public ICommand RestoreRecoveryVaultCommand { get; }

        private readonly SecureBackupManager _secureBackupManager;
        private readonly BackupService _backupService;
        private readonly DialogService _dialogService;
        private readonly Window? _owner;
        private readonly Func<(string manifestPath, string? passphrase, string? keyfilePath)?> _manifestContextResolver;
        private readonly RecoveryVaultPathResolver _recoveryVaultPathResolver;

        public BackupSettingsViewModel(
            SecureBackupManager? secureBackupManager = null,
            DialogService? dialogService = null,
            Window? owner = null,
            Func<(string manifestPath, string? passphrase, string? keyfilePath)?>? manifestContextResolver = null)
        {
            _secureBackupManager = secureBackupManager ?? ((Application.Current as App)?.Services?.GetService(typeof(SecureBackupManager)) as SecureBackupManager)!;
            _backupService =
                ((Application.Current as App)?.Services?.GetService(typeof(BackupService)) as BackupService)
                ?? CreateBackupService();
            _dialogService = dialogService ?? new DialogService();
            _owner = owner;
            _manifestContextResolver = manifestContextResolver ?? (() => null);
            _recoveryVaultPathResolver =
                ((Application.Current as App)?.Services?.GetService(typeof(RecoveryVaultPathResolver)) as RecoveryVaultPathResolver)
                ?? new RecoveryVaultPathResolver(new UsbArtifactProtectionService(new EncryptionService()));

            BrowseBackupLocationCommand = ReactiveCommand.CreateFromTask(BrowseBackupLocation);
            BackupNowCommand = ReactiveCommand.CreateFromTask(BackupNow);
            RestoreFromBackupCommand = ReactiveCommand.CreateFromTask(RestoreFromBackup);
            OpenBackupFolderCommand = ReactiveCommand.Create(OpenBackupFolder);
            ManageSnapshotsCommand = ReactiveCommand.CreateFromTask(ManageSnapshotsAsync);
            BackupRecoveryVaultNowCommand = ReactiveCommand.CreateFromTask(BackupRecoveryVaultNow);
            RestoreRecoveryVaultCommand = ReactiveCommand.CreateFromTask(RestoreRecoveryVault);

            LoadPersistedSettings();
            RefreshBackupHistory();
            RefreshAvailability();
        }

        private async Task BrowseBackupLocation()
        {
            try
            {
                var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                if (topLevel == null) return;

                var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Backup Location",
                    AllowMultiple = false
                });

                if (folders.Count > 0)
                {
                    BackupLocation = folders[0].Path.LocalPath;
                    PersistBackupSettings();
                    RefreshBackupHistory();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error selecting backup location: {ex.Message}");
            }
        }

        private async Task BackupNow()
        {
            try
            {
                var context = GetManifestContext();
                if (context == null)
                {
                    await _dialogService.ShowWarningAsync("Backup Unavailable", "Vault manifest context not available. Open settings from an unlocked vault.", _owner);
                    return;
                }

                var (manifestPath, passphrase, keyfilePath) = context.Value;
                Directory.CreateDirectory(BackupLocation);
                string outputFile;
                if (EncryptBackups)
                {
                    var credentials = await BackupCredentialsDialog.PromptAsync(_owner);
                    if (credentials == null)
                    {
                        return;
                    }

                    outputFile = Path.Combine(BackupLocation, $"vault-backup-{DateTime.UtcNow:yyyyMMddHHmmss}.pvbkp");
                    var policyPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Policies", "base_policy.signed.json");
                    var certPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Policies", "obscura_root.crt");

                    _secureBackupManager.CreateBackup(
                        manifestPath,
                        policyPath,
                        certPath,
                        credentials.Passphrase ?? passphrase ?? string.Empty,
                        string.IsNullOrWhiteSpace(credentials.KeyfilePath) ? keyfilePath : credentials.KeyfilePath,
                        credentials.Pin,
                        outputFile);
                }
                else
                {
                    outputFile = await _backupService.CreateBackupAsync(manifestPath, passphrase, keyfilePath, BackupLocation);
                }

                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastBackupTime = timestamp;

                if (BackupHistory[0] == "No backups found")
                {
                    BackupHistory.Clear();
                }

                BackupHistory.Insert(0, $"Backup created: {timestamp}");
                BackupAutomationService.PruneBackupCount(BackupLocation, SelectedRetention);
                RefreshBackupHistory();
                await _dialogService.ShowInfoAsync("Backup Created", $"Backup saved to:\n{outputFile}", _owner);
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Backup Failed", ex.Message, _owner);
            }
        }

        private async Task RestoreFromBackup()
        {
            try
            {
                var context = GetManifestContext();
                if (context == null)
                {
                    await _dialogService.ShowWarningAsync("Restore Unavailable", "Vault manifest context not available. Open settings from an unlocked vault.", _owner);
                    return;
                }

                var top = _owner ?? (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (top == null || top.StorageProvider == null)
                    return;

                var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Backup File to Restore",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("PhantomVault Backup") { Patterns = new[] { "*.pvbkp", "*.pvbackup", "*.backup" } },
                        new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                    }
                });

                if (files.Count == 0)
                    return;

                var (manifestPath, passphrase, keyfilePath) = context.Value;
                var selectedPath = files[0].Path.LocalPath;

                if (selectedPath.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                {
                    await _backupService.RestoreBackupAsync(selectedPath, manifestPath, passphrase, keyfilePath);
                }
                else
                {
                    var credentials = await BackupCredentialsDialog.PromptAsync(_owner);
                    if (credentials == null)
                        return;

                    var policyPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Policies", "base_policy.signed.json");
                    var certPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Policies", "obscura_root.crt");

                    _secureBackupManager.RestoreBackup(
                        selectedPath,
                        manifestPath,
                        policyPath,
                        certPath,
                        credentials.Passphrase ?? passphrase ?? string.Empty,
                        string.IsNullOrWhiteSpace(credentials.KeyfilePath) ? keyfilePath : credentials.KeyfilePath,
                        credentials.Pin);
                }

                await _dialogService.ShowInfoAsync("Restore Complete", "Backup restored successfully.", _owner);
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Restore Failed", ex.Message, _owner);
            }
        }

        private void OpenBackupFolder()
        {
            try
            {
                if (System.IO.Directory.Exists(BackupLocation))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = BackupLocation,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Backup folder does not exist");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening backup folder: {ex.Message}");
            }
        }

        private async Task ManageSnapshotsAsync()
        {
            var window = new BackupSnapshotsWindow
            {
                DataContext = new BackupSnapshotsViewModel(
                    BackupLocation,
                    _backupService,
                    _owner,
                    _manifestContextResolver)
            };

            if (_owner != null)
            {
                await window.ShowDialog(_owner);
            }
            else
            {
                window.Show();
            }

            RefreshBackupHistory();
        }

        /// <summary>
        /// Backs up the PhantomRecovery vault containing recovery artifacts
        /// </summary>
        private async Task BackupRecoveryVaultNow()
        {
            try
            {
                var context = GetManifestContext();
                if (context == null)
                {
                    await _dialogService.ShowWarningAsync("Backup Unavailable",
                        "Recovery container context is only available from an unlocked vault.", _owner);
                    return;
                }

                var (manifestPath, passphrase, keyfilePath) = context.Value;
                var manifest = LoadManifest(manifestPath, passphrase, keyfilePath);
                var resolution = _recoveryVaultPathResolver.Resolve(manifestPath, manifest, passphrase, keyfilePath);
                if (!resolution.Resolved)
                {
                    await _dialogService.ShowWarningAsync("Recovery Vault Not Found",
                        resolution.Message, _owner);
                    RefreshAvailability();
                    return;
                }

                if (!resolution.HasHeader)
                {
                    await _dialogService.ShowWarningAsync("Recovery Vault Not Initialized",
                        "The PhantomRecovery workspace is resolved, but the vault has not been initialized yet. Open PhantomRecovery once before backing it up.", _owner);
                    return;
                }

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupFileName = $"PhantomRecovery_Backup_{timestamp}.pvbak";
                var backupPath = Path.Combine(BackupLocation, backupFileName);

                Directory.CreateDirectory(BackupLocation);

                await Task.Run(() =>
                {
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }

                    ZipFile.CreateFromDirectory(resolution.VaultPath, backupPath, CompressionLevel.Optimal, includeBaseDirectory: false);
                });

                RefreshAvailability();
                await _dialogService.ShowSuccessAsync("Backup Complete", 
                    $"Recovery vault backed up successfully to:\n{backupPath}", _owner);
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Backup Failed", 
                    $"Failed to backup recovery vault:\n{ex.Message}", _owner);
            }
        }

        /// <summary>
        /// Restores the PhantomRecovery vault from a backup
        /// </summary>
        private async Task RestoreRecoveryVault()
        {
            try
            {
                var context = GetManifestContext();
                if (context == null)
                {
                    await _dialogService.ShowWarningAsync("Restore Unavailable",
                        "Recovery container context is only available from an unlocked vault.", _owner);
                    return;
                }

                var storageProvider = (_owner as Window)?.StorageProvider;
                if (storageProvider == null)
                    return;

                var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Recovery Vault Backup",
                    AllowMultiple = false,
                    FileTypeFilter = new List<FilePickerFileType>
                    {
                        new("PhantomVault Backup") { Patterns = new[] { "*.pvbak" } },
                        new("All Files") { Patterns = new[] { "*.*" } }
                    }
                });

                if (files.Count == 0)
                    return;

                // Confirm restoration
                var confirm = await _dialogService.ShowConfirmationAsync(
                    "Restore Recovery Vault",
                    "This will replace your current recovery vault with the backup. Continue?",
                    _owner);

                if (!confirm)
                    return;

                // Prompt for backup credentials
                var credentials = await BackupCredentialsDialog.PromptAsync(_owner);
                if (credentials == null)
                    return;

                var (manifestPath, passphrase, keyfilePath) = context.Value;
                var manifest = LoadManifest(manifestPath, passphrase, keyfilePath);
                var resolution = _recoveryVaultPathResolver.Resolve(manifestPath, manifest, passphrase, keyfilePath);
                if (!resolution.Resolved)
                {
                    await _dialogService.ShowWarningAsync("Restore Unavailable",
                        resolution.Message, _owner);
                    return;
                }

                await Task.Run(() =>
                {
                    var targetPath = _recoveryVaultPathResolver.EnsureDirectory(resolution.VaultPath);
                    var tempExtractPath = Path.Combine(Path.GetTempPath(), "PhantomRecoveryRestore", Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempExtractPath);

                    try
                    {
                        ZipFile.ExtractToDirectory(files[0].Path.LocalPath, tempExtractPath);
                        if (!File.Exists(Path.Combine(tempExtractPath, "header.json")))
                        {
                            throw new InvalidOperationException("Selected backup does not contain a PhantomRecovery vault header.");
                        }

                        foreach (var file in Directory.GetFiles(targetPath))
                        {
                            File.Delete(file);
                        }

                        foreach (var directory in Directory.GetDirectories(targetPath))
                        {
                            Directory.Delete(directory, recursive: true);
                        }

                        CopyDirectory(tempExtractPath, targetPath);
                    }
                    finally
                    {
                        if (Directory.Exists(tempExtractPath))
                        {
                            Directory.Delete(tempExtractPath, recursive: true);
                        }
                    }
                });

                RefreshAvailability();
                await _dialogService.ShowSuccessAsync("Restore Complete", 
                    "Recovery vault restored successfully. Please restart PhantomRecovery if it is currently open.", _owner);
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Restore Failed", 
                    $"Failed to restore recovery vault:\n{ex.Message}", _owner);
            }
        }

        private static VaultManifest LoadManifest(string manifestPath, string? passphrase, string? keyfilePath)
        {
            var encryptionService = new EncryptionService();
            var containerService = new PhantomContainerService(encryptionService);
            var manifestService = new ManifestService(encryptionService, containerService);
            return manifestService.ReadManifest(manifestPath, passphrase, keyfilePath);
        }

        private (string manifestPath, string? passphrase, string? keyfilePath)? GetManifestContext()
        {
            var context = _manifestContextResolver();
            RefreshAvailability(context);
            return context;
        }

        private void RefreshAvailability((string manifestPath, string? passphrase, string? keyfilePath)? context = null)
        {
            context ??= _manifestContextResolver();

            if (context == null)
            {
                IsVaultContextAvailable = false;
                VaultContextStatus = "Open this page from an unlocked vault to create, restore, schedule, or verify encrypted backups.";
                IsRecoveryVaultAvailable = false;
                RecoveryVaultStatus = "Recovery vault backup is unavailable until an unlocked vault can resolve its PhantomRecovery workspace.";
                BackupHistoryStatus = "History shown here only reflects backups created during this app session.";
                this.RaisePropertyChanged(nameof(ScheduledBackupStatus));
                return;
            }

            IsVaultContextAvailable = true;
            VaultContextStatus = "Manual backup, restore, automated backup scheduling, and snapshot management are available for the currently unlocked vault.";
            BackupHistoryStatus = $"Backups are written to {BackupLocation}. History reflects backup files currently present in that folder.";

            try
            {
                var (manifestPath, passphrase, keyfilePath) = context.Value;
                var manifest = LoadManifest(manifestPath, passphrase, keyfilePath);
                var resolution = _recoveryVaultPathResolver.Resolve(manifestPath, manifest, passphrase, keyfilePath);

                if (resolution.Resolved && resolution.HasHeader)
                {
                    IsRecoveryVaultAvailable = true;
                    RecoveryVaultStatus = $"Recovery vault backup and restore are available for the current vault at {resolution.VaultPath}.";
                }
                else if (resolution.Resolved)
                {
                    IsRecoveryVaultAvailable = true;
                    RecoveryVaultStatus = resolution.Message;
                }
                else
                {
                    IsRecoveryVaultAvailable = false;
                    RecoveryVaultStatus = resolution.Message;
                }
            }
            catch (Exception ex)
            {
                IsRecoveryVaultAvailable = false;
                RecoveryVaultStatus = $"Recovery-container availability could not be verified: {ex.Message}";
            }

            this.RaisePropertyChanged(nameof(ScheduledBackupStatus));
        }

        private static BackupService CreateBackupService()
        {
            var encryptionService = new EncryptionService();
            return new BackupService(encryptionService, new PhantomContainerService(encryptionService));
        }

        private static void CopyDirectory(string sourcePath, string destinationPath)
        {
            Directory.CreateDirectory(destinationPath);

            foreach (var directory in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourcePath, directory);
                Directory.CreateDirectory(Path.Combine(destinationPath, relativePath));
            }

            foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourcePath, file);
                var destinationFile = Path.Combine(destinationPath, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
                File.Copy(file, destinationFile, overwrite: true);
            }
        }

        private void LoadPersistedSettings()
        {
            _isInitializing = true;
            try
            {
                var settings = SettingsService.Load();
                if (!string.IsNullOrWhiteSpace(settings.BackupLocation))
                {
                    _backupLocation = settings.BackupLocation;
                }

                _enableAutomatedBackups = settings.BackupAutomationEnabled;
                _selectedFrequency = Math.Clamp(settings.BackupFrequencyMode, 0, 3);
                _encryptBackups = settings.BackupUseEncryption;
                _selectedRetention = Math.Clamp(settings.BackupRetentionCount, 0, 4);

                // Capture baseline AFTER load so Discard can roll back to disk
                // values (not whatever the field defaults happened to be).
                _baseline = SnapshotCurrent();
            }
            finally
            {
                _isInitializing = false;
            }
        }

        // Issue: route the existing PersistBackupSettings helper through the
        // shared tracker. All 5 call sites already funnel here, so a single
        // edit migrates the whole tab. Re-staging replaces the prior pair so
        // a burst of changes still produces a single commit/discard.
        private void PersistBackupSettings()
        {
            this.RaisePropertyChanged(nameof(ScheduledBackupStatus));

            if (_isInitializing) return;
            if (MatchesBaseline())
            {
                _draft.ClearKey("Backup.All");
                return;
            }
            var staged = SnapshotCurrent();
            var baseline = _baseline;
            _draft.Stage(
                key: "Backup.All",
                commit: () =>
                {
                    SettingsService.Update(settings =>
                    {
                        settings.BackupAutomationEnabled = staged.EnableAutomatedBackups;
                        settings.BackupFrequencyMode = staged.SelectedFrequency;
                        settings.BackupLocation = staged.BackupLocation;
                        settings.BackupUseEncryption = staged.EncryptBackups;
                        settings.BackupRetentionCount = staged.SelectedRetention;
                    });
                    _baseline = staged;
                },
                discard: () =>
                {
                    _isInitializing = true;
                    try
                    {
                        BackupLocation = baseline.BackupLocation;
                        EnableAutomatedBackups = baseline.EnableAutomatedBackups;
                        SelectedFrequency = baseline.SelectedFrequency;
                        EncryptBackups = baseline.EncryptBackups;
                        SelectedRetention = baseline.SelectedRetention;
                    }
                    finally
                    {
                        _isInitializing = false;
                    }
                    this.RaisePropertyChanged(nameof(ScheduledBackupStatus));
                });
        }

        private BackupBaseline SnapshotCurrent() => new BackupBaseline
        {
            BackupLocation = BackupLocation,
            EnableAutomatedBackups = EnableAutomatedBackups,
            SelectedFrequency = SelectedFrequency,
            EncryptBackups = EncryptBackups,
            SelectedRetention = SelectedRetention,
        };

        private bool MatchesBaseline()
        {
            var b = _baseline;
            return string.Equals(BackupLocation ?? string.Empty, b.BackupLocation ?? string.Empty, StringComparison.Ordinal)
                && EnableAutomatedBackups == b.EnableAutomatedBackups
                && SelectedFrequency == b.SelectedFrequency
                && EncryptBackups == b.EncryptBackups
                && SelectedRetention == b.SelectedRetention;
        }

        private async Task PersistManifestBackupSettingsAsync()
        {
            var context = _manifestContextResolver();
            if (context == null)
            {
                return;
            }

            try
            {
                var services = (Application.Current as App)?.Services;
                var manifestService = services?.GetService<ManifestService>();
                if (manifestService == null)
                {
                    return;
                }

                var (manifestPath, passphrase, keyfilePath) = context.Value;
                await Task.Run(() =>
                {
                    var manifest = manifestService.ReadManifest(manifestPath, passphrase, keyfilePath);
                    manifest.AutoBackupEnabled = EnableAutomatedBackups;
                    manifest.BackupRetentionDays = SelectedRetention switch
                    {
                        0 => 7,
                        1 => 14,
                        2 => 30,
                        3 => 90,
                        _ => 365
                    };
                    manifestService.WriteManifest(manifest, manifestPath, passphrase, keyfilePath);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to persist backup settings to manifest: {ex.Message}");
            }
        }

        private void RefreshBackupHistory()
        {
            BackupHistory.Clear();

            if (Directory.Exists(BackupLocation))
            {
                var historyEntries = Directory.GetFiles(BackupLocation)
                    .Where(path =>
                        path.EndsWith(".pvbkp", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".pvbackup", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".backup", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".pvbak", StringComparison.OrdinalIgnoreCase))
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.CreationTimeUtc)
                    .Take(20);

                foreach (var file in historyEntries)
                {
                    BackupHistory.Add($"{file.CreationTime:yyyy-MM-dd HH:mm:ss}  {file.Name}");
                }
            }

            if (BackupHistory.Count == 0)
            {
                BackupHistory.Add("No backups found");
            }
        }
    }
}
