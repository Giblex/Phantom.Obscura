using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Services.Integrity;

public enum IntegrityChangeKind
{
    Created,
    Modified,
    Deleted,
    Renamed,
    Unexpected,
    ManifestMismatch,
    MonitorOverflow,
    BaselineVerified
}

public enum IntegrityChangeOrigin
{
    AuthorizedApplicationWrite,
    ExternalOrUnknown
}

public sealed record IntegrityFileRecord(
    string RelativePath,
    long Length,
    DateTimeOffset LastWriteUtc,
    string Sha256);

public sealed record IntegrityManifest(
    int SchemaVersion,
    string RootLabel,
    DateTimeOffset CreatedUtc,
    IReadOnlyList<IntegrityFileRecord> Files,
    string Algorithm,
    string? KeyId,
    string? Signature,
    long ReleaseSequence = 0);

public sealed record IntegrityEvent(
    long Sequence,
    DateTimeOffset TimestampUtc,
    IntegrityChangeKind Kind,
    IntegrityChangeOrigin Origin,
    string RelativePath,
    string? PreviousRelativePath,
    string? Sha256,
    string? AuthorizationId,
    string PreviousHash,
    string Hash);

public sealed record IntegrityScanResult(
    bool ManifestSignatureValid,
    IReadOnlyList<IntegrityEvent> Changes)
{
    [JsonIgnore]
    public bool IsClean => ManifestSignatureValid && Changes.Count == 0;
}

public sealed class IntegrityControllerOptions
{
    public required string ProtectedRoot { get; init; }
    public required string StateDirectory { get; init; }
    public string RootLabel { get; init; } = "Phantom.Obscura";
    public TimeSpan AuthorizationLifetime { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan PeriodicScanInterval { get; init; } = TimeSpan.FromMinutes(5);
    public IReadOnlyCollection<string> ExcludedRelativePaths { get; init; } = Array.Empty<string>();
}

public sealed record AuthorizedWrite(
    string Id,
    string RelativePath,
    DateTimeOffset ExpiresUtc,
    string? ExpectedOldSha256 = null,
    string? ExpectedNewSha256 = null,
    long? MaximumLength = null,
    IntegrityChangeKind ExpectedChange = IntegrityChangeKind.Modified);

public sealed record IntegrityWriteIntent(
    string RelativePath,
    IntegrityChangeKind ExpectedChange,
    string? ExpectedOldSha256 = null,
    string? ExpectedNewSha256 = null,
    long? MaximumLength = null);
