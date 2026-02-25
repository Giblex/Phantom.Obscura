using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using Avalonia.Threading;
using Serilog;

namespace PhantomVault.UI.Services;

/// <summary>
/// Shared sync data written by both Phantom.Obscura and Phantom.Attestor.
/// </summary>
public class PhantomSyncData
{
    public string? SelectedThemeId { get; set; }
    public string? LastChangedBy { get; set; }
    public DateTimeOffset? LastChangedAt { get; set; }
}

/// <summary>
/// Cross-application sync service that keeps theme in sync
/// between Phantom.Obscura and Phantom.Attestor via a shared JSON file.
/// Monitors <c>%AppData%/PhantomVault/phantom-sync.json</c>.
/// </summary>
public sealed class CrossAppSyncService : IDisposable
{
    private const string SyncFileName = "phantom-sync.json";
    private const string AppIdentity = "PhantomObscura";

    private readonly string _syncFilePath;
    private readonly IRuntimeThemeService _runtimeThemeService;
    private FileSystemWatcher? _watcher;
    private bool _suppressNextWrite;
    private readonly object _lock = new();

    /// <summary>
    /// Raised when an incoming sync changes the theme.  Payload is the new theme ID.
    /// </summary>
    public event EventHandler<string>? ThemeSynced;

    public CrossAppSyncService(IRuntimeThemeService runtimeThemeService)
    {
        _runtimeThemeService = runtimeThemeService;

        var syncDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PhantomVault");
        Directory.CreateDirectory(syncDir);
        _syncFilePath = Path.Combine(syncDir, SyncFileName);
    }

    /// <summary>
    /// Starts watching the shared sync file. Also subscribes to theme changes
    /// so local changes are published automatically.
    /// </summary>
    public void Start()
    {
        // Publish current state if file doesn't exist
        if (!File.Exists(_syncFilePath))
        {
            WriteSyncFile(_runtimeThemeService.CurrentThemeId);
        }
        else
        {
            ApplyIncomingSync();
        }

        // Subscribe to local theme changes
        _runtimeThemeService.ThemeChanged += OnLocalThemeChanged;

        // Watch for external changes
        var dir = Path.GetDirectoryName(_syncFilePath)!;
        _watcher = new FileSystemWatcher(dir, SyncFileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnSyncFileChanged;

        Log.Information("[CrossAppSync] Started watching {SyncFile}", _syncFilePath);
    }

    private void OnLocalThemeChanged(object? sender, string themeId)
    {
        var settings = SettingsService.Load();
        if (!settings.SyncEnabled || !settings.SyncTheme) return;

        WriteSyncFile(themeId);
    }

    /// <summary>
    /// Manually publish a theme change (call in addition to the event if needed).
    /// </summary>
    public void PublishThemeChange(string themeId)
    {
        var settings = SettingsService.Load();
        if (!settings.SyncEnabled || !settings.SyncTheme) return;

        WriteSyncFile(themeId);
    }

    private void WriteSyncFile(string themeId)
    {
        lock (_lock)
        {
            try
            {
                _suppressNextWrite = true;

                var data = new PhantomSyncData
                {
                    SelectedThemeId = themeId,
                    LastChangedBy = AppIdentity,
                    LastChangedAt = DateTimeOffset.UtcNow
                };

                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_syncFilePath, json);

                Log.Debug("[CrossAppSync] Wrote sync file: theme={ThemeId}", themeId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[CrossAppSync] Failed to write sync file");
            }
        }
    }

    private void OnSyncFileChanged(object sender, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            if (_suppressNextWrite)
            {
                _suppressNextWrite = false;
                return;
            }
        }

        Thread.Sleep(100);
        Dispatcher.UIThread.Post(() => ApplyIncomingSync());
    }

    private void ApplyIncomingSync()
    {
        var settings = SettingsService.Load();
        if (!settings.SyncEnabled || !settings.SyncTheme) return;

        try
        {
            if (!File.Exists(_syncFilePath)) return;

            var json = File.ReadAllText(_syncFilePath);
            var data = JsonSerializer.Deserialize<PhantomSyncData>(json);
            if (data == null) return;

            // Only apply if another app changed it
            if (data.LastChangedBy == AppIdentity) return;

            if (!string.IsNullOrEmpty(data.SelectedThemeId) &&
                data.SelectedThemeId != _runtimeThemeService.CurrentThemeId)
            {
                Log.Information("[CrossAppSync] Incoming theme sync: {ThemeId} from {Source}",
                    data.SelectedThemeId, data.LastChangedBy);

                _runtimeThemeService.Apply(data.SelectedThemeId);

                // Persist to local settings
                settings.SelectedThemeId = data.SelectedThemeId;
                settings.LastSyncTime = DateTimeOffset.UtcNow;
                SettingsService.Save(settings);

                ThemeSynced?.Invoke(this, data.SelectedThemeId);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[CrossAppSync] Failed to apply incoming sync");
        }
    }

    public void Dispose()
    {
        _runtimeThemeService.ThemeChanged -= OnLocalThemeChanged;

        if (_watcher != null)
        {
            _watcher.Changed -= OnSyncFileChanged;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }
}
