using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services
{

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

    public sealed class ImportHistoryService
    {
        private readonly string _historyFilePath;
        private List<ImportSession> _sessions = new();

        public ImportHistoryService(string historyDirectory)
        {
            _historyFilePath = Path.Combine(historyDirectory, "import_history.json");
            LoadHistory();
        }

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

        public List<ImportSession> GetAllSessions()
        {
            return _sessions.OrderByDescending(s => s.Timestamp).ToList();
        }

        public List<ImportSession> GetSessionsByDateRange(DateTimeOffset start, DateTimeOffset end)
        {
            return _sessions
                .Where(s => s.Timestamp >= start && s.Timestamp <= end)
                .OrderByDescending(s => s.Timestamp)
                .ToList();
        }

        public ImportSession? GetSession(string sessionId)
        {
            return _sessions.FirstOrDefault(s => s.Id == sessionId);
        }

        public async Task MarkSessionRevertedAsync(string sessionId)
        {
            var session = GetSession(sessionId);
            if (session != null)
            {
                session.CanRevert = false;
                await SaveHistoryAsync();
            }
        }

        public async Task CleanupOldSessionsAsync(int olderThanDays = 90)
        {
            var cutoffDate = DateTimeOffset.UtcNow.AddDays(-olderThanDays);
            _sessions.RemoveAll(s => s.Timestamp < cutoffDate);
            await SaveHistoryAsync();
        }

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

            }
        }
    }

    public sealed class ImportHistorySummary
    {
        public int TotalSessions { get; set; }
        public int TotalCredentialsImported { get; set; }
        public DateTimeOffset? LastImportDate { get; set; }
        public int RevertableSessions { get; set; }
    }
}

