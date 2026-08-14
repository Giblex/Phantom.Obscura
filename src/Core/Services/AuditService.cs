using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PhantomVault.Core.Services
{

    public sealed class AuditService
    {
        private readonly EncryptionService? _encryptionService;
        private byte[]? _auditLogKey;

        public AuditService(EncryptionService? encryptionService = null)
        {
            _encryptionService = encryptionService;
        }

        public void InitializeEncryption(byte[] vaultMasterKey, byte[] salt)
        {
            if (_encryptionService == null)
                throw new InvalidOperationException("EncryptionService not provided");

            using var hmac = new HMACSHA512(salt);
            var ikm = hmac.ComputeHash(vaultMasterKey);
            _auditLogKey = new byte[32];
            Array.Copy(ikm, 0, _auditLogKey, 0, 32);
        }

        public record AuditEntry(DateTimeOffset Timestamp, string Category, string Message, string? PreviousHash, string Hash);

        public void LogEvent(string logFilePath, string category, string message)
        {
            if (string.IsNullOrWhiteSpace(logFilePath))
                throw new ArgumentException("Log file path must be provided", nameof(logFilePath));
            if (category == null) throw new ArgumentNullException(nameof(category));
            if (message == null) throw new ArgumentNullException(nameof(message));

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logFilePath))!);

            string? prevHash = null;
            if (File.Exists(logFilePath))
            {
                var lastLine = File.ReadLines(logFilePath).LastOrDefault();
                if (!string.IsNullOrEmpty(lastLine))
                {
                    try
                    {
                        var last = JsonSerializer.Deserialize<AuditEntry>(lastLine);
                        prevHash = last?.Hash;
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning(ex, "[AuditService] Failed to deserialize last audit entry — chain integrity check skipped");
                    }
                }
            }

            var entry = new AuditEntry(DateTimeOffset.UtcNow, category, message, prevHash, string.Empty);
            var entryJson = JsonSerializer.Serialize(entry);
            var entryBytes = Encoding.UTF8.GetBytes(entryJson);

            string hash;
            using (var sha = SHA256.Create())
            {
                hash = Convert.ToBase64String(sha.ComputeHash(entryBytes));
            }

            var finalized = entry with { Hash = hash };

            if (_encryptionService != null && _auditLogKey != null)
            {
                var aad = Encoding.UTF8.GetBytes(prevHash ?? string.Empty);
                var enc = _encryptionService.Encrypt(entryBytes, _auditLogKey, aad);
                var stored = new EncryptedAuditEntry
                {
                    Nonce = Convert.ToBase64String(enc.Nonce),
                    Tag = Convert.ToBase64String(enc.Tag),
                    Ciphertext = Convert.ToBase64String(enc.Ciphertext),
                    Timestamp = finalized.Timestamp.ToUnixTimeSeconds(),
                    PrevHash = prevHash ?? string.Empty
                };
                File.AppendAllText(logFilePath, JsonSerializer.Serialize(stored) + Environment.NewLine);
            }
            else
            {
                File.AppendAllText(logFilePath, JsonSerializer.Serialize(finalized) + Environment.NewLine);
            }
        }

        public bool VerifyAuditLog(string logFilePath, out string? error)
        {
            var result = ReadEntries(logFilePath);
            error = result.Error;
            return result.ChainValid;
        }

        /// <summary>
        /// Decodes every entry from a (plaintext or encrypted) audit log, verifying the
        /// hash chain as it goes. Used by the in-app Security Activity viewer. On a chain
        /// break it returns the entries decoded so far plus an explanatory error, so the UI
        /// can show partial history and flag tampering rather than failing silently.
        /// </summary>
        public AuditReadResult ReadEntries(string logFilePath)
        {
            var entries = new List<AuditEntry>();
            if (string.IsNullOrWhiteSpace(logFilePath) || !File.Exists(logFilePath))
                return new AuditReadResult(entries, true, null);

            string expectedPrev = string.Empty;

            foreach (var line in File.ReadLines(logFilePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    AuditEntry entry;
                    bool verifyContentHash = false;
                    if (_encryptionService != null && _auditLogKey != null)
                    {
                        var enc = JsonSerializer.Deserialize<EncryptedAuditEntry>(line);
                        if (enc == null)
                            throw new InvalidOperationException("Invalid encrypted entry");

                        var ciphertext = Convert.FromBase64String(enc.Ciphertext);
                        var nonce = Convert.FromBase64String(enc.Nonce);
                        var tag = Convert.FromBase64String(enc.Tag);
                        var aad = Encoding.UTF8.GetBytes(enc.PrevHash ?? string.Empty);
                        // AEAD (with PrevHash as AAD) authenticates both content and linkage.
                        var plain = _encryptionService.Decrypt(ciphertext, nonce, tag, _auditLogKey, aad);
                        entry = JsonSerializer.Deserialize<AuditEntry>(plain)!;
                        entry = entry with { PreviousHash = enc.PrevHash };
                    }
                    else
                    {
                        entry = JsonSerializer.Deserialize<AuditEntry>(line)!;
                        verifyContentHash = true;
                    }

                    if ((entry.PreviousHash ?? string.Empty) != expectedPrev)
                        return new AuditReadResult(entries, false, $"Hash chain broken at {entry.Timestamp:u}");

                    if (verifyContentHash)
                    {
                        // Recompute the entry hash over its content (with Hash blanked, matching
                        // how LogEvent derives it) so an edited line is detected even if its
                        // stored hash field was left untouched.
                        var recomputed = ComputeEntryHash(entry);
                        if (!string.Equals(recomputed, entry.Hash, StringComparison.Ordinal))
                            return new AuditReadResult(entries, false, $"Entry content altered at {entry.Timestamp:u}");
                    }

                    expectedPrev = entry.Hash;
                    entries.Add(entry);
                }
                catch (Exception ex)
                {
                    return new AuditReadResult(entries, false, $"Audit log read failed: {ex.Message}");
                }
            }

            return new AuditReadResult(entries, true, null);
        }

        public sealed record AuditReadResult(IReadOnlyList<AuditEntry> Entries, bool ChainValid, string? Error);

        private static string ComputeEntryHash(AuditEntry entry)
        {
            var basis = entry with { Hash = string.Empty };
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(basis));
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(bytes));
        }

        private record EncryptedAuditEntry
        {
            public string Nonce { get; init; } = string.Empty;
            public string Tag { get; init; } = string.Empty;
            public string Ciphertext { get; init; } = string.Empty;
            public long Timestamp { get; init; }
            public string PrevHash { get; init; } = string.Empty;
        }
    }
}

