using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PhantomVault.Core.Services.Integrity;

public sealed class TamperEvidentIntegrityLog
{
    private readonly string _path;
    private readonly byte[] _key;
    private readonly object _gate = new();

    public TamperEvidentIntegrityLog(string path, ReadOnlySpan<byte> authenticationKey)
    {
        if (authenticationKey.Length < 32)
            throw new ArgumentException("The audit authentication key must be at least 256 bits.", nameof(authenticationKey));
        _path = Path.GetFullPath(path);
        _key = authenticationKey.ToArray();
    }

    public IntegrityEvent Append(IntegrityEvent candidate)
    {
        lock (_gate)
        {
            var entries = ReadAndVerifyCore();
            string previous = entries.Count == 0 ? string.Empty : entries[^1].Hash;
            long sequence = entries.Count == 0 ? 1 : entries[^1].Sequence + 1;
            var pending = candidate with { Sequence = sequence, PreviousHash = previous, Hash = string.Empty };
            string hash = ComputeHash(pending);
            var finalized = pending with { Hash = hash };
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.AppendAllText(_path, JsonSerializer.Serialize(finalized) + Environment.NewLine, new UTF8Encoding(false));
            return finalized;
        }
    }

    public IReadOnlyList<IntegrityEvent> ReadAndVerify()
    {
        lock (_gate) return ReadAndVerifyCore();
    }

    public IntegrityEvent? GetVerifiedHead()
    {
        lock (_gate)
        {
            var entries = ReadAndVerifyCore();
            return entries.Count == 0 ? null : entries[^1];
        }
    }

    private List<IntegrityEvent> ReadAndVerifyCore()
    {
        var result = new List<IntegrityEvent>();
        if (!File.Exists(_path)) return result;
        string previous = string.Empty;
        long expectedSequence = 1;
        foreach (string line in File.ReadLines(_path))
        {
            var entry = JsonSerializer.Deserialize<IntegrityEvent>(line)
                ?? throw new InvalidDataException("Null integrity audit entry.");
            if (entry.Sequence != expectedSequence || entry.PreviousHash != previous ||
                !CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(entry.Hash), Convert.FromHexString(ComputeHash(entry with { Hash = string.Empty }))))
                throw new InvalidDataException($"Integrity audit chain failed at sequence {expectedSequence}.");
            result.Add(entry);
            previous = entry.Hash;
            expectedSequence++;
        }
        return result;
    }

    private string ComputeHash(IntegrityEvent entry)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(entry);
        return Convert.ToHexString(HMACSHA256.HashData(_key, bytes)).ToLowerInvariant();
    }
}
