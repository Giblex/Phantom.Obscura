using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using System.Windows.Input;
using ReactiveUI;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.ViewModels
{
    public class BackupSnapshotsViewModel : ReactiveObject
    {
        private readonly string _backupLocation;
        private readonly DialogService _dialogService;
        private string _statusMessage = "Ready";
        private ObservableCollection<BackupSnapshotInfo> _snapshots = new();

        public BackupSnapshotsViewModel(string? backupLocation = null)
        {
            _backupLocation = backupLocation ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhantomVault", "Backups");
            _dialogService = new DialogService();

            RefreshCommand = ReactiveCommand.CreateFromTask(RefreshSnapshots);
            CreateSnapshotCommand = ReactiveCommand.CreateFromTask(CreateSnapshot);
            RestoreCommand = ReactiveCommand.CreateFromTask<BackupSnapshotInfo>(RestoreSnapshot);
            DeleteCommand = ReactiveCommand.CreateFromTask<BackupSnapshotInfo>(DeleteSnapshot);

            // Load snapshots on initialization
            _ = RefreshSnapshots();
        }

        public ObservableCollection<BackupSnapshotInfo> Snapshots
        {
            get => _snapshots;
            set => this.RaiseAndSetIfChanged(ref _snapshots, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public int SnapshotCount => Snapshots.Count;
        public string TotalSize
        {
            get
            {
                long totalBytes = Snapshots.Sum(s => s.SizeBytes);
                return $"Total Size: {FormatSize(totalBytes)}";
            }
        }

        public ICommand RefreshCommand { get; }
        public ICommand CreateSnapshotCommand { get; }
        public ICommand RestoreCommand { get; }
        public ICommand DeleteCommand { get; }

        private async Task RefreshSnapshots()
        {
            try
            {
                StatusMessage = "Loading snapshots...";

                await Task.Run(() =>
                {
                    Snapshots.Clear();

                    if (!Directory.Exists(_backupLocation))
                    {
                        Directory.CreateDirectory(_backupLocation);
                        return;
                    }

                    var backupFiles = Directory.GetFiles(_backupLocation, "*.pvbkp")
                        .Concat(Directory.GetFiles(_backupLocation, "*.pvbackup"))
                        .Concat(Directory.GetFiles(_backupLocation, "*.backup"))
                        .OrderByDescending(f => File.GetCreationTime(f));

                    foreach (var file in backupFiles)
                    {
                        var fileInfo = new FileInfo(file);
                        var snapshot = new BackupSnapshotInfo
                        {
                            FilePath = file,
                            FileName = Path.GetFileName(file),
                            CreatedDate = fileInfo.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"),
                            SizeBytes = fileInfo.Length,
                            SizeFormatted = FormatSize(fileInfo.Length),
                            Type = DetermineBackupType(file),
                            Status = "Valid",
                            StatusColor = "#2D5F2E" // Green
                        };

                        Snapshots.Add(snapshot);
                    }
                });

                this.RaisePropertyChanged(nameof(SnapshotCount));
                this.RaisePropertyChanged(nameof(TotalSize));
                StatusMessage = $"Found {SnapshotCount} backup snapshot(s)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading snapshots: {ex.Message}";
            }
        }

        private async Task CreateSnapshot()
        {
            StatusMessage = "Creating backup snapshot...";

            // This would integrate with the actual backup service
            await Task.Delay(500);

            StatusMessage = "Snapshot creation requires an open vault. Use 'Backup Now' in Settings.";
        }

        private async Task RestoreSnapshot(BackupSnapshotInfo? snapshot)
        {
            if (snapshot == null) return;

            try
            {
                var confirmed = await _dialogService.ShowConfirmationAsync(
                    "Restore Backup",
                    $"Restore vault from backup created on {snapshot.CreatedDate}?\n\n" +
                    "⚠️ This will replace your current vault data.\n\n" +
                    "Make sure you have a recent backup before proceeding.",
                    null
                );

                if (!confirmed) return;

                StatusMessage = $"Restoring from {snapshot.FileName}...";

                // Actual restoration would happen here
                await Task.Delay(1000);

                StatusMessage = "Restoration requires vault credentials. Use 'Restore from Backup' in Settings.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Restore failed: {ex.Message}";
            }
        }

        private async Task DeleteSnapshot(BackupSnapshotInfo? snapshot)
        {
            if (snapshot == null) return;

            try
            {
                var confirmed = await _dialogService.ShowConfirmationAsync(
                    "Delete Backup",
                    $"Permanently delete backup snapshot?\n\n" +
                    $"File: {snapshot.FileName}\n" +
                    $"Created: {snapshot.CreatedDate}\n" +
                    $"Size: {snapshot.SizeFormatted}\n\n" +
                    "This action cannot be undone.",
                    null
                );

                if (!confirmed) return;

                File.Delete(snapshot.FilePath);
                Snapshots.Remove(snapshot);

                this.RaisePropertyChanged(nameof(SnapshotCount));
                this.RaisePropertyChanged(nameof(TotalSize));
                StatusMessage = $"Deleted {snapshot.FileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Delete failed: {ex.Message}";
                await _dialogService.ShowErrorAsync("Delete Failed", ex.Message, null);
            }
        }

        private static string DetermineBackupType(string filePath)
        {
            var fileName = Path.GetFileName(filePath).ToLowerInvariant();

            if (fileName.Contains("auto"))
                return "Automatic";
            else if (fileName.Contains("manual"))
                return "Manual";
            else
                return "Manual";
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";

            string[] units = { "KB", "MB", "GB", "TB" };
            double value = bytes / 1024d;
            var unitIndex = 0;

            while (value >= 1024 && unitIndex < units.Length - 1)
            {
                value /= 1024;
                unitIndex++;
            }

            return $"{value:0.##} {units[unitIndex]}";
        }
    }

    public class BackupSnapshotInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string CreatedDate { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string SizeFormatted { get; set; } = string.Empty;
        public string Type { get; set; } = "Manual";
        public string Status { get; set; } = "Unknown";
        public string StatusColor { get; set; } = "#555555";
    }
}
