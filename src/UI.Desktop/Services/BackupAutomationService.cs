using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.Services
{
    public static class BackupAutomationService
    {
        public static bool IsBackupDue(UserSettings settings, DateTimeOffset nowUtc)
        {
            if (!settings.BackupAutomationEnabled)
            {
                return false;
            }

            if (settings.BackupFrequencyMode == 3)
            {
                return true;
            }

            if (settings.LastAutomatedBackupUtc is null)
            {
                return true;
            }

            var elapsed = nowUtc - settings.LastAutomatedBackupUtc.Value;
            return settings.BackupFrequencyMode switch
            {
                0 => elapsed >= TimeSpan.FromDays(1),
                1 => elapsed >= TimeSpan.FromDays(7),
                2 => elapsed >= TimeSpan.FromDays(30),
                _ => false
            };
        }

        public static async Task<string?> RunAutomatedBackupIfDueAsync(
            BackupService backupService,
            string manifestPath,
            string? passphrase,
            string? keyfilePath,
            string fallbackBackupDirectory)
        {
            var nowUtc = DateTimeOffset.UtcNow;
            var settings = SettingsService.Load();
            if (!IsBackupDue(settings, nowUtc))
            {
                return null;
            }

            var backupDirectory = string.IsNullOrWhiteSpace(settings.BackupLocation)
                ? fallbackBackupDirectory
                : settings.BackupLocation;

            Directory.CreateDirectory(backupDirectory);
            var backupPath = await backupService.CreateBackupAsync(manifestPath, passphrase, keyfilePath, backupDirectory).ConfigureAwait(false);
            PruneBackupCount(backupDirectory, settings.BackupRetentionCount);

            SettingsService.Update(current =>
            {
                current.LastAutomatedBackupUtc = nowUtc;
                if (string.IsNullOrWhiteSpace(current.BackupLocation))
                {
                    current.BackupLocation = backupDirectory;
                }
            });

            return backupPath;
        }

        public static void PruneBackupCount(string backupDirectory, int retentionSelection)
        {
            if (string.IsNullOrWhiteSpace(backupDirectory) || !Directory.Exists(backupDirectory))
            {
                return;
            }

            var keepCount = retentionSelection switch
            {
                0 => 5,
                1 => 10,
                2 => 20,
                3 => 50,
                _ => int.MaxValue
            };

            if (keepCount == int.MaxValue)
            {
                return;
            }

            var backupFiles = Directory.GetFiles(backupDirectory, "*.bak")
                .Concat(Directory.GetFiles(backupDirectory, "*.pvbkp"))
                .Concat(Directory.GetFiles(backupDirectory, "*.pvbackup"))
                .Concat(Directory.GetFiles(backupDirectory, "*.backup"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.CreationTimeUtc)
                .ToList();

            foreach (var staleFile in backupFiles.Skip(keepCount))
            {
                try
                {
                    staleFile.Delete();
                }
                catch
                {
                    // Best effort pruning.
                }
            }
        }
    }
}
