using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using PhantomVault.Core.Models;

namespace PhantomVault.UI.Services;

public sealed class ObscuraCredentialIndexService
{
    private const string IndexFileName = "obscura-search-index.pidx";
    private const string LegacyIndexFileName = "obscura-search-index.json";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Phantom.Obscura/SuiteCredentialIndex/v2");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string GetIndexPath(string mountPath)
        => Path.Combine(mountPath, "vaults", IndexFileName);

    public void Delete(string mountPath)
    {
        if (string.IsNullOrWhiteSpace(mountPath))
            return;

        var directory = Path.Combine(mountPath, "vaults");
        foreach (var fileName in new[] { IndexFileName, LegacyIndexFileName, IndexFileName + ".tmp" })
        {
            var path = Path.Combine(directory, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    public async Task ExportAsync(string mountPath, string vaultName, IEnumerable<Credential> credentials)
    {
        if (string.IsNullOrWhiteSpace(mountPath))
            throw new ArgumentException("Mount path is required.", nameof(mountPath));

        var indexPath = GetIndexPath(mountPath);
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);

        var payload = new ObscuraCredentialIndex
        {
            VaultName = string.IsNullOrWhiteSpace(vaultName) ? "My Vault" : vaultName,
            UpdatedUtc = DateTimeOffset.UtcNow,
            Entries = credentials
                .Where(static credential => !credential.IsPasskey)
                .Select(static credential => new ObscuraCredentialIndexEntry
                {
                    Id = credential.Id,
                    Title = credential.Title ?? string.Empty,
                    Username = credential.Username ?? string.Empty,
                    Url = credential.Url ?? string.Empty,
                    Category = credential.Group ?? string.Empty,
                    EntryType = credential.EntryType.ToString(),
                    LastUsedUtc = credential.LastUsedUtc,
                    IsFavorite = credential.IsFavorite
                })
                .OrderBy(static entry => entry.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static entry => entry.Username, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        var plain = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        byte[]? sealedBytes = null;
        var temporaryPath = indexPath + ".tmp";
        try
        {
            sealedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(temporaryPath, sealedBytes).ConfigureAwait(false);
            File.Move(temporaryPath, indexPath, overwrite: true);

            // Remove metadata produced by versions that wrote titles, usernames,
            // and URLs as plaintext. This happens only after the encrypted write
            // commits successfully, so migration cannot destroy the usable index.
            var legacyPath = Path.Combine(Path.GetDirectoryName(indexPath)!, LegacyIndexFileName);
            if (File.Exists(legacyPath))
                File.Delete(legacyPath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            if (sealedBytes is not null)
                CryptographicOperations.ZeroMemory(sealedBytes);
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}

public sealed class ObscuraCredentialIndex
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("vaultName")]
    public string VaultName { get; set; } = string.Empty;

    [JsonPropertyName("updatedUtc")]
    public DateTimeOffset UpdatedUtc { get; set; }

    [JsonPropertyName("entries")]
    public List<ObscuraCredentialIndexEntry> Entries { get; set; } = new();
}

public sealed class ObscuraCredentialIndexEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("entryType")]
    public string EntryType { get; set; } = string.Empty;

    [JsonPropertyName("lastUsedUtc")]
    public DateTimeOffset? LastUsedUtc { get; set; }

    [JsonPropertyName("isFavorite")]
    public bool IsFavorite { get; set; }
}

