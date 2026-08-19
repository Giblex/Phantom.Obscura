using System.Security.Cryptography;
using System.IO.Compression;
using System.Text.Json;
using PhantomVault.Core.Services.Integrity;

return args switch
{
    ["keygen", string privateKeyPath, string publicKeyPath] => GenerateKeys(privateKeyPath, publicKeyPath),
    ["sign", string root, string privateKeyPath, string outputManifest] => Sign(root, privateKeyPath, outputManifest),
    ["verify", string root, string publicKeyPath, string manifestPath] => Verify(root, publicKeyPath, manifestPath),
    ["verify-anchors", string receiptsPath] => VerifyAnchors(receiptsPath),
    ["evidence", string stateDirectory, string outputArchive] => ExportEvidence(stateDirectory, outputArchive),
    _ => Usage()
};

static int GenerateKeys(string privateKeyPath, string publicKeyPath)
{
    RefuseOverwrite(privateKeyPath);
    RefuseOverwrite(publicKeyPath);
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    WritePrivateFile(privateKeyPath, key.ExportECPrivateKeyPem());
    File.WriteAllText(publicKeyPath, key.ExportSubjectPublicKeyInfoPem());
    Console.WriteLine($"Created offline private key: {Path.GetFullPath(privateKeyPath)}");
    Console.WriteLine($"Created distributable public key: {Path.GetFullPath(publicKeyPath)}");
    return 0;
}

static int Sign(string root, string privateKeyPath, string outputManifest)
{
    using var key = ECDsa.Create();
    key.ImportFromPem(File.ReadAllText(privateKeyPath));
    string publicKeyHash = Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
    var service = new IntegrityManifestService();
    string[] exclusions = RelativeExclusion(root, outputManifest);
    var manifest = service.Create(root, "Phantom.Obscura", key, publicKeyHash, exclusions);
    service.Write(outputManifest, manifest);
    Console.WriteLine($"Signed {manifest.Files.Count} files to {Path.GetFullPath(outputManifest)}");
    return 0;
}

static int Verify(string root, string publicKeyPath, string manifestPath)
{
    using var key = ECDsa.Create();
    key.ImportFromPem(File.ReadAllText(publicKeyPath));
    var service = new IntegrityManifestService();
    var manifest = service.Read(manifestPath);
    if (!service.Verify(manifest, key))
    {
        Console.Error.WriteLine("Manifest signature is invalid.");
        return 2;
    }

    var expected = manifest.Files.ToDictionary(x => x.RelativePath, StringComparer.OrdinalIgnoreCase);
    var current = service.Inventory(root, RelativeExclusion(root, manifestPath)).ToDictionary(x => x.RelativePath, StringComparer.OrdinalIgnoreCase);
    var mismatches = expected.Keys.Union(current.Keys, StringComparer.OrdinalIgnoreCase)
        .Where(path => !expected.TryGetValue(path, out var left) ||
                       !current.TryGetValue(path, out var right) ||
                       !string.Equals(left.Sha256, right.Sha256, StringComparison.Ordinal))
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    foreach (string mismatch in mismatches) Console.Error.WriteLine(mismatch);
    Console.WriteLine(mismatches.Length == 0 ? "Signature and file inventory verified." : $"Detected {mismatches.Length} mismatch(es).");
    return mismatches.Length == 0 ? 0 : 3;
}

static void WritePrivateFile(string path, string contents)
{
    string full = Path.GetFullPath(path);
    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
    File.WriteAllText(full, contents);
    if (OperatingSystem.IsWindows())
    {
        // Do not pretend a generic developer machine has an HSM. The offline key
        // must be moved into the release secret store immediately after creation.
        File.SetAttributes(full, File.GetAttributes(full) | FileAttributes.Hidden);
    }
}

static void RefuseOverwrite(string path)
{
    if (File.Exists(path)) throw new IOException($"Refusing to overwrite existing key: {Path.GetFullPath(path)}");
}

static string[] RelativeExclusion(string root, string path)
{
    string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    string fullPath = Path.GetFullPath(path);
    return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
        ? [Path.GetRelativePath(root, fullPath)]
        : [];
}

static int Usage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  integrity keygen <private.pem> <public.pem>");
    Console.Error.WriteLine("  integrity sign <publish-root> <private.pem> <manifest.json>");
    Console.Error.WriteLine("  integrity verify <publish-root> <public.pem> <manifest.json>");
    Console.Error.WriteLine("  integrity verify-anchors <phantom-key-anchors.jsonl>");
    Console.Error.WriteLine("  integrity evidence <watchdog-state-dir> <evidence.zip>");
    return 64;
}

static int VerifyAnchors(string receiptsPath)
{
    long previous = 0;
    int count = 0;
    foreach (string line in File.ReadLines(receiptsPath))
    {
        var receipt = JsonSerializer.Deserialize<IntegrityAnchorReceipt>(line);
        if (receipt is null || receipt.Sequence <= previous || !IntegrityAnchorCoordinator.VerifyReceipt(receipt))
        {
            Console.Error.WriteLine($"Anchor verification failed at line {count + 1}.");
            return 4;
        }
        previous = receipt.Sequence;
        count++;
    }
    Console.WriteLine($"Verified {count} Phantom Key integrity anchor receipt(s).");
    return 0;
}

static int ExportEvidence(string stateDirectory, string outputArchive)
{
    string root = Path.GetFullPath(stateDirectory);
    string output = Path.GetFullPath(outputArchive);
    if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
    if (File.Exists(output)) throw new IOException($"Refusing to overwrite evidence archive: {output}");
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    using var archive = ZipFile.Open(output, ZipArchiveMode.Create);
    var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
    foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
    {
        string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        if (relative.Equals("audit-key.dpapi", StringComparison.OrdinalIgnoreCase)) continue;
        byte[] bytes = File.ReadAllBytes(file);
        hashes[relative] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var entry = archive.CreateEntry("state/" + relative, CompressionLevel.Optimal);
        using Stream destination = entry.Open();
        destination.Write(bytes);
        CryptographicOperations.ZeroMemory(bytes);
    }
    var index = archive.CreateEntry("evidence-index.json", CompressionLevel.Optimal);
    using (var writer = new StreamWriter(index.Open()))
        writer.Write(JsonSerializer.Serialize(new { CreatedUtc = DateTimeOffset.UtcNow, Files = hashes }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Exported {hashes.Count} evidence file(s) without DPAPI key material to {output}");
    return 0;
}
