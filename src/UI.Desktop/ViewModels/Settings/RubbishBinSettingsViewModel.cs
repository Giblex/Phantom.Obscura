using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Linq;
using System.Text.Json;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PhantomVault.Core.Services;
using PhantomVault.UI.Models;
using PhantomVault.UI.Services;
using PhantomVault.UI.Views.Dialogs;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels.Settings
{
    public class RubbishBinSettingsViewModel : ReactiveObject
    {
        private int _selectedErasureMethod = 2; // DoD 5220.22-M
        private string _methodDescription = "US Department of Defense standard. Overwrites data 7 times with specific patterns. Provides high security with reasonable performance. Recommended for most users.";
        private bool _enableRecovery = true;
        private int _selectedRetentionPeriod = 2; // 30 days
        private bool _enableDuplicateDetection = false;
        private bool _autoDeleteDuplicates = false;
        private int _selectedScanFrequency = 1; // Weekly
        private int _itemsInBin = 0;
        private string _totalSize = "0 KB";
        private string _oldestItem = "N/A";
        private bool _autoEmptyOnClose = false;
        private bool _promptBeforeDeletion = true;
        private string _recoveryRetentionStatus = "Shadow snapshots can be created before you empty the bin in this app session, and retention settings are applied to the secure rubbish bin service.";
        private string _duplicateAutomationStatus = "Manual duplicate scans are available for secure-bin records, and duplicate preferences can be configured from this panel.";
        private string _cleanupAutomationStatus = "Prompt-before-delete applies in this app session, and auto-empty on vault close can be configured here.";
        private readonly DialogService _dialogService;
        private readonly Func<string[]?> _trashItemsProvider;
        private readonly ShadowTrashService _shadowTrashService;
        private readonly SecureTrashService _secureTrashService;
        private readonly Window? _owner;

        // Issue #29: shared draft tracker — PersistSettings now stages a single
        // 'RubbishBin.All' entry; commits/discards drive disk + live config.
        private readonly SettingsDraftTracker _draft;
        private RubbishBinBaseline _baseline;
        private bool _suppressStage;

        private readonly struct RubbishBinBaseline
        {
            public int SelectedErasureMethod { get; init; }
            public bool EnableRecovery { get; init; }
            public int SelectedRetentionPeriod { get; init; }
            public bool EnableDuplicateDetection { get; init; }
            public bool AutoDeleteDuplicates { get; init; }
            public int SelectedScanFrequency { get; init; }
            public bool AutoEmptyOnClose { get; init; }
            public bool PromptBeforeDeletion { get; init; }
        }

        public int SelectedErasureMethod
        {
            get => _selectedErasureMethod;
            set
            {
                if (_selectedErasureMethod != value)
                {
                    this.RaiseAndSetIfChanged(ref _selectedErasureMethod, value);
                    UpdateMethodDescription();
                    PersistSettings();
                    ApplySecureTrashConfiguration();
                }
            }
        }

        public string MethodDescription
        {
            get => _methodDescription;
            set => this.RaiseAndSetIfChanged(ref _methodDescription, value);
        }

        public bool EnableRecovery
        {
            get => _enableRecovery;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _enableRecovery, value))
                {
                    PersistSettings();
                    ApplySecureTrashConfiguration();
                    UpdateStatusMessages();
                }
            }
        }

        public int SelectedRetentionPeriod
        {
            get => _selectedRetentionPeriod;
            set
            {
                if (_selectedRetentionPeriod != value)
                {
                    this.RaiseAndSetIfChanged(ref _selectedRetentionPeriod, value);
                    PersistSettings();
                    ApplySecureTrashConfiguration();
                    UpdateStatusMessages();
                }
            }
        }

        public bool EnableDuplicateDetection
        {
            get => _enableDuplicateDetection;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _enableDuplicateDetection, value))
                {
                    PersistSettings();
                    UpdateStatusMessages();
                }
            }
        }

        public bool AutoDeleteDuplicates
        {
            get => _autoDeleteDuplicates;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _autoDeleteDuplicates, value))
                {
                    PersistSettings();
                    UpdateStatusMessages();
                }
            }
        }

        public int SelectedScanFrequency
        {
            get => _selectedScanFrequency;
            set
            {
                if (_selectedScanFrequency != value)
                {
                    this.RaiseAndSetIfChanged(ref _selectedScanFrequency, value);
                    PersistSettings();
                    UpdateStatusMessages();
                }
            }
        }

        public int ItemsInBin
        {
            get => _itemsInBin;
            set => this.RaiseAndSetIfChanged(ref _itemsInBin, value);
        }

        public string TotalSize
        {
            get => _totalSize;
            set => this.RaiseAndSetIfChanged(ref _totalSize, value);
        }

        public string OldestItem
        {
            get => _oldestItem;
            set => this.RaiseAndSetIfChanged(ref _oldestItem, value);
        }

        public bool AutoEmptyOnClose
        {
            get => _autoEmptyOnClose;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _autoEmptyOnClose, value))
                {
                    PersistSettings();
                    UpdateStatusMessages();
                }
            }
        }

        public bool PromptBeforeDeletion
        {
            get => _promptBeforeDeletion;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _promptBeforeDeletion, value))
                {
                    PersistSettings();
                    UpdateStatusMessages();
                }
            }
        }

        public string RecoveryRetentionStatus
        {
            get => _recoveryRetentionStatus;
            set => this.RaiseAndSetIfChanged(ref _recoveryRetentionStatus, value);
        }

        public string DuplicateAutomationStatus
        {
            get => _duplicateAutomationStatus;
            set => this.RaiseAndSetIfChanged(ref _duplicateAutomationStatus, value);
        }

        public string CleanupAutomationStatus
        {
            get => _cleanupAutomationStatus;
            set => this.RaiseAndSetIfChanged(ref _cleanupAutomationStatus, value);
        }

        public ICommand ScanForDuplicatesCommand { get; }
        public ICommand ViewDeletedItemsCommand { get; }
        public ICommand EmptyBinCommand { get; }
        public ICommand RestoreShadowSnapshotCommand { get; }

        public RubbishBinSettingsViewModel(
            DialogService? dialogService = null,
            ShadowTrashService? shadowTrashService = null,
            SecureTrashService? secureTrashService = null,
            Window? owner = null,
            Func<string[]?>? trashItemsProvider = null)
        {
            _dialogService = dialogService ?? new DialogService();
            _shadowTrashService = shadowTrashService ?? ((Application.Current as App)?.Services?.GetService(typeof(ShadowTrashService)) as ShadowTrashService)!;
            _secureTrashService = secureTrashService ?? ((Application.Current as App)?.Services?.GetService(typeof(SecureTrashService)) as SecureTrashService)!;
            _owner = owner;
            _trashItemsProvider = trashItemsProvider ?? (() => Array.Empty<string>());
            _draft = ((Application.Current as App)?.Services?.GetService(typeof(SettingsDraftTracker)) as SettingsDraftTracker)
                ?? new SettingsDraftTracker();

            ScanForDuplicatesCommand = ReactiveCommand.CreateFromTask(ScanForDuplicates);
            ViewDeletedItemsCommand = ReactiveCommand.CreateFromTask(ViewDeletedItemsAsync);
            EmptyBinCommand = ReactiveCommand.CreateFromTask(EmptyBin);
            RestoreShadowSnapshotCommand = ReactiveCommand.CreateFromTask(RestoreShadowSnapshotAsync);

            _suppressStage = true;
            LoadSettings();
            _baseline = SnapshotCurrent();
            _suppressStage = false;
            UpdateMethodDescription();
            ApplySecureTrashConfiguration();
            UpdateStatusMessages();
            RefreshStats();
        }

        private void UpdateMethodDescription()
        {
            MethodDescription = _selectedErasureMethod switch
            {
                0 => "Simple (1-pass)\n\nFast, single overwrite with zeros. Suitable for non-sensitive data or when speed is a priority. Not recommended for highly sensitive information.",
                1 => "Standard (3-pass)\n\nBalanced security and speed. Overwrites data 3 times with different patterns. Good compromise for most use cases.",
                2 => "DoD 5220.22-M (7-pass)\n\nUS Department of Defense standard. Overwrites data 7 times with specific patterns. Provides high security with reasonable performance. Recommended for most users.",
                3 => "Gutmann (35-pass)\n\nMaximum security, very slow. Designed for older hard drives. Overwrites data 35 times. Overkill for modern drives but provides peace of mind for extremely sensitive data.",
                4 => "Enhanced (1000+ passes)\n\nExtreme paranoia mode. Overwrites data over 1000 times. Extremely slow and generally unnecessary. Only use for the most sensitive data when time is not a constraint.",
                _ => "Unknown method"
            };
        }

        private void LoadSettings()
        {
            var settings = SettingsService.Load();
            _enableRecovery = settings.SecureTrashCreateSnapshotBeforePurge;
            _selectedRetentionPeriod = MapRetentionDaysToIndex(settings.SecureTrashRetentionDays);
            _enableDuplicateDetection = settings.SecureTrashDuplicateDetectionEnabled;
            _autoDeleteDuplicates = settings.SecureTrashAutoDeleteDuplicates;
            _selectedScanFrequency = Math.Clamp(settings.SecureTrashDuplicateScanFrequency, 0, 3);
            _autoEmptyOnClose = settings.SecureTrashAutoEmptyOnClose;
            _promptBeforeDeletion = settings.SecureTrashPromptBeforeDeletion;
            _selectedErasureMethod = Math.Clamp(settings.SecureTrashErasureMethod, 0, 4);
        }

        private void PersistSettings()
        {
            // Issue #29: stage instead of writing immediately. Setters still
            // call this on every change; re-staging replaces the prior pair
            // under the 'RubbishBin.All' key so a burst of toggles still
            // results in a single commit/discard pair.
            if (_suppressStage) return;
            if (MatchesBaseline())
            {
                _draft.ClearKey("RubbishBin.All");
                return;
            }
            var staged = SnapshotCurrent();
            var baseline = _baseline;
            _draft.Stage(
                key: "RubbishBin.All",
                commit: () =>
                {
                    SettingsService.Update(settings =>
                    {
                        settings.SecureTrashCreateSnapshotBeforePurge = staged.EnableRecovery;
                        settings.SecureTrashRetentionDays = MapRetentionIndexToDays(staged.SelectedRetentionPeriod);
                        settings.SecureTrashDuplicateDetectionEnabled = staged.EnableDuplicateDetection;
                        settings.SecureTrashAutoDeleteDuplicates = staged.AutoDeleteDuplicates;
                        settings.SecureTrashDuplicateScanFrequency = staged.SelectedScanFrequency;
                        settings.SecureTrashAutoEmptyOnClose = staged.AutoEmptyOnClose;
                        settings.SecureTrashPromptBeforeDeletion = staged.PromptBeforeDeletion;
                        settings.SecureTrashErasureMethod = staged.SelectedErasureMethod;
                        settings.SecureTrashEnabled = staged.EnableRecovery;
                        settings.SecureTrashAutoPurge = true;
                        settings.SecureTrashWipePasses = MapErasureMethodToWipePasses(staged.SelectedErasureMethod);
                    });
                    _baseline = staged;
                },
                discard: () =>
                {
                    _suppressStage = true;
                    try
                    {
                        SelectedErasureMethod = baseline.SelectedErasureMethod;
                        EnableRecovery = baseline.EnableRecovery;
                        SelectedRetentionPeriod = baseline.SelectedRetentionPeriod;
                        EnableDuplicateDetection = baseline.EnableDuplicateDetection;
                        AutoDeleteDuplicates = baseline.AutoDeleteDuplicates;
                        SelectedScanFrequency = baseline.SelectedScanFrequency;
                        AutoEmptyOnClose = baseline.AutoEmptyOnClose;
                        PromptBeforeDeletion = baseline.PromptBeforeDeletion;
                    }
                    finally
                    {
                        _suppressStage = false;
                    }
                });
        }

        private RubbishBinBaseline SnapshotCurrent() => new RubbishBinBaseline
        {
            SelectedErasureMethod = SelectedErasureMethod,
            EnableRecovery = EnableRecovery,
            SelectedRetentionPeriod = SelectedRetentionPeriod,
            EnableDuplicateDetection = EnableDuplicateDetection,
            AutoDeleteDuplicates = AutoDeleteDuplicates,
            SelectedScanFrequency = SelectedScanFrequency,
            AutoEmptyOnClose = AutoEmptyOnClose,
            PromptBeforeDeletion = PromptBeforeDeletion,
        };

        private bool MatchesBaseline()
        {
            var b = _baseline;
            return SelectedErasureMethod == b.SelectedErasureMethod
                && EnableRecovery == b.EnableRecovery
                && SelectedRetentionPeriod == b.SelectedRetentionPeriod
                && EnableDuplicateDetection == b.EnableDuplicateDetection
                && AutoDeleteDuplicates == b.AutoDeleteDuplicates
                && SelectedScanFrequency == b.SelectedScanFrequency
                && AutoEmptyOnClose == b.AutoEmptyOnClose
                && PromptBeforeDeletion == b.PromptBeforeDeletion;
        }

        private void ApplySecureTrashConfiguration()
        {
            _secureTrashService.ApplyConfiguration(
                EnableRecovery,
                autoPurgeEnabled: true,
                retentionDays: MapRetentionIndexToDays(SelectedRetentionPeriod),
                secureWipePasses: MapErasureMethodToWipePasses(SelectedErasureMethod));
        }

        private void UpdateStatusMessages()
        {
            var retentionDays = MapRetentionIndexToDays(SelectedRetentionPeriod);
            RecoveryRetentionStatus = EnableRecovery
                ? $"Deleted items stay recoverable for {retentionDays} day{(retentionDays == 1 ? string.Empty : "s")} before secure purge. Empty-bin actions can also create a protected shadow snapshot first."
                : "Recovery snapshots before purge are disabled. Deleted items are securely purged immediately instead of entering a recovery period.";

            DuplicateAutomationStatus = EnableDuplicateDetection
                ? AutoDeleteDuplicates
                    ? $"Duplicate review is enabled and duplicate scans are configured for {DescribeScanFrequency(SelectedScanFrequency)} checks. Matching secure-bin duplicates can be purged when you run a scan."
                    : $"Duplicate review is enabled with a {DescribeScanFrequency(SelectedScanFrequency)} scan preference. Scans currently report matches and let you decide what to purge."
                : "Duplicate review is disabled. Use the scan action to inspect secure-bin duplicates manually.";

            CleanupAutomationStatus = AutoEmptyOnClose
                ? "The secure rubbish bin will be emptied when the vault window closes. If confirmation is enabled, the app will prompt before purging."
                : "The secure rubbish bin stays intact when the vault window closes. Prompt-before-delete still applies to manual empty-bin actions.";
        }

        private async Task ScanForDuplicates()
        {
            try
            {
                var records = _secureTrashService.Records;
                var dupes = records
                    .GroupBy(r =>
                    {
                        var title = (r.Payload?.Title ?? string.Empty).Trim().ToLowerInvariant();
                        var user = (r.Payload?.Username ?? string.Empty).Trim().ToLowerInvariant();
                        return $"{title}|{user}";
                    })
                    .Where(g => g.Count() > 1)
                    .Select(g =>
                    {
                        var parts = g.Key.Split('|');
                        var title = parts.Length > 0 ? parts[0] : string.Empty;
                        var user = parts.Length > 1 ? parts[1] : string.Empty;
                        return $"{title} / {user} ({g.Count()} copies)";
                    })
                    .ToList();

                var message = dupes.Count == 0
                    ? "No duplicates detected in secure bin."
                    : "Possible duplicates:\n- " + string.Join("\n- ", dupes);

                if (dupes.Count > 0 && AutoDeleteDuplicates)
                {
                    var duplicateGroups = records
                        .GroupBy(r =>
                        {
                            var title = (r.Payload?.Title ?? string.Empty).Trim().ToLowerInvariant();
                            var user = (r.Payload?.Username ?? string.Empty).Trim().ToLowerInvariant();
                            return $"{title}|{user}";
                        })
                        .Where(g => g.Count() > 1)
                        .ToList();

                    var purged = 0;
                    foreach (var group in duplicateGroups)
                    {
                        foreach (var duplicate in group.Skip(1))
                        {
                            if (_secureTrashService.SecurelyPurge(duplicate.Id))
                            {
                                purged++;
                            }
                        }
                    }

                    RefreshStats();
                    message += $"\n\nAuto-delete removed {purged} duplicate secure-bin item(s).";
                }

                await _dialogService.ShowInfoAsync("Duplicate Scan", message, _owner);
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Duplicate Scan Failed", ex.Message, _owner);
            }
        }

        private async Task ViewDeletedItemsAsync()
        {
            try
            {
                var dialogResult = await _dialogService.ShowTrashManagerAsync(_secureTrashService.Records.ToList(), _owner);
                if (dialogResult.Action == DialogService.TrashManagerAction.Restore)
                {
                    var restored = 0;
                    foreach (var record in dialogResult.Selected)
                    {
                        var result = _secureTrashService.Restore(record.Id);
                        if (result.success) restored++;
                    }
                    if (restored > 0) await _dialogService.ShowInfoAsync("Restore", $"Restored {restored} item(s).", _owner);
                }
                else if (dialogResult.Action == DialogService.TrashManagerAction.Empty)
                {
                    var purged = 0;
                    foreach (var record in dialogResult.Selected)
                    {
                        if (_secureTrashService.SecurelyPurge(record.Id))
                        {
                            purged++;
                        }
                    }
                    if (purged > 0) await _dialogService.ShowInfoAsync("Purge", $"Purged {purged} item(s).", _owner);
                }
                RefreshStats();
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Deleted Items", ex.Message, _owner);
            }
        }

        private async Task EmptyBin()
        {
            try
            {
                if (PromptBeforeDeletion)
                {
                    await _dialogService.ShowWarningAsync("Empty Bin", "This will securely erase all items. Continue?", _owner);
                }

                var fileItems = _trashItemsProvider()?.Where(File.Exists).ToArray() ?? Array.Empty<string>();
                var secureTrashRecords = _secureTrashService.Records.ToList();

                if (fileItems.Length == 0 && secureTrashRecords.Count == 0)
                {
                    await _dialogService.ShowInfoAsync("Bin Empty", "There are no items to erase.", _owner);
                    return;
                }

                if (EnableRecovery)
                {
                    var creds = await BackupCredentialsDialog.PromptAsync(_owner);
                    if (creds == null)
                    {
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(creds.Passphrase) && string.IsNullOrWhiteSpace(creds.KeyfilePath))
                    {
                        await _dialogService.ShowWarningAsync("Shadow Snapshot", "Enter a passphrase or keyfile to protect the shadow snapshot.", _owner);
                        return;
                    }

                    var secret = await BuildSecretAsync(creds);
                    await CreateShadowSnapshotAsync(fileItems, secureTrashRecords, secret, creds.Pin);
                }

                foreach (var item in fileItems)
                {
                    await SecureDeletionService.SecureDeleteFileAsync(item, MapDeletionMethod(), null);
                }

                foreach (var record in secureTrashRecords)
                {
                    _secureTrashService.SecurelyPurge(record.Id);
                }

                RefreshStats();
                await _dialogService.ShowInfoAsync("Bin Emptied", "All items have been securely erased.", _owner);
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Empty Bin Failed", ex.Message, _owner);
            }
        }

        private async Task RestoreShadowSnapshotAsync()
        {
            try
            {
                var top = _owner ?? (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (top?.StorageProvider == null)
                    throw new InvalidOperationException("No window available to browse for snapshot.");

                var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select shadow snapshot",
                    AllowMultiple = false,
                    FileTypeFilter = new[] { new FilePickerFileType("Shadow snapshots") { Patterns = new[] { "*.pvshadow" } } }
                });
                if (files == null || files.Count == 0) return;

                var creds = await BackupCredentialsDialog.PromptAsync(_owner);
                if (creds == null) return;

                var secret = await BuildSecretAsync(creds);
                var result = await _shadowTrashService.RestoreSnapshotAsync(files[0].Path.LocalPath, secret, creds.Pin);

                var summary = $"Snapshot created UTC: {result.CreatedUtc:yyyy-MM-dd HH:mm:ss}\nItems: {result.Items.Count}";
                if (result.Items.Count > 0)
                {
                    var preview = result.Items.Take(5)
                        .Select(i => i.IsSecureTrash
                            ? $"secure-trash: {i.OriginalGroup ?? "unknown"} deleted {i.DeletedUtc?.ToLocalTime():g}"
                            : $"file: {i.Path} ({i.Size ?? 0} bytes)")
                        .ToList();
                    summary += "\nPreview:\n- " + string.Join("\n- ", preview);
                }

                await _dialogService.ShowInfoAsync("Snapshot Restored", summary, _owner);
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Restore Snapshot Failed", ex.Message, _owner);
            }
        }

        private async Task CreateShadowSnapshotAsync(string[] fileItems, IList<SecureTrashRecord> records, string passphrase, string pin)
        {
            if (fileItems.Length == 0 && (records == null || records.Count == 0)) return;
            var policyPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Policies", "base_policy.signed.json");
            var certPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Policies", "obscura_root.crt");
            var dest = System.IO.Path.Combine(AppContext.BaseDirectory, "trash-shadows");
            await _shadowTrashService.CreateShadowSnapshotAsync(fileItems, records, policyPath, certPath, dest, passphrase, pin);
        }

        private SecureDeletionService.DeletionMethod MapDeletionMethod() =>
            SelectedErasureMethod switch
            {
                0 => SecureDeletionService.DeletionMethod.SimpleOverwrite,
                1 => SecureDeletionService.DeletionMethod.StandardSecure,
                2 => SecureDeletionService.DeletionMethod.DoD522022M,
                3 => SecureDeletionService.DeletionMethod.Gutmann,
                4 => SecureDeletionService.DeletionMethod.EnhancedOverwrite,
                _ => SecureDeletionService.DeletionMethod.StandardSecure
            };

        private static int MapRetentionIndexToDays(int selectedRetentionPeriod) => selectedRetentionPeriod switch
        {
            <= 0 => 1,
            1 => 7,
            2 => 30,
            3 => 90,
            _ => 365
        };

        private static int MapRetentionDaysToIndex(int retentionDays) => retentionDays switch
        {
            <= 1 => 0,
            <= 7 => 1,
            <= 30 => 2,
            <= 90 => 3,
            _ => 4
        };

        private static int MapErasureMethodToWipePasses(int erasureMethod) => erasureMethod switch
        {
            0 => 1,
            1 => 3,
            2 => 7,
            3 => 10,
            4 => 10,
            _ => 3
        };

        private static string DescribeScanFrequency(int selectedScanFrequency) => selectedScanFrequency switch
        {
            0 => "daily",
            1 => "weekly",
            2 => "monthly",
            _ => "manual"
        };

        private void RefreshStats()
        {
            var records = _secureTrashService.Records.ToList();
            ItemsInBin = records.Count;

            if (records.Count == 0)
            {
                TotalSize = "0 KB";
                OldestItem = "N/A";
                return;
            }

            var totalBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(records).Length;
            TotalSize = $"{totalBytes / 1024.0:F1} KB";
            var oldest = records.Min(r => r.DeletedUtc);
            OldestItem = oldest.ToLocalTime().ToString("yyyy-MM-dd");
        }

        private static async Task<string> BuildSecretAsync(BackupCredentialsDialog.Result creds)
        {
            var secret = creds.Passphrase ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(creds.KeyfilePath) && File.Exists(creds.KeyfilePath))
            {
                var bytes = await File.ReadAllBytesAsync(creds.KeyfilePath);
                secret += Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(bytes));
            }

            return secret;
        }
    }
}
