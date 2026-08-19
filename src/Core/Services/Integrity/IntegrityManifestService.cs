using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PhantomVault.Core.Services.Integrity;

public sealed class IntegrityManifestService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public IntegrityManifest Create(
        string root,
        string rootLabel,
        ECDsa signingKey,
        string keyId,
        IEnumerable<string>? excludedRelativePaths = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(signingKey);

        var files = Inventory(root, excludedRelativePaths);
        var unsigned = new IntegrityManifest(1, rootLabel, DateTimeOffset.UtcNow,
            files, "ECDSA-P256-SHA256", keyId, null, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        byte[] signature = signingKey.SignData(CanonicalBytes(unsigned), HashAlgorithmName.SHA256);
        return unsigned with { Signature = Convert.ToBase64String(signature) };
    }

    public bool Verify(IntegrityManifest manifest, ECDsa publicKey)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(publicKey);
        if (manifest.SchemaVersion != 1 || manifest.Algorithm != "ECDSA-P256-SHA256" ||
            string.IsNullOrWhiteSpace(manifest.Signature))
            return false;

        try
        {
            return publicKey.VerifyData(
                CanonicalBytes(manifest with { Signature = null }),
                Convert.FromBase64String(manifest.Signature),
                HashAlgorithmName.SHA256);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public void Write(string path, IntegrityManifest manifest)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + ".tmp." + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
        File.Move(temporary, fullPath, true);
    }

    public IntegrityManifest Read(string path) =>
        JsonSerializer.Deserialize<IntegrityManifest>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException("Integrity manifest is empty or invalid.");

    public IReadOnlyList<IntegrityFileRecord> Inventory(
        string root,
        IEnumerable<string>? excludedRelativePaths = null)
    {
        string canonicalRoot = EnsureRoot(root);
        var excluded = new HashSet<string>(
            (excludedRelativePaths ?? Array.Empty<string>()).Select(NormalizeRelativePath),
            StringComparer.OrdinalIgnoreCase);
        var records = new List<IntegrityFileRecord>();

        foreach (string path in Directory.EnumerateFiles(canonicalRoot, "*", SearchOption.AllDirectories))
        {
            string relative = NormalizeRelativePath(Path.GetRelativePath(canonicalRoot, path));
            if (excluded.Any(e => relative.Equals(e, StringComparison.OrdinalIgnoreCase) ||
                                  relative.StartsWith(e + "/", StringComparison.OrdinalIgnoreCase)))
                continue;

            var info = new FileInfo(path);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Reparse points are forbidden in a protected tree: {relative}");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            string hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            records.Add(new IntegrityFileRecord(relative, info.Length, info.LastWriteTimeUtc, hash));
        }

        return records.OrderBy(x => x.RelativePath, StringComparer.Ordinal).ToArray();
    }

    internal static string NormalizeRelativePath(string path)
    {
        string normalized = path.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized) ||
            normalized.Split('/').Any(part => part is "." or ".."))
            throw new ArgumentException("A safe relative path is required.", nameof(path));
        return normalized;
    }

    private static byte[] CanonicalBytes(IntegrityManifest manifest)
    {
        var canonical = manifest with
        {
            Files = manifest.Files.OrderBy(x => x.RelativePath, StringComparer.Ordinal).ToArray(),
            Signature = null
        };
        return JsonSerializer.SerializeToUtf8Bytes(canonical, JsonOptions);
    }

    private static string EnsureRoot(string root)
    {
        string full = Path.GetFullPath(root);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException(full);
        return full;
    }
}
