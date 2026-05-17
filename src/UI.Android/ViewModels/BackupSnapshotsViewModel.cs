using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Android.Services;

namespace PhantomVault.Android.ViewModels
{
    public sealed partial class BackupSnapshotsViewModel : BaseViewModel
    {
        private readonly MobileBackupService _backupService;
        private readonly VaultViewModel _vault;

        [ObservableProperty] private ObservableCollection<BackupSnapshotItem> _snapshots = new();
        [ObservableProperty] private int _snapshotCount;
        [ObservableProperty] private string _totalSize = "0 B";

        private string _masterPassword = string.Empty;

        public BackupSnapshotsViewModel(MobileBackupService backupService, VaultViewModel vault)
        {
            _backupService = backupService;
            _vault = vault;
        }

        public void SetMasterPassword(string mp) => _masterPassword = mp;

        [RelayCommand]
        public async Task RefreshAsync()
        {
            await RunSafeAsync(async () =>
            {
                var items = await _backupService.ListSnapshotsAsync();
                Snapshots.Clear();
                foreach (var i in items) Snapshots.Add(i);
                SnapshotCount = Snapshots.Count;
                TotalSize = FormatSize(Snapshots.Sum(s => s.SizeBytes));
            }, "Loading snapshots…");
        }

        [RelayCommand]
        private async Task CreateSnapshotAsync()
        {
            await RunSafeAsync(async () =>
            {
                await _backupService.CreateSnapshotAsync(_masterPassword);
                await RefreshAsync();
                StatusMessage = "Backup snapshot created.";
            }, "Creating snapshot…");
        }

        [RelayCommand]
        private async Task RestoreSnapshotAsync(BackupSnapshotItem? snapshot)
        {
            if (snapshot is null) return;
            bool confirmed = await Shell.Current.DisplayAlert(
                "Restore Backup",
                $"Restore vault from backup created {snapshot.CreatedDate}?\n\nThis will replace your current vault data.",
                "Restore", "Cancel");
            if (!confirmed) return;

            await RunSafeAsync(async () =>
            {
                await _backupService.RestoreSnapshotAsync(snapshot.FilePath, _masterPassword);
                var db = await _backupService.LoadRestoredVaultAsync(_masterPassword);
                _vault.LoadCredentials(db);
                StatusMessage = $"Restored from {snapshot.FileName}";
            }, "Restoring…");
        }

        [RelayCommand]
        private async Task DeleteSnapshotAsync(BackupSnapshotItem? snapshot)
        {
            if (snapshot is null) return;
            bool confirmed = await Shell.Current.DisplayAlert("Delete",
                $"Delete backup {snapshot.FileName}?", "Delete", "Cancel");
            if (!confirmed) return;
            System.IO.File.Delete(snapshot.FilePath);
            Snapshots.Remove(snapshot);
            SnapshotCount = Snapshots.Count;
            TotalSize = FormatSize(Snapshots.Sum(s => s.SizeBytes));
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            string[] units = { "KB", "MB", "GB" };
            double value = bytes / 1024d;
            int idx = 0;
            while (value >= 1024 && idx < units.Length - 1) { value /= 1024; idx++; }
            return $"{value:0.##} {units[idx]}";
        }
    }

    public partial class BackupSnapshotItem : ObservableObject
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string CreatedDate { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string SizeFormatted { get; set; } = string.Empty;
    }
}
