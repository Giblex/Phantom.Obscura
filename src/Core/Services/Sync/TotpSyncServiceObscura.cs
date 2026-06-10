using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using Serilog;

namespace PhantomVault.Core.Services.Sync
{

    public class TotpSyncServiceObscura : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private const int MaxRetries = 3;
        private const int RetryDelayMs = 100;
        private const int DebounceMs = 150;

        private readonly string _syncFilePath;
        private FileSystemWatcher? _watcher;
        private readonly SemaphoreSlim _syncGate = new(1, 1);
        private DateTimeOffset _lastWriteTime = DateTimeOffset.MinValue;
        private Timer? _debounceTimer;
        private bool _isSelfWrite;

        public event EventHandler<List<SharedTotpEntry>>? EntriesChanged;

        public TotpSyncServiceObscura(string syncFilePath)
        {
            _syncFilePath = syncFilePath ?? throw new ArgumentNullException(nameof(syncFilePath));
        }

        public async Task InitializeAsync()
        {
            try
            {
                var directory = Path.GetDirectoryName(_syncFilePath);
                if (string.IsNullOrEmpty(directory))
                {
                    throw new InvalidOperationException("Invalid sync file path");
                }

                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!File.Exists(_syncFilePath))
                {
                    await WriteFileWithRetryAsync(_syncFilePath, "[]");
                }

                _watcher = new FileSystemWatcher(directory)
                {
                    Filter = Path.GetFileName(_syncFilePath),
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                    IncludeSubdirectories = false
                };

                _watcher.Changed += OnFileChanged;
                _watcher.Created += OnFileChanged;
                _watcher.EnableRaisingEvents = true;

                Log.Information("TOTP sync service initialized: {Path}", _syncFilePath);

                await LoadEntriesAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize TOTP sync service");
                throw;
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (_isSelfWrite)
            {
                return;
            }

            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(OnDebounceElapsed, null, DebounceMs, Timeout.Infinite);
        }

        private async void OnDebounceElapsed(object? state)
        {
            try
            {
                await LoadEntriesAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load TOTP entries after file change");
            }
        }

