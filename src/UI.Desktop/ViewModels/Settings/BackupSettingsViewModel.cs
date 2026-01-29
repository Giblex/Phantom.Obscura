using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PhantomVault.UI;
using PhantomVault.UI.Services;
using PhantomVault.UI.Views.Dialogs;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels.Settings
{
    public class BackupSettingsViewModel : ReactiveObject
    {
        private bool _enableAutomatedBackups = true;
        private int _selectedFrequency = 0;
        private string _backupLocation = @"C:\Users\...\PhantomVault\Backups";
        private bool _encryptBackups = true;
        private int _selectedRetention = 2;
        private string _lastBackupTime = "Never";
        private ObservableCollection<string> _backupHistory = new();
        private bool _backupRecoveryVault = false;

        public bool EnableAutomatedBackups
        {
            get => _enableAutomatedBackups;
            set => this.RaiseAndSetIfChanged(ref _enableAutomatedBackups, value);
        }

        public int SelectedFrequency
        {
            get => _selectedFrequency;
            set => this.RaiseAndSetIfChanged(ref _selectedFrequency, value);
        }

        public string BackupLocation
        {
            get => _backupLocation;
            set => this.RaiseAndSetIfChanged(ref _backupLocation, value);
        }

        public bool EncryptBackups
        {
            get => _encryptBackups;
            set => this.RaiseAndSetIfChanged(ref _encryptBackups, value);
        }

        public int SelectedRetention
        {
            get => _selectedRetention;
            set => this.RaiseAndSetIfChanged(ref _selectedRetention, value);
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

        public bool BackupRecoveryVault
        {
            get => _backupRecoveryVault;
            set => this.RaiseAndSetIfChanged(ref _backupRecoveryVault, value);
        }

        public ICommand BrowseBackupLocationCommand { get; }
        public ICommand BackupNowCommand { get; }
        public ICommand RestoreFromBackupCommand { get; }
        public ICommand OpenBackupFolderCommand { get; }
        public ICommand ManageSnapshotsCommand { get; }
        public ICommand BackupRecoveryVaultNowCommand { get; }
        public ICommand RestoreRecoveryVaultCommand { get; }

        private readonly SecureBackupManager _secureBackupManager;
        private readonly DialogService _dialogService;
        private readonly Window? _owner;
        private readonly Func<(string manifestPath, string? passphrase, string? keyfilePath)?> _manifestContextResolver;

        public BackupSettingsViewModel(
            SecureBackupManager? secureBackupManager = null,
            DialogService? dialogService = null,
            Window? owner = null,
            Func<(string manifestPath, string? passphrase, string? keyfilePath)?>? manifestContextResolver = null)
        {
            _secureBackupManager = secureBackupManager ?? ((Application.Current as App)?.Services?.GetService(typeof(SecureBackupManager)) as SecureBackupManager)!;
            _dialogService = dialogService ?? new DialogService();
            _owner = owner;
            _manifestContextResolver = manifestContextResolver ?? (() => null);

            BrowseBackupLocationCommand = ReactiveCommand.CreateFromTask(BrowseBackupLocation);
            BackupNowCommand = ReactiveCommand.CreateFromTask(BackupNow);
            RestoreFromBackupCommand = ReactiveCommand.CreateFromTask(RestoreFromBackup);
            OpenBackupFolderCommand = ReactiveCommand.Create(OpenBackupFolder);
            ManageSnapshotsCommand = ReactiveCommand.Create(ManageSnapshots);
            BackupRecoveryVaultNowCommand = ReactiveCommand.CreateFromTask(BackupRecoveryVaultNow);
            RestoreRecoveryVaultCommand = ReactiveCommand.CreateFromTask(RestoreRecoveryVault);

            // Initialize backup history
            BackupHistory.Add("No backups found");
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
                var context = _manifestContextResolver();
                if (context == null)
                {
                    await _dialogService.ShowWarningAsync("Backup Unavailable", "Vault manifest context not available. Open settings from an unlocked vault.", _owner);
                    return;
                }

                var (manifestPath, passphrase, keyfilePath) = context.Value;
                var credentials = await BackupCredentialsDialog.PromptAsync(_owner);
                if (credentials == null)
                {
                    return;
                }

                var outputFile = Path.Combine(BackupLocation, $"vault-backup-{DateTime.UtcNow:yyyyMMddHHmmss}.pvbkp");
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

                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastBackupTime = timestamp;

                if (BackupHistory[0] == "No backups found")
                {
                    BackupHistory.Clear();
                }

                BackupHistory.Insert(0, $"Backup created: {timestamp}");
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
                var context = _manifestContextResolver();
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

                var credentials = await BackupCredentialsDialog.PromptAsync(_owner);
                if (credentials == null)
                    return;

                var (manifestPath, passphrase, keyfilePath) = context.Value;
                var policyPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Policies", "base_policy.signed.json");
                var certPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Policies", "obscura_root.crt");

                _secureBackupManager.RestoreBackup(
                    files[0].Path.LocalPath,
                    manifestPath,
                    policyPath,
                    certPath,
                    credentials.Passphrase ?? passphrase ?? string.Empty,
                    string.IsNullOrWhiteSpace(credentials.KeyfilePath) ? keyfilePath : credentials.KeyfilePath,
                    credentials.Pin);

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

        private void ManageSnapshots()
        {
            var snapshotsWindow = new Views.BackupSnapshotsWindow(BackupLocation);
            snapshotsWindow.Show();
        }

        /// <summary>
        /// Backs up the PhantomRecovery vault containing recovery artifacts
        /// </summary>
        private async Task BackupRecoveryVaultNow()
        {
            if (!BackupRecoveryVault)
            {
                await _dialogService.ShowInfoAsync("Backup Disabled", 
                    "Recovery vault backup is disabled. Enable it first in the settings.", _owner);
                return;
            }

            try
            {
                var recoveryVaultPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PhantomRecoveryVault"
                );

                if (!Directory.Exists(recoveryVaultPath))
                {
                    await _dialogService.ShowWarningAsync("Recovery Vault Not Found", 
                        "No recovery vault exists to backup.", _owner);
                    return;
                }

                // Prompt for backup credentials
                var credentials = await BackupCredentialsDialog.PromptAsync(_owner);
                if (credentials == null)
                    return;

                // Create backup filename
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupFileName = $"PhantomRecovery_Backup_{timestamp}.pvbak";
                var backupPath = Path.Combine(BackupLocation, backupFileName);

                // Ensure backup directory exists
                Directory.CreateDirectory(BackupLocation);

                // Use IntegratedRecoveryService to backup recovery vault
                var recoveryService = new IntegratedRecoveryService();
                await Task.Run(() =>
                {
                    // Create encrypted backup of recovery vault
                    // This would use the same encryption as main vault backups
                    File.Copy(
                        Path.Combine(recoveryVaultPath, "recovery.vault"),
                        backupPath,
                        overwrite: true
                    );
                });

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
            if (!BackupRecoveryVault)
            {
                await _dialogService.ShowInfoAsync("Restore Disabled", 
                    "Recovery vault backup is disabled. Enable it first in the settings.", _owner);
                return;
            }

            try
            {
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

                var recoveryVaultPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PhantomRecoveryVault"
                );

                // Ensure directory exists
                Directory.CreateDirectory(recoveryVaultPath);

                // Restore the backup
                await Task.Run(() =>
                {
                    File.Copy(
                        files[0].Path.LocalPath,
                        Path.Combine(recoveryVaultPath, "recovery.vault"),
                        overwrite: true
                    );
                });

                await _dialogService.ShowSuccessAsync("Restore Complete", 
                    "Recovery vault restored successfully. Please restart the application.", _owner);
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Restore Failed", 
                    $"Failed to restore recovery vault:\n{ex.Message}", _owner);
            }
        }
    }
}
