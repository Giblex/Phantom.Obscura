using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Represents a single import session with rollback capability.
    /// </summary>
    public sealed class ImportSession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string SourceFile { get; set; } = "";
        public string Format { get; set; } = "";
        public int ImportedCount { get; set; }
        public List<string> CredentialIds { get; set; } = new();
        public string Notes { get; set; } = "";
        public bool CanRevert { get; set; } = true;
    }

    /// <summary>
    /// Manages import history and provides rollback capabilities.
    /// </summary>
    public sealed class ImportHistoryService
    {
        private readonly string _historyFilePath;
        private List<ImportSession> _sessions = new();

        public ImportHistoryService(string historyDirectory)
        {
            _historyFilePath = Path.Combine(historyDirectory, "import_history.json");
            LoadHistory();
        }

        /// <summary>
        /// Records a new import session.
        /// </summary>
        public async Task<ImportSession> RecordImportAsync(
            string sourceFile,
            string format,
            List<Credential> importedCredentials,
            string notes = "")
        {
            var session = new ImportSession
            {
                SourceFile = Path.GetFileName(sourceFile),
                Format = format,
                ImportedCount = importedCredentials.Count,
                CredentialIds = importedCredentials.Select(c =>
                    $"{c.Title}|{c.Username}|{c.Url}".GetHashCode().ToString()).ToList(),
                Notes = notes
            };

            _sessions.Add(session);
            await SaveHistoryAsync();

            return session;
        }

        /// <summary>
        /// Gets all import sessions, newest first.
        /// </summary>
        public List<ImportSession> GetAllSessions()
        {
            return _sessions.OrderByDescending(s => s.Timestamp).ToList();
        }

        /// <summary>
        /// Gets import sessions from a specific date range.
        /// </summary>
        public List<ImportSession> GetSessionsByDateRange(DateTimeOffset start, DateTimeOffset end)
        {
            return _sessions
                .Where(s => s.Timestamp >= start && s.Timestamp <= end)
                .OrderByDescending(s => s.Timestamp)
                .ToList();
        }

        /// <summary>
        /// Gets a specific import session by ID.
        /// </summary>
        public ImportSession? GetSession(string sessionId)
        {
            return _sessions.FirstOrDefault(s => s.Id == sessionId);
        }

        /// <summary>
        /// Marks a session as reverted (cannot be reverted again).
        /// </summary>
        public async Task MarkSessionRevertedAsync(string sessionId)
        {
            var session = GetSession(sessionId);
            if (session != null)
            {
                session.CanRevert = false;
                await SaveHistoryAsync();
            }
        }

        /// <summary>
        /// Deletes old import sessions (older than specified days).
        /// </summary>
        public async Task CleanupOldSessionsAsync(int olderThanDays = 90)
        {
            var cutoffDate = DateTimeOffset.UtcNow.AddDays(-olderThanDays);
            _sessions.RemoveAll(s => s.Timestamp < cutoffDate);
            await SaveHistoryAsync();
        }

        /// <summary>
        /// Gets summary statistics for import history.
        /// </summary>
        public ImportHistorySummary GetSummary()
        {
            return new ImportHistorySummary
            {
                TotalSessions = _sessions.Count,
                TotalCredentialsImported = _sessions.Sum(s => s.ImportedCount),
                LastImportDate = _sessions.Any() ? _sessions.Max(s => s.Timestamp) : (DateTimeOffset?)null,
                RevertableSessions = _sessions.Count(s => s.CanRevert)
            };
        }

        private void LoadHistory()
        {
            try
            {
                if (File.Exists(_historyFilePath))
                {
                    var json = File.ReadAllText(_historyFilePath);
                    _sessions = JsonSerializer.Deserialize<List<ImportSession>>(json) ?? new List<ImportSession>();
                }
            }
            catch
            {
                _sessions = new List<ImportSession>();
            }
        }

        private async Task SaveHistoryAsync()
        {
            try
            {
                var directory = Path.GetDirectoryName(_historyFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_sessions, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                await File.WriteAllTextAsync(_historyFilePath, json);
            }
            catch
            {
                // Log error but don't throw - history is not critical
            }
        }
    }

    /// <summary>
    /// Summary statistics for import history.
    /// </summary>
    public sealed class ImportHistorySummary
    {
        public int TotalSessions { get; set; }
        public int TotalCredentialsImported { get; set; }
        public DateTimeOffset? LastImportDate { get; set; }
        public int RevertableSessions { get; set; }
    }
}
