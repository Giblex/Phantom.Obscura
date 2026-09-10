using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PhantomVault.Core.Services;

public static class MerkleAuditService
{

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

                    hashes.Add(Convert.FromHexString(entry.Hash));
                }
            }
            catch (Exception ex)
            {
                // Skipping an entry changes the root, and a corrupted line in an audit log is
                // exactly what someone covering their tracks would leave, so it is recorded.
                Serilog.Log.Warning(ex, "[MerkleAudit] Skipped an unreadable audit log entry while computing the Merkle root");
            }
        }
        if (hashes.Count == 0) return null;

        if (hashes.Count == 1) return Convert.ToHexString(hashes[0]);

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

