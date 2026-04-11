using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PhantomVault.Core.Services;
using PhantomVault.UI.Services;
using Xunit;

namespace PhantomVault.UI.Tests;

[Collection("SettingsService")]
public sealed class BackupAutomationServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _manifestPath;
    private readonly string _settingsPath;
    private readonly string? _originalSettingsJson;

    public BackupAutomationServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"PhantomVault_BackupAutomation_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        _manifestPath = Path.Combine(_tempDirectory, "vault.manifest");
        File.WriteAllText(_manifestPath, """{"vault":"test"}""");

        var settingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhantomVault");
        _settingsPath = Path.Combine(settingsDir, "settings.json");
        _originalSettingsJson = File.Exists(_settingsPath) ? File.ReadAllText(_settingsPath) : null;
    }

    public void Dispose()
    {
        try
        {
            if (_originalSettingsJson is null)
            {
                if (File.Exists(_settingsPath))
                {
                    File.Delete(_settingsPath);
                }
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                File.WriteAllText(_settingsPath, _originalSettingsJson);
            }
        }
        catch
        {
            // Best effort restore for shared local settings.
        }

        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    [Fact]
    public void IsBackupDue_ReturnsFalse_WhenAutomationDisabled()
    {
        var settings = new UserSettings
        {
            BackupAutomationEnabled = false,
            BackupFrequencyMode = 0,
            LastAutomatedBackupUtc = null
        };

        var due = BackupAutomationService.IsBackupDue(settings, DateTimeOffset.UtcNow);

        Assert.False(due);
    }

    [Fact]
    public void IsBackupDue_ReturnsTrue_WhenEnabledAndNeverRun()
    {
        var settings = new UserSettings
        {
            BackupAutomationEnabled = true,
            BackupFrequencyMode = 1,
            LastAutomatedBackupUtc = null
        };

        var due = BackupAutomationService.IsBackupDue(settings, DateTimeOffset.UtcNow);

        Assert.True(due);
    }

    [Fact]
    public async Task RunAutomatedBackupIfDueAsync_CreatesBackupAndUpdatesSettings()
    {
        var backupDirectory = Path.Combine(_tempDirectory, "backups");
        SettingsService.Save(new UserSettings
        {
            BackupAutomationEnabled = true,
            BackupFrequencyMode = 3,
            BackupLocation = backupDirectory,
            BackupRetentionCount = 2
        });

        var backupService = new BackupService(new EncryptionService());

        var backupPath = await BackupAutomationService.RunAutomatedBackupIfDueAsync(
            backupService,
            _manifestPath,
            "test-passphrase",
            null,
            backupDirectory);

        Assert.False(string.IsNullOrWhiteSpace(backupPath));
        Assert.NotNull(backupPath);
        Assert.True(File.Exists(backupPath!));

        var settings = SettingsService.Load();
        Assert.NotNull(settings.LastAutomatedBackupUtc);
        Assert.Equal(backupDirectory, settings.BackupLocation);
    }

    [Fact]
    public void PruneBackupCount_KeepsMostRecentFiles_WithinSelectedRetention()
    {
        var backupDirectory = Path.Combine(_tempDirectory, "retention");
        Directory.CreateDirectory(backupDirectory);

        for (var index = 0; index < 7; index++)
        {
            var path = Path.Combine(backupDirectory, $"manifest-backup-{index:000}.bak");
            File.WriteAllText(path, $"backup-{index}");
            File.SetCreationTimeUtc(path, DateTime.UtcNow.AddMinutes(-index));
        }

        BackupAutomationService.PruneBackupCount(backupDirectory, retentionSelection: 0);

        var remainingFiles = Directory.GetFiles(backupDirectory, "*.bak")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(5, remainingFiles.Length);
        Assert.DoesNotContain("manifest-backup-005.bak", remainingFiles);
        Assert.DoesNotContain("manifest-backup-006.bak", remainingFiles);
    }
}

[CollectionDefinition("SettingsService", DisableParallelization = true)]
public sealed class SettingsServiceCollectionDefinition
{
}
