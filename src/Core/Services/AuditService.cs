using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Lightweight audit logger with optional encryption and hash chaining.
    /// </summary>
    public sealed class AuditService
    {
        private readonly EncryptionService? _encryptionService;
        private byte[]? _auditLogKey;

        public AuditService(EncryptionService? encryptionService = null)
        {
            _encryptionService = encryptionService;
        }

        /// <summary>Call once after vault unlock to derive the audit log key.</summary>
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

        /// <summary>
        /// Append an audit entry. If encryption is not initialized, falls back to plaintext logging.
        /// </summary>
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
                    catch { /* ignore */ }
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

        /// <summary>Verify encrypted audit log chain. Returns true if valid.</summary>
        public bool VerifyAuditLog(string logFilePath, out string? error)
        {
            error = null;
            if (!File.Exists(logFilePath))
                return true;

            string expectedPrev = string.Empty;

            foreach (var line in File.ReadLines(logFilePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    AuditEntry entry;
                    if (_encryptionService != null && _auditLogKey != null)
                    {
                        var enc = JsonSerializer.Deserialize<EncryptedAuditEntry>(line);
                        if (enc == null)
                            throw new InvalidOperationException("Invalid encrypted entry");

                        var ciphertext = Convert.FromBase64String(enc.Ciphertext);
                        var nonce = Convert.FromBase64String(enc.Nonce);
                        var tag = Convert.FromBase64String(enc.Tag);
                        var aad = Encoding.UTF8.GetBytes(enc.PrevHash ?? string.Empty);
                        var plain = _encryptionService.Decrypt(ciphertext, nonce, tag, _auditLogKey, aad);
                        entry = JsonSerializer.Deserialize<AuditEntry>(plain)!;
                    }
                    else
                    {
                        entry = JsonSerializer.Deserialize<AuditEntry>(line)!;
                    }

                    if (entry.PreviousHash != expectedPrev)
                    {
                        error = $"Hash chain broken at {entry.Timestamp}";
                        return false;
                    }
                    expectedPrev = entry.Hash;
                }
                catch (Exception ex)
                {
                    error = $"Audit log verification failed: {ex.Message}";
                    return false;
                }
            }

            return true;
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
