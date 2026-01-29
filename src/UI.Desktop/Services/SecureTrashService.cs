using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhantomVault.Core.Models;
using PhantomVault.UI.Models;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Manages the secure rubbish bin, including retention policy, persistence,
    /// restores, and secure wiping of permanently deleted credentials.
    /// </summary>
    public sealed class SecureTrashService
    {
        private readonly object _gate = new();
        private readonly string _storagePath;
        private readonly List<SecureTrashRecord> _records = new();

        public SecureTrashService()
        {
            var settingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhantomVault");
            Directory.CreateDirectory(settingsDir);
            _storagePath = Path.Combine(settingsDir, "secure-trash.json");
            Load();
        }

        public bool IsEnabled { get; private set; } = true;
        public bool AutoPurgeEnabled { get; private set; } = true;
        public int RetentionDays { get; private set; } = 30;
        public int SecureWipePasses { get; private set; } = 3;

        public IReadOnlyList<SecureTrashRecord> Records
        {
            get
            {
                lock (_gate)
                {
                    return _records
                        .Where(r => !r.SecurelyPurged)
                        .OrderBy(r => r.DeletedUtc)
                        .Select(CloneRecord)
                        .ToList();
                }
            }
        }

        public void ApplyConfiguration(bool enabled, bool autoPurgeEnabled, int retentionDays, int secureWipePasses)
        {
            IsEnabled = enabled;
            AutoPurgeEnabled = autoPurgeEnabled;
            RetentionDays = Math.Clamp(retentionDays, 1, 365);
            SecureWipePasses = Math.Clamp(secureWipePasses, 1, 10);
            if (AutoPurgeEnabled)
            {
                PurgeExpiredInternal();
            }
        }

        public SecureTrashRecord MoveToTrash(Credential credential)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));

            var snapshot = CloneCredential(credential);
            var record = new SecureTrashRecord
            {
                Payload = snapshot,
                OriginalGroup = credential.Group,
                DeletedUtc = DateTimeOffset.UtcNow,
                ScheduledPurgeUtc = AutoPurgeEnabled
                    ? DateTimeOffset.UtcNow.AddDays(RetentionDays)
                    : null,
                SecurelyPurged = false
            };

            lock (_gate)
            {
                _records.RemoveAll(r => r.Payload == null);
                _records.Add(record);
                Save();
            }

            if (!IsEnabled)
            {
                // Immediately purge when secure trash is disabled.
                SecurelyPurge(record.Id);
            }

            return CloneRecord(record);
        }

        public (bool success, Credential? credential, string? originalGroup) Restore(Guid recordId)
        {
            lock (_gate)
            {
                var record = _records.FirstOrDefault(r => r.Id == recordId);
                if (record == null || record.SecurelyPurged)
                {
                    return (false, null, null);
                }

                _records.Remove(record);
                Save();
                return (true, CloneCredential(record.Payload), record.OriginalGroup);
            }
        }

        public int SecurelyPurgeExpired()
        {
            lock (_gate)
            {
                return PurgeExpiredInternal();
            }
        }

        public bool SecurelyPurge(Guid recordId)
        {
            lock (_gate)
            {
                var record = _records.FirstOrDefault(r => r.Id == recordId);
                if (record == null)
                {
                    return false;
                }

                if (!record.SecurelyPurged)
                {
                    SecurelyErase(record.Payload, SecureWipePasses);
                    record.SecurelyPurged = true;
                    record.PurgedUtc = DateTimeOffset.UtcNow;
                    Save();
                }

                _records.Remove(record);
                Save();
                return true;
            }
        }

        private int PurgeExpiredInternal()
        {
            if (!AutoPurgeEnabled)
            {
                return 0;
            }

            var now = DateTimeOffset.UtcNow;
            var expired = _records.Where(r => r.ScheduledPurgeUtc != null && r.ScheduledPurgeUtc <= now).ToList();
            foreach (var record in expired)
            {
                SecurelyErase(record.Payload, SecureWipePasses);
                record.SecurelyPurged = true;
                record.PurgedUtc = now;
                _records.Remove(record);
            }
            if (expired.Count > 0)
            {
                Save();
            }
            return expired.Count;
        }

        private static void SecurelyErase(Credential credential, int passes)
        {
            if (credential == null)
            {
                return;
            }

            passes = Math.Clamp(passes, 1, 10);
            using var rng = RandomNumberGenerator.Create();
            for (var pass = 0; pass < passes; pass++)
            {
                credential.Title = RandomString(rng, credential.Title?.Length ?? 12);
                credential.Username = RandomString(rng, credential.Username?.Length ?? 12);
                credential.Password = RandomString(rng, credential.Password?.Length ?? 24);
                credential.Url = RandomString(rng, credential.Url?.Length ?? 16);
                credential.Notes = RandomString(rng, credential.Notes?.Length ?? 64);
                credential.Group = RandomString(rng, credential.Group?.Length ?? 12);
                if (credential.Tags != null)
                {
                    for (int i = 0; i < credential.Tags.Count; i++)
                    {
                        credential.Tags[i] = RandomString(rng, credential.Tags[i]?.Length ?? 8);
                    }
                }
            }
        }

        private static string RandomString(RandomNumberGenerator rng, int length)
        {
            length = Math.Max(length, 8);
            Span<byte> buffer = stackalloc byte[length];
            rng.GetBytes(buffer);
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                var b = buffer[i] % 62;
                char c = (char)(b < 10 ? '0' + b : b < 36 ? 'A' + (b - 10) : 'a' + (b - 36));
                sb.Append(c);
            }
            return sb.ToString();
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_storagePath))
                {
                    var json = File.ReadAllText(_storagePath);
                    var data = JsonSerializer.Deserialize<List<SecureTrashRecord>>(json);
                    if (data != null)
                    {
                        _records.Clear();
                        _records.AddRange(data);
                    }
                }
            }
            catch
            {
                _records.Clear();
            }
        }

        private void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(_records, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_storagePath, json);
            }
            catch
            {
                // best effort
            }
        }

        private static Credential CloneCredential(Credential credential)
        {
            var json = JsonSerializer.Serialize(credential);
            return JsonSerializer.Deserialize<Credential>(json) ?? new Credential();
        }

        private static SecureTrashRecord CloneRecord(SecureTrashRecord record)
        {
            var json = JsonSerializer.Serialize(record);
            return JsonSerializer.Deserialize<SecureTrashRecord>(json) ?? new SecureTrashRecord();
        }
    }
}
