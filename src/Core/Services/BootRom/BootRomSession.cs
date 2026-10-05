using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace PhantomVault.Core.Services.BootRom
{
    /// <summary>
    /// Holds the Boot ROM's key contribution for the vaults unlocked in this process.
    ///
    /// The contribution has to reach every manifest derivation, read and write alike. Threading
    /// it through the forty-odd call sites would mean that one missed write path silently
    /// re-encrypts a vault under a different key — an unrecoverable mistake. Instead the value is
    /// established once at unlock and read at the single derivation chokepoint inside
    /// <c>ManifestService</c>, so reads and writes cannot disagree.
    ///
    /// Entries are per-manifest and live only as long as the vault is open: <see cref="Clear"/>
    /// on lock, <see cref="ClearAll"/> on exit. The stored bytes are zeroed when removed.
    /// </summary>
    public static class BootRomSession
    {
        private static readonly object Gate = new();
        private static readonly Dictionary<string, byte[]> Contributions = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Normalises a manifest path so the same vault always maps to one entry.</summary>
        private static string Normalise(string manifestPath)
        {
            if (string.IsNullOrWhiteSpace(manifestPath))
                throw new ArgumentException("Manifest path must be provided.", nameof(manifestPath));
            try
            {
                return Path.GetFullPath(manifestPath);
            }
            catch
            {
                // An unusual path (container object id, raw selection) is still a usable key as-is.
                return manifestPath;
            }
        }

        /// <summary>Records the contribution a Boot ROM produced for this vault.</summary>
        public static void Set(string manifestPath, ReadOnlySpan<byte> contribution)
        {
            if (contribution.Length != 32)
                throw new ArgumentException("Boot ROM contribution must be 32 bytes.", nameof(contribution));

            string key = Normalise(manifestPath);
            byte[] copy = contribution.ToArray();
            lock (Gate)
            {
                if (Contributions.TryGetValue(key, out var existing))
                    CryptographicOperations.ZeroMemory(existing);
                Contributions[key] = copy;
            }
        }

        /// <summary>
        /// The contribution for this vault, or null when it is not Boot ROM bound. The array is
        /// the live stored instance — read it, never mutate or zero it.
        /// </summary>
        public static byte[]? Peek(string manifestPath)
        {
            if (string.IsNullOrWhiteSpace(manifestPath))
                return null;

            string key = Normalise(manifestPath);
            lock (Gate)
            {
                return Contributions.TryGetValue(key, out var value) ? value : null;
            }
        }

        public static bool IsBound(string manifestPath) => Peek(manifestPath) is not null;

        /// <summary>Forgets (and zeroes) the contribution for one vault, on lock.</summary>
        public static void Clear(string manifestPath)
        {
            if (string.IsNullOrWhiteSpace(manifestPath))
                return;

            string key = Normalise(manifestPath);
            lock (Gate)
            {
                if (!Contributions.Remove(key, out var value))
                    return;
                CryptographicOperations.ZeroMemory(value);
            }
        }

        /// <summary>Forgets every contribution, on exit or panic.</summary>
        public static void ClearAll()
        {
            lock (Gate)
            {
                foreach (var value in Contributions.Values)
                    CryptographicOperations.ZeroMemory(value);
                Contributions.Clear();
            }
        }
    }
}
