using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using Serilog;

namespace PhantomVault.Core.Services.Sync
{
    /// <summary>
    /// Synchronizes TOTP entries between PhantomAttestor and PhantomObscura via a shared JSON file
    /// </summary>
    public class TotpSyncServiceObscura : IDisposable
    {
        private readonly string _syncFilePath;
        private FileSystemWatcher? _watcher;
        private readonly object _syncLock = new object();
        private DateTimeOffset _lastWriteTime = DateTimeOffset.MinValue;
        private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(100);
        private System.Threading.Timer? _debounceTimer;
        private bool _isProcessing;

        public event EventHandler<List<SharedTotpEntry>>? EntriesChanged;

        public TotpSyncServiceObscura(string syncFilePath)
        {
            _syncFilePath = syncFilePath ?? throw new ArgumentNullException(nameof(syncFilePath));
        }

        /// <summary>
        /// Initializes the file watcher for TOTP sync file
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                var directory = Path.GetDirectoryName(_syncFilePath);
                if (string.IsNullOrEmpty(directory))
                {
                    throw new InvalidOperationException("Invalid sync file path");
                }

                // Ensure directory exists
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Create empty file if it doesn't exist
                if (!File.Exists(_syncFilePath))
                {
                    await File.WriteAllTextAsync(_syncFilePath, "[]");
                }

                // Set up file watcher
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

                // Load initial entries
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
            lock (_syncLock)
            {
                if (_isProcessing)
                {
                    return; // Ignore changes we caused
                }

                // Debounce rapid-fire events
                _debounceTimer?.Dispose();
                _debounceTimer = new System.Threading.Timer(async _ =>
                {
                    try
                    {
                        await LoadEntriesAsync();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to load TOTP entries after file change");
                    }
                }, null, (int)_debounceDelay.TotalMilliseconds, System.Threading.Timeout.Infinite);
            }
        }

        /// <summary>
        /// Loads TOTP entries from the sync file
        /// </summary>
        private async Task LoadEntriesAsync()
        {
            try
            {
                if (!File.Exists(_syncFilePath))
                {
                    return;
                }

                var fileInfo = new FileInfo(_syncFilePath);
                var currentWriteTime = fileInfo.LastWriteTimeUtc;

                // Skip if file hasn't changed
                if (currentWriteTime <= _lastWriteTime)
                {
                    return;
                }

                _lastWriteTime = currentWriteTime;

                // Read with retry for file locks
                string json = string.Empty;
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        json = await File.ReadAllTextAsync(_syncFilePath);
                        break;
                    }
                    catch (IOException) when (i < 2)
                    {
                        await Task.Delay(100); // Wait and retry
                    }
                }

                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                var entries = JsonSerializer.Deserialize<List<SharedTotpEntry>>(json) ?? new List<SharedTotpEntry>();

                // Filter out entries modified by PhantomObscura to prevent loops
                var externalEntries = entries.Where(e => e.ModifiedByApp != "PhantomObscura").ToList();

                if (externalEntries.Any())
                {
                    EntriesChanged?.Invoke(this, externalEntries);
                    Log.Information("Loaded {Count} TOTP entries from PhantomAttestor", externalEntries.Count);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load TOTP entries from sync file");
            }
        }

        /// <summary>
        /// Writes TOTP entries to the sync file
        /// </summary>
        public async Task WriteEntriesAsync(List<SharedTotpEntry> entries)
        {
            lock (_syncLock)
            {
                _isProcessing = true;
            }

            try
            {
                // Mark all entries as modified by PhantomObscura
                foreach (var entry in entries)
                {
                    entry.ModifiedByApp = "PhantomObscura";
                    entry.LastModifiedUtc = DateTimeOffset.UtcNow;
                }

                var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                // Temporarily disable watcher to avoid self-trigger
                if (_watcher != null)
                    _watcher.EnableRaisingEvents = false;

                await File.WriteAllTextAsync(_syncFilePath, json);
                await File.WriteAllTextAsync(_syncFilePath + ".timestamp", DateTimeOffset.UtcNow.Ticks.ToString());

                // Re-enable watcher with minimal delay
                if (_watcher != null)
                {
                    await Task.Delay(50);
                    _watcher.EnableRaisingEvents = true;
                }

                var fileInfo = new FileInfo(_syncFilePath);
                _lastWriteTime = fileInfo.LastWriteTimeUtc;

                Log.Information("Wrote {Count} TOTP entries to sync file", entries.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to write TOTP entries to sync file");
                throw;
            }
            finally
            {
                lock (_syncLock)
                {
                    _isProcessing = false;
                }
            }
        }

        /// <summary>
        /// Exports TOTP entries from vault credentials
        /// </summary>
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
                        LinkedPasswordEntryId = credential.Title, // Use Title as identifier since Credential doesn't have Id
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
        }
    }
}
