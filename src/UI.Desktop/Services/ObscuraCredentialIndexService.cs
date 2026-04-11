using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using PhantomVault.Core.Models;

namespace PhantomVault.UI.Services;

/// <summary>
/// Persists a metadata-only credential index that sibling suite apps can
/// consume without needing direct access to vault secrets.
/// </summary>
public sealed class ObscuraCredentialIndexService
{
    private const string IndexFileName = "obscura-search-index.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string GetIndexPath(string mountPath)
        => Path.Combine(mountPath, "vaults", IndexFileName);

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

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await File.WriteAllTextAsync(indexPath, json).ConfigureAwait(false);
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
