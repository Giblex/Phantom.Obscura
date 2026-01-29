using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PhantomVault.Core.Services;

/// <summary>
///     Provides helper methods for computing a Merkle root of an audit
///     log.  A Merkle tree allows all entries in the log to be
///     summarised by a single root hash.  If any entry changes, the
///     root will be different.  In practice the root can be stored
///     alongside the log or anchored to an external timestamping
///     service for additional assurance.
/// </summary>
public static class MerkleAuditService
{
    /// <summary>
    ///     Reads all audit entries from the given file and computes a
    ///     Merkle root hash.  The audit file must contain one JSON
    ///     record per line matching the <see cref="AuditService.AuditEntry"/>
    ///     schema.  If the file does not exist or is empty, this
    ///     method returns null.
    /// </summary>
    public static string? ComputeMerkleRoot(string logFilePath)
    {
        if (!File.Exists(logFilePath)) return null;
        var hashes = new List<byte[]>();
        foreach (var line in File.ReadLines(logFilePath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<AuditService.AuditEntry>(line);
                if (entry != null)
                {
                    // Use the existing entry hash as the leaf.  Each
                    // AuditEntry.Hash is already a SHA‑256 hex string.
                    hashes.Add(Convert.FromHexString(entry.Hash));
                }
            }
            catch
            {
                // Skip malformed entries
            }
        }
        if (hashes.Count == 0) return null;
        // If only one hash, return it as the root
        if (hashes.Count == 1) return Convert.ToHexString(hashes[0]);
        // Build the Merkle tree bottom‑up
        while (hashes.Count > 1)
        {
            var nextLevel = new List<byte[]>();
            for (int i = 0; i < hashes.Count; i += 2)
            {
                byte[] left = hashes[i];
                byte[] right = (i + 1 < hashes.Count) ? hashes[i + 1] : left;
                byte[] concat = new byte[left.Length + right.Length];
                Buffer.BlockCopy(left, 0, concat, 0, left.Length);
                Buffer.BlockCopy(right, 0, concat, left.Length, right.Length);
                using var sha = SHA256.Create();
                nextLevel.Add(sha.ComputeHash(concat));
            }
            hashes = nextLevel;
        }
        return Convert.ToHexString(hashes[0]);
    }
}