        private async Task LoadEntriesAsync()
        {

            if (!await _syncGate.WaitAsync(TimeSpan.FromSeconds(5)))
            {
                Log.Warning("TOTP sync: Could not acquire lock for LoadEntriesAsync, skipping");
                return;
            }

            try
            {
                if (!File.Exists(_syncFilePath))
                {
                    return;
                }

                var fileInfo = new FileInfo(_syncFilePath);
                var currentWriteTime = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero);

                if (currentWriteTime <= _lastWriteTime)
                {
                    return;
                }

                _lastWriteTime = currentWriteTime;

                var json = await ReadFileWithRetryAsync(_syncFilePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                var entries = JsonSerializer.Deserialize<List<SharedTotpEntry>>(json, JsonOptions) ?? new List<SharedTotpEntry>();

                foreach (var entry in entries)
                {
                    entry.Normalize();
                }

                var externalEntries = entries.Where(e => e.ModifiedByApp != "PhantomObscura").ToList();

                if (externalEntries.Count > 0)
                {
                    EntriesChanged?.Invoke(this, externalEntries);
                    Log.Information("Loaded {Count} TOTP entries from external app(s)", externalEntries.Count);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load TOTP entries from sync file");
            }
            finally
            {
                _syncGate.Release();
            }
        }

        public async Task WriteEntriesAsync(List<SharedTotpEntry> entries)
        {
            await _syncGate.WaitAsync();

            try
            {
                _isSelfWrite = true;

                foreach (var entry in entries)
                {
                    entry.LastModifiedUtc = DateTimeOffset.UtcNow;

                    if (string.IsNullOrEmpty(entry.ModifiedByApp))
                        entry.ModifiedByApp = "PhantomObscura";
                    if (entry.CreatedUtc == default)
                        entry.CreatedUtc = entry.LastModifiedUtc;
                    entry.Normalize();
                }

                var json = JsonSerializer.Serialize(entries, JsonOptions);

                if (_watcher != null)
                    _watcher.EnableRaisingEvents = false;

                await WriteFileWithRetryAsync(_syncFilePath, json);

                if (_watcher != null)
                {
                    await Task.Delay(50);
                    _watcher.EnableRaisingEvents = true;
                }

                var fileInfo = new FileInfo(_syncFilePath);
                _lastWriteTime = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero);

                Log.Information("Wrote {Count} TOTP entries to sync file", entries.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to write TOTP entries to sync file");
                throw;
            }
            finally
            {
                _isSelfWrite = false;
                _syncGate.Release();
            }
        }

        public async Task ExportToSyncFileAsync(List<SharedTotpEntry> obscuraEntries)
        {
            await _syncGate.WaitAsync();

            try
            {
                _isSelfWrite = true;

                var existingEntries = new List<SharedTotpEntry>();
                if (File.Exists(_syncFilePath))
                {
                    try
                    {
                        var json = await ReadFileWithRetryAsync(_syncFilePath);
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            existingEntries = JsonSerializer.Deserialize<List<SharedTotpEntry>>(json, JsonOptions) ?? new List<SharedTotpEntry>();
                            foreach (var entry in existingEntries)
                            {
                                entry.Normalize();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Could not read existing sync file for merge, starting fresh");
                    }
                }

                var attestorEntries = existingEntries
                    .Where(e => e.ModifiedByApp != "PhantomObscura")
                    .ToList();

                var now = DateTimeOffset.UtcNow;
                foreach (var entry in obscuraEntries)
                {
                    entry.ModifiedByApp = "PhantomObscura";
                    entry.LastModifiedUtc = now;
                    entry.Normalize();
                }

                var mergedEntries = new List<SharedTotpEntry>(attestorEntries);
                foreach (var obscuraEntry in obscuraEntries)
                {

                    var existingAttestor = mergedEntries.FirstOrDefault(e =>
                        !string.IsNullOrEmpty(e.Secret) &&
                        string.Equals(e.Secret, obscuraEntry.Secret, StringComparison.OrdinalIgnoreCase));

                    if (existingAttestor != null)
                    {

                        if (obscuraEntry.LastModifiedUtc > existingAttestor.LastModifiedUtc)
                        {

                            mergedEntries.Remove(existingAttestor);
                            mergedEntries.Add(obscuraEntry);
                        }
                        else
                        {

                            if (string.IsNullOrEmpty(existingAttestor.LinkedPasswordEntryId) &&
                                !string.IsNullOrEmpty(obscuraEntry.LinkedPasswordEntryId))
                            {
                                existingAttestor.LinkedPasswordEntryId = obscuraEntry.LinkedPasswordEntryId;
                            }
                        }
                    }
                    else
                    {

                        mergedEntries.Add(obscuraEntry);
                    }
                }

                var mergedJson = JsonSerializer.Serialize(mergedEntries, JsonOptions);

                if (_watcher != null)
                    _watcher.EnableRaisingEvents = false;

                await WriteFileWithRetryAsync(_syncFilePath, mergedJson);

                if (_watcher != null)
                {
                    await Task.Delay(50);
                    _watcher.EnableRaisingEvents = true;
                }

                var fileInfo = new FileInfo(_syncFilePath);
                _lastWriteTime = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero);

                Log.Information("Exported {ObscuraCount} Obscura TOTP entries, merged with {AttestorCount} Attestor entries ({TotalCount} total)",
                    obscuraEntries.Count, attestorEntries.Count, mergedEntries.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to export TOTP entries to sync file");
            }
            finally
            {
                _isSelfWrite = false;
                _syncGate.Release();
            }
        }

        public List<SharedTotpEntry> ExtractFromVault(IEnumerable<Credential> credentials)
        {
            var entries = new List<SharedTotpEntry>();

            foreach (var credential in credentials)
            {
                if (!string.IsNullOrWhiteSpace(credential.TotpSecret))
                {
                    entries.Add(new SharedTotpEntry
                    {
                        Id = Guid.NewGuid().ToString(),
                        LinkedPasswordEntryId = credential.Title,
                        Issuer = credential.TotpIssuer ?? string.Empty,
                        AccountName = credential.TotpAccountName ?? credential.Username ?? string.Empty,
                        Secret = credential.TotpSecret,
                        Digits = credential.TotpDigits,
                        Period = credential.TotpTimeStep,
                        Algorithm = credential.TotpAlgorithm ?? "SHA1",
                        LastModifiedUtc = credential.LastUpdatedUtc,
                        ModifiedByApp = "PhantomObscura"
                    });
                }
            }

            return entries;
        }

        #region File I/O helpers with retry

        private static async Task<string> ReadFileWithRetryAsync(string path)
        {
            for (int i = 0; i < MaxRetries; i++)
            {
                try
                {
                    return await File.ReadAllTextAsync(path);
                }
                catch (IOException) when (i < MaxRetries - 1)
                {
                    await Task.Delay(RetryDelayMs);
                }
            }

            return string.Empty;
        }

        private static async Task WriteFileWithRetryAsync(string path, string content)
        {
            for (int i = 0; i < MaxRetries; i++)
            {
                try
                {
                    await File.WriteAllTextAsync(path, content);
                    return;
                }
                catch (IOException) when (i < MaxRetries - 1)
                {
                    await Task.Delay(RetryDelayMs);
                }
            }
        }

        #endregion

        public void Dispose()
        {
            if (_watcher != null)
            {
                _watcher.Changed -= OnFileChanged;
                _watcher.Dispose();
                _watcher = null;
            }

            _debounceTimer?.Dispose();
            _debounceTimer = null;
            _syncGate.Dispose();
        }
    }
}

