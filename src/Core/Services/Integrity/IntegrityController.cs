using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace PhantomVault.Core.Services.Integrity;

public sealed class IntegrityController : IDisposable
{
    private readonly IntegrityControllerOptions _options;
    private readonly IntegrityManifest _manifest;
    private readonly IntegrityManifestService _manifests;
    private readonly ECDsa _publicKey;
    private readonly TamperEvidentIntegrityLog _log;
    private readonly ConcurrentDictionary<string, AuthorizedWrite> _authorizations = new(StringComparer.OrdinalIgnoreCase);
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _scanTimer;
    private readonly string _root;
    private bool _disposed;

    public IntegrityController(IntegrityControllerOptions options, IntegrityManifest manifest,
        IntegrityManifestService manifests, ECDsa publicKey, TamperEvidentIntegrityLog log)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _manifests = manifests ?? throw new ArgumentNullException(nameof(manifests));
        _publicKey = publicKey ?? throw new ArgumentNullException(nameof(publicKey));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _root = Path.GetFullPath(options.ProtectedRoot);
        if (!Directory.Exists(_root)) throw new DirectoryNotFoundException(_root);

        _watcher = new FileSystemWatcher(_root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                           NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            InternalBufferSize = 64 * 1024
        };
        _watcher.Created += (_, e) => RecordWatcherChange(IntegrityChangeKind.Created, e.FullPath, null);
        _watcher.Changed += (_, e) => RecordWatcherChange(IntegrityChangeKind.Modified, e.FullPath, null);
        _watcher.Deleted += (_, e) => RecordWatcherChange(IntegrityChangeKind.Deleted, e.FullPath, null);
        _watcher.Renamed += (_, e) => RecordWatcherChange(IntegrityChangeKind.Renamed, e.FullPath, e.OldFullPath);
        _watcher.Error += (_, _) => Record(IntegrityChangeKind.MonitorOverflow,
            IntegrityChangeOrigin.ExternalOrUnknown, "[watcher]", null, null, null);
        _scanTimer = new Timer(_ => SafePeriodicScan(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event EventHandler<IntegrityEvent>? ChangeDetected;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _watcher.EnableRaisingEvents = true;
        _scanTimer.Change(_options.PeriodicScanInterval, _options.PeriodicScanInterval);
    }

    public AuthorizedWrite AuthorizeWrite(string relativePath)
        => AuthorizeWrite(new IntegrityWriteIntent(relativePath, IntegrityChangeKind.Modified));

    public AuthorizedWrite AuthorizeWrite(IntegrityWriteIntent intent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(intent);
        string normalized = IntegrityManifestService.NormalizeRelativePath(intent.RelativePath);
        if (intent.MaximumLength is < 0)
            throw new ArgumentOutOfRangeException(nameof(intent), "Maximum write length cannot be negative.");
        string fullPath = Path.GetFullPath(Path.Combine(_root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        EnsureContained(fullPath);
        string? currentHash = TryHash(fullPath);
        if (!string.IsNullOrWhiteSpace(intent.ExpectedOldSha256) &&
            !string.Equals(currentHash, intent.ExpectedOldSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The file changed before write authorization could be issued.");
        var authorization = new AuthorizedWrite(Guid.NewGuid().ToString("N"), normalized,
            DateTimeOffset.UtcNow.Add(_options.AuthorizationLifetime), intent.ExpectedOldSha256,
            intent.ExpectedNewSha256, intent.MaximumLength, intent.ExpectedChange);
        _authorizations[normalized] = authorization;
        return authorization;
    }

    public IntegrityScanResult Scan()
    {
        bool signatureValid = _manifests.Verify(_manifest, _publicKey);
        var changes = new List<IntegrityEvent>();
        if (!signatureValid)
            changes.Add(Record(IntegrityChangeKind.ManifestMismatch, IntegrityChangeOrigin.ExternalOrUnknown,
                "[manifest]", null, null, null));

        var expected = _manifest.Files.ToDictionary(x => x.RelativePath, StringComparer.OrdinalIgnoreCase);
        var current = _manifests.Inventory(_root, EffectiveExclusions())
            .ToDictionary(x => x.RelativePath, StringComparer.OrdinalIgnoreCase);

        foreach ((string path, IntegrityFileRecord record) in current)
        {
            if (!expected.TryGetValue(path, out var baseline))
                changes.Add(Record(IntegrityChangeKind.Unexpected, OriginFor(path, IntegrityChangeKind.Created, record.Sha256, record.Length, out string? id), path, null, record.Sha256, id));
            else if (!CryptographicOperations.FixedTimeEquals(
                         Convert.FromHexString(record.Sha256), Convert.FromHexString(baseline.Sha256)))
                changes.Add(Record(IntegrityChangeKind.Modified, OriginFor(path, IntegrityChangeKind.Modified, record.Sha256, record.Length, out string? id), path, null, record.Sha256, id));
        }
        foreach (string path in expected.Keys.Except(current.Keys, StringComparer.OrdinalIgnoreCase))
            changes.Add(Record(IntegrityChangeKind.Deleted, OriginFor(path, IntegrityChangeKind.Deleted, null, null, out string? id), path, null, null, id));
        return new IntegrityScanResult(signatureValid, changes);
    }

    private void RecordWatcherChange(IntegrityChangeKind kind, string fullPath, string? oldFullPath)
    {
        if (_disposed || Directory.Exists(fullPath) || IsExcluded(fullPath)) return;
        string relative = IntegrityManifestService.NormalizeRelativePath(Path.GetRelativePath(_root, fullPath));
        string? oldRelative = oldFullPath is null ? null : IntegrityManifestService.NormalizeRelativePath(Path.GetRelativePath(_root, oldFullPath));
        string? hash = TryHash(fullPath);
        long? length = File.Exists(fullPath) ? new FileInfo(fullPath).Length : null;
        var origin = OriginFor(relative, kind, hash, length, out string? id);
        Record(kind, origin, relative, oldRelative, hash, id);
    }

    private IntegrityChangeOrigin OriginFor(string path, IntegrityChangeKind observedChange,
        string? observedHash, long? observedLength, out string? authorizationId)
    {
        authorizationId = null;
        if (_authorizations.TryRemove(path, out var authorization) &&
            authorization.ExpiresUtc >= DateTimeOffset.UtcNow &&
            ChangesMatch(authorization.ExpectedChange, observedChange) &&
            (authorization.MaximumLength is null || observedLength <= authorization.MaximumLength) &&
            (authorization.ExpectedNewSha256 is null ||
             string.Equals(authorization.ExpectedNewSha256, observedHash, StringComparison.OrdinalIgnoreCase)))
        {
            authorizationId = authorization.Id;
            return IntegrityChangeOrigin.AuthorizedApplicationWrite;
        }
        return IntegrityChangeOrigin.ExternalOrUnknown;
    }

    private static bool ChangesMatch(IntegrityChangeKind expected, IntegrityChangeKind observed) =>
        expected == observed || (expected == IntegrityChangeKind.Created && observed == IntegrityChangeKind.Unexpected);

    private void EnsureContained(string fullPath)
    {
        string rooted = _root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rooted, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Integrity write target escapes the protected root.");
    }

    private IntegrityEvent Record(IntegrityChangeKind kind, IntegrityChangeOrigin origin, string path,
        string? oldPath, string? hash, string? authorizationId)
    {
        var entry = _log.Append(new IntegrityEvent(0, DateTimeOffset.UtcNow, kind, origin, path,
            oldPath, hash, authorizationId, string.Empty, string.Empty));
        ChangeDetected?.Invoke(this, entry);
        return entry;
    }

    private IEnumerable<string> EffectiveExclusions()
    {
        foreach (string path in _options.ExcludedRelativePaths) yield return path;
        string state = Path.GetRelativePath(_root, Path.GetFullPath(_options.StateDirectory));
        if (!state.StartsWith("..", StringComparison.Ordinal)) yield return state;
    }

    private bool IsExcluded(string fullPath)
    {
        string relative = Path.GetRelativePath(_root, fullPath).Replace('\\', '/');
        return EffectiveExclusions().Any(x => relative.Equals(x, StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith(x.TrimEnd('/', '\\') + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryHash(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private void SafePeriodicScan()
    {
        try { Scan(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            Record(IntegrityChangeKind.MonitorOverflow, IntegrityChangeOrigin.ExternalOrUnknown,
                "[periodic-scan-failed]", null, null, null);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher.Dispose();
        _scanTimer.Dispose();
        _publicKey.Dispose();
    }
}
