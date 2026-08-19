using System;
using System.Globalization;
using System.IO;

namespace PhantomVault.Core.Services.Integrity;

/// <summary>Rejects correctly signed manifests older than the highest accepted release.</summary>
public sealed class IntegrityRollbackGuard
{
    private readonly string _statePath;
    private readonly object _gate = new();

    public IntegrityRollbackGuard(string statePath) => _statePath = Path.GetFullPath(statePath);

    public void AcceptOrThrow(IntegrityManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.ReleaseSequence <= 0)
            throw new InvalidDataException("Manifest has no valid anti-rollback release sequence.");
        lock (_gate)
        {
            long highest = ReadHighest();
            if (manifest.ReleaseSequence < highest)
                throw new InvalidDataException(
                    $"Signed release rollback rejected: {manifest.ReleaseSequence} is older than {highest}.");
            if (manifest.ReleaseSequence > highest)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
                string temporary = _statePath + ".tmp." + Guid.NewGuid().ToString("N");
                File.WriteAllText(temporary, manifest.ReleaseSequence.ToString(CultureInfo.InvariantCulture));
                File.Move(temporary, _statePath, true);
            }
        }
    }

    public long ReadHighest()
    {
        if (!File.Exists(_statePath)) return 0;
        string text = File.ReadAllText(_statePath).Trim();
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long value) || value < 0)
            throw new InvalidDataException("Anti-rollback state is corrupt.");
        return value;
    }
}
