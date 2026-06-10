using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PhantomVault.Core.Services.Autofill;
using Serilog;

namespace PhantomVault.UI.Services.AutoFill
{
    /// <summary>
    /// File-backed <see cref="ISyncStateBridge"/> used by the native messaging host
    /// process. It mirrors the desktop app's existing source of truth
    /// (<c>%APPDATA%/PhantomVault/phantom-sync.json</c>, watched by
    /// <c>CrossAppSyncService</c>) so the file remains the single source of truth.
    ///
    /// Only the non-secret layer is bridged: theme, UI prefs, and the origin
    /// allowlist. Credentials and TOTP secrets are never read or written here.
    /// </summary>
    internal sealed class FileSyncStateBridge : ISyncStateBridge, IDisposable
    {
        // Must stay compatible with CrossAppSyncService.PhantomSyncData. We write a
        // superset (adds "uiPrefs"); System.Text.Json ignores unknown members so the
        // desktop service still parses theme/identity fields cleanly.
        private const string SyncFileName = "phantom-sync.json";

        // A distinct identity so the desktop CrossAppSyncService (AppIdentity
        // "PhantomObscura") treats an extension-originated write as *incoming* and
        // applies it, instead of ignoring it as its own write.
        private const string ExtensionIdentity = "PhantomObscura.Extension";

        private readonly string _syncFilePath;
        private readonly IReadOnlyList<string> _enabledOrigins;
        private readonly FileSystemWatcher? _watcher;
        private readonly object _lock = new();

        public event EventHandler? Changed;

        public FileSyncStateBridge(string syncDirectory, IEnumerable<string>? enabledOrigins)
        {
            Directory.CreateDirectory(syncDirectory);
            _syncFilePath = Path.Combine(syncDirectory, SyncFileName);
            _enabledOrigins = (enabledOrigins ?? Enumerable.Empty<string>())
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .Select(o => o.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            try
            {
                _watcher = new FileSystemWatcher(syncDirectory, SyncFileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                    EnableRaisingEvents = true
                };
                _watcher.Changed += OnFileChanged;
                _watcher.Created += OnFileChanged;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "FileSyncStateBridge: could not watch {Path}", _syncFilePath);
            }
        }

        public SyncState Read()
        {
            var state = new SyncState
            {
                EnabledOrigins = _enabledOrigins.ToList()
            };

            try
            {
                if (!File.Exists(_syncFilePath))
                    return state;

                var json = File.ReadAllText(_syncFilePath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("SelectedThemeId", out var theme) &&
                    theme.ValueKind == JsonValueKind.String)
                {
                    state.ThemeId = theme.GetString();
                }

                if (root.TryGetProperty("uiPrefs", out var prefs) &&
                    prefs.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in prefs.EnumerateObject())
                    {
                        if (p.Value.ValueKind == JsonValueKind.String)
                            state.UiPrefs[p.Name] = p.Value.GetString() ?? string.Empty;
                    }
                }

                if (root.TryGetProperty("LastChangedAt", out var ts) &&
                    ts.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(ts.GetString(), out var parsed))
                {
                    state.UpdatedAt = parsed;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "FileSyncStateBridge: failed to read sync state");
            }

            // TotpIssuers intentionally left empty — the TOTP sync file lives on the
            // USB vault and is only available to the unlocked desktop app, not here.
            return state;
        }

        public void Apply(SyncState incoming)
        {
            if (incoming == null) return;

            lock (_lock)
            {
                try
                {
                    var payload = new Dictionary<string, object?>
                    {
                        ["SelectedThemeId"] = incoming.ThemeId,
                        ["LastChangedBy"] = ExtensionIdentity,
                        ["LastChangedAt"] = DateTimeOffset.UtcNow,
                        ["uiPrefs"] = incoming.UiPrefs ?? new Dictionary<string, string>()
                    };

                    var json = JsonSerializer.Serialize(payload,
                        new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_syncFilePath, json);

                    Log.Information("FileSyncStateBridge: applied extension sync (theme={Theme})",
                        incoming.ThemeId);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "FileSyncStateBridge: failed to apply sync state");
                }
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            try { Changed?.Invoke(this, EventArgs.Empty); }
            catch { /* best-effort notification */ }
        }

        public void Dispose()
        {
            if (_watcher != null)
            {
                _watcher.Changed -= OnFileChanged;
                _watcher.Created -= OnFileChanged;
                _watcher.Dispose();
            }
        }
    }
}
