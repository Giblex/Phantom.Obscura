using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PhantomVault.Android.ViewModels;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;

namespace PhantomVault.Android.Services
{
    /// <summary>
    /// Manages encrypted backup snapshots of the mobile vault.
    /// Backups are copies of the encrypted vault file with a timestamp suffix.
    /// </summary>
    public sealed class MobileBackupService
    {
        private readonly MobileVaultService _vaultService;

        public string BackupDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhantomObscura", "Backups");

        public MobileBackupService(MobileVaultService vaultService) => _vaultService = vaultService;

        public Task<List<BackupSnapshotItem>> ListSnapshotsAsync() => Task.Run(() =>
        {
            var list = new List<BackupSnapshotItem>();
            if (!Directory.Exists(BackupDirectory)) return list;
            foreach (var file in Directory.GetFiles(BackupDirectory, "*.pvbkp")
                         .OrderByDescending(f => File.GetCreationTime(f)))
            {
                var fi = new FileInfo(file);
                list.Add(new BackupSnapshotItem
                {
                    FilePath = file,
                    FileName = fi.Name,
                    CreatedDate = fi.CreationTime.ToString("yyyy-MM-dd HH:mm"),
                    SizeBytes = fi.Length,
                    SizeFormatted = FormatSize(fi.Length)
                });
            }
            return list;
        });

        public async Task CreateSnapshotAsync(string masterPassword)
        {
            if (!_vaultService.VaultExists()) throw new InvalidOperationException("No vault to back up.");
            Directory.CreateDirectory(BackupDirectory);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var dest = Path.Combine(BackupDirectory, $"backup-{timestamp}.pvbkp");
            await Task.Run(() => File.Copy(_vaultService.VaultFilePath, dest, overwrite: false));
            PruneOldBackups(10);
        }

        public Task RestoreSnapshotAsync(string snapshotPath, string masterPassword) => Task.Run(() =>
        {
            if (!File.Exists(snapshotPath)) throw new FileNotFoundException("Snapshot not found.", snapshotPath);
            File.Copy(snapshotPath, _vaultService.VaultFilePath, overwrite: true);
        });

        public Task<VaultDatabase> LoadRestoredVaultAsync(string masterPassword)
            => _vaultService.OpenVaultAsync(masterPassword);

        private void PruneOldBackups(int maxCount)
        {
            var files = Directory.GetFiles(BackupDirectory, "*.pvbkp")
                .OrderByDescending(f => File.GetCreationTime(f))
                .Skip(maxCount);
            foreach (var f in files)
                try { File.Delete(f); } catch { /* best-effort */ }
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
}
