using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PhantomVault.UI.Models;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Creates encrypted shadow snapshots of deleted items for limited restore.
    /// Stores only metadata and optional encrypted payloads; governed by policy/cert bundle.
    /// </summary>
    public sealed class ShadowTrashService
    {
        private readonly EncryptionService _encryptionService;

        public sealed record ShadowSnapshotItem(string? Path, long? Size, string? OriginalGroup, DateTimeOffset? DeletedUtc, bool IsSecureTrash);
        public sealed record ShadowSnapshotResult(DateTimeOffset CreatedUtc, List<ShadowSnapshotItem> Items);

        public ShadowTrashService(EncryptionService encryptionService)
        {
            _encryptionService = encryptionService;
        }

        public async Task<string> CreateShadowSnapshotAsync(
            IEnumerable<string> filePaths,
            IEnumerable<SecureTrashRecord>? trashRecords,
            string policyPath,
            string certPath,
            string outputDirectory,
            string passphrase,
            string pin)
        {
            Directory.CreateDirectory(outputDirectory);

            var items = new List<object>();
            foreach (var file in filePaths)
            {
                if (!File.Exists(file)) continue;
                var info = new FileInfo(file);
                items.Add(new
                {
                    path = file,
                    size = info.Length,
                    modifiedUtc = info.LastWriteTimeUtc
                });
            }

            if (trashRecords != null)
            {
                foreach (var record in trashRecords)
                {
                    items.Add(new
                    {
                        type = "secure-trash",
                        id = record.Id,
                        deletedUtc = record.DeletedUtc,
                        scheduledPurgeUtc = record.ScheduledPurgeUtc,
                        originalGroup = record.OriginalGroup,
                        titleHash = HashUtf8(record.Payload?.Title ?? string.Empty),
                        usernameHash = HashUtf8(record.Payload?.Username ?? string.Empty),
                        notesLength = record.Payload?.Notes?.Length ?? 0,
                        tagCount = record.Payload?.Tags?.Count ?? 0
                    });
                }
            }

            var payload = new
            {
                createdUtc = DateTimeOffset.UtcNow,
                policy = File.Exists(policyPath) ? Convert.ToBase64String(await File.ReadAllBytesAsync(policyPath)) : null,
                rootCert = File.Exists(certPath) ? Convert.ToBase64String(await File.ReadAllBytesAsync(certPath)) : null,
                items
            };

            var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
            var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

            var salt = _encryptionService.GenerateSalt();
            var combinedSecret = $"{passphrase}{pin}";
            var key = _encryptionService.DeriveKey(combinedSecret.AsSpan(), salt, 32);

            var aad = Encoding.UTF8.GetBytes("shadow-trash");
            var enc = _encryptionService.Encrypt(payloadBytes, key, aad);

            var envelope = new
            {
                version = "1.0",
                kdf = new { salt = Convert.ToBase64String(salt), memoryKb = 64 * 1024, iterations = 3, parallelism = 0 },
                crypto = new
                {
                    nonce = Convert.ToBase64String(enc.Nonce),
                    tag = Convert.ToBase64String(enc.Tag),
                    ciphertext = Convert.ToBase64String(enc.Ciphertext),
                    aad = Convert.ToBase64String(aad)
                }
            };

            var outPath = Path.Combine(outputDirectory, $"shadow-trash-{DateTime.UtcNow:yyyyMMddHHmmss}.pvshadow");
            await File.WriteAllTextAsync(outPath, JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true }));
            return outPath;
        }

        public async Task<ShadowSnapshotResult> RestoreSnapshotAsync(string snapshotPath, string passphrase, string pin)
        {
            if (!File.Exists(snapshotPath)) throw new FileNotFoundException("Snapshot not found", snapshotPath);
            var json = await File.ReadAllTextAsync(snapshotPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var kdf = root.GetProperty("kdf");
            var salt = Convert.FromBase64String(kdf.GetProperty("salt").GetString() ?? throw new InvalidDataException("salt missing"));
            var memoryKb = kdf.GetProperty("memoryKb").GetInt32();
            var iterations = kdf.GetProperty("iterations").GetInt32();
            var parallelism = kdf.GetProperty("parallelism").GetInt32();

            var crypto = root.GetProperty("crypto");
            var nonce = Convert.FromBase64String(crypto.GetProperty("nonce").GetString() ?? string.Empty);
            var tag = Convert.FromBase64String(crypto.GetProperty("tag").GetString() ?? string.Empty);
            var ciphertext = Convert.FromBase64String(crypto.GetProperty("ciphertext").GetString() ?? string.Empty);
            var aad = Convert.FromBase64String(crypto.GetProperty("aad").GetString() ?? string.Empty);

            var combinedSecret = $"{passphrase}{pin}";
            var key = _encryptionService.DeriveKey(combinedSecret.AsSpan(), salt, 32, memoryKb, iterations, parallelism);
            var plaintext = _encryptionService.Decrypt(ciphertext, nonce, tag, key, aad);

            var payload = JsonDocument.Parse(plaintext);
            var createdUtc = payload.RootElement.GetProperty("createdUtc").GetDateTimeOffset();
            var itemsEl = payload.RootElement.GetProperty("items");

            var items = new List<ShadowSnapshotItem>();
            foreach (var el in itemsEl.EnumerateArray())
            {
                var path = el.TryGetProperty("path", out var p) ? p.GetString() : null;
                var size = el.TryGetProperty("size", out var s) ? s.GetInt64() : (long?)null;
                var originalGroup = el.TryGetProperty("originalGroup", out var og) ? og.GetString() : null;
                var deletedUtc = el.TryGetProperty("deletedUtc", out var du) ? du.GetDateTimeOffset() : (DateTimeOffset?)null;
                var isSecure = el.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "secure-trash";
                items.Add(new ShadowSnapshotItem(path, size, originalGroup, deletedUtc, isSecure));
            }

            return new ShadowSnapshotResult(createdUtc, items);
        }

        private static string HashUtf8(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var hash = SHA256.HashData(bytes);
            return Convert.ToBase64String(hash);
        }
    }
}
