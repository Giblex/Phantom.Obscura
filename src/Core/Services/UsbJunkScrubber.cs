using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using PhantomVault.Core.Models;
// VaultManifest dependency removed — scrub history is returned to the caller
// who decides how/where to persist it.

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Removes OS-injected indexing/system folders and files from the root of
    /// a Phantom Obscura USB drive. Whitelist-driven — never recurses into
    /// arbitrary user data; only touches a known list of paths created by
    /// Windows / macOS / Android indexers.
    ///
    /// The list of <see cref="ScrubEvent"/>s removed is returned to the caller
    /// for logging or persistence. By default, removed items are moved into
    /// <c>.phantom_quarantine/</c> on the drive and retained for a configurable
    /// window before hard-delete.
    /// </summary>
    public sealed class UsbJunkScrubber
    {
        private const string QuarantineFolderName = ".phantom_quarantine";
        private const int MaxScrubHistory = 200;

        /// <summary>
        /// Root-relative paths that are safe to remove. Each entry tagged with
        /// (path, isDirectory, reason).
        /// </summary>
        private static readonly IReadOnlyList<JunkTarget> Targets = new[]
        {
            // Android
            new JunkTarget("LOST.DIR",                    true,  "android-lost-dir"),
            new JunkTarget(".android_secure",             true,  "android-secure"),
            new JunkTarget(".thumbnails",                 true,  "android-thumbs"),
            // macOS
            new JunkTarget(".Spotlight-V100",             true,  "macos-spotlight"),
            new JunkTarget(".TemporaryItems",             true,  "macos-temp"),
            // Windows
            new JunkTarget("System Volume Information",   true,  "windows-svi"),
            new JunkTarget("$RECYCLE.BIN",                true,  "windows-recycle"),
            new JunkTarget("Thumbs.db",                   false, "windows-thumbs"),
            new JunkTarget("desktop.ini",                 false, "windows-desktop-ini"),
            new JunkTarget("IndexerVolumeGuid",           false, "windows-indexer-guid"),
            new JunkTarget("WPSettings.dat",              false, "windows-wp-settings"),
            // macOS Finder
            new JunkTarget(".DS_Store",                   false, "macos-ds-store"),
        };

        /// <summary>
        /// Runs the scrubber. Caller must have already verified that
        /// <paramref name="driveRoot"/> belongs to a Phantom Obscura-bound vault
        /// (device-id check passed). Returns the list of events removed; when
        /// <paramref name="manifest"/> is supplied, the same events are also
        /// appended to its <see cref="VaultManifest.ScrubHistory"/> (capped).
        /// </summary>
        /// <param name="driveRoot">Root path of the bound USB drive.</param>
        /// <param name="quarantineDays">Days to retain quarantined items. Set to 0 to hard-delete immediately.</param>
        /// <param name="dryRun">When true, scans but does not delete. Returned events have <c>Quarantined=false</c> and no real file system change is made.</param>
        /// <param name="manifest">Optional manifest whose ScrubHistory will be appended to. Caller is responsible for persisting the manifest afterwards.</param>
        public IReadOnlyList<ScrubEvent> Scrub(
            string driveRoot,
            int quarantineDays = 7,
            bool dryRun = false,
            VaultManifest? manifest = null)
        {
            if (string.IsNullOrEmpty(driveRoot)) throw new ArgumentException(nameof(driveRoot));

            var events = new List<ScrubEvent>();
            string? quarantineRoot = null;

            foreach (var target in Targets)
            {
                var fullPath = Path.Combine(driveRoot, target.RelativePath);
                bool exists = target.IsDirectory ? Directory.Exists(fullPath) : File.Exists(fullPath);
                if (!exists) continue;

                long bytes;
                string? sha = null;
                try
                {
                    if (target.IsDirectory)
                    {
                        bytes = SafeDirectorySize(fullPath);
                    }
                    else
                    {
                        bytes = new FileInfo(fullPath).Length;
                        sha = ComputeSha256(fullPath);
                    }
                }
                catch
                {
                    bytes = 0;
                }

                var evt = new ScrubEvent
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Path = target.RelativePath,
                    Bytes = bytes,
                    Sha256 = sha,
                    Reason = target.Reason,
                    Quarantined = false
                };

                if (!dryRun)
                {
                    try
                    {
                        if (quarantineDays > 0)
                        {
                            quarantineRoot ??= EnsureQuarantineRoot(driveRoot);
                            MoveToQuarantine(fullPath, target, quarantineRoot);
                            evt.Quarantined = true;
                        }
                        else
                        {
                            HardDelete(fullPath, target.IsDirectory);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[UsbJunkScrubber] failed to remove '{target.RelativePath}': {ex.Message}");
                        continue;
                    }
                }

                events.Add(evt);
            }

            // AppleDouble files: ._* at root only (non-recursive).
            try
            {
                foreach (var path in Directory.EnumerateFiles(driveRoot, "._*", SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(path);
                    long bytes = 0;
                    try { bytes = new FileInfo(path).Length; } catch { }
                    var evt = new ScrubEvent
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        Path = name,
                        Bytes = bytes,
                        Reason = "macos-appledouble",
                        Quarantined = false,
                    };
                    if (!dryRun)
                    {
                        try
                        {
                            if (quarantineDays > 0)
                            {
                                quarantineRoot ??= EnsureQuarantineRoot(driveRoot);
                                MoveToQuarantine(path, new JunkTarget(name, false, "macos-appledouble"), quarantineRoot);
                                evt.Quarantined = true;
                            }
                            else
                            {
                                File.Delete(path);
                            }
                        }
                        catch { continue; }
                    }
                    events.Add(evt);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UsbJunkScrubber] AppleDouble scan failed: {ex.Message}");
            }

            // Purge any quarantine entries older than retention window.
            if (!dryRun && quarantineDays > 0)
            {
                PurgeExpiredQuarantine(driveRoot, quarantineDays);
            }

            if (manifest != null && events.Count > 0)
            {
                manifest.ScrubHistory.AddRange(events);
                if (manifest.ScrubHistory.Count > MaxScrubHistory)
                {
                    int excess = manifest.ScrubHistory.Count - MaxScrubHistory;
                    manifest.ScrubHistory.RemoveRange(0, excess);
                }
            }

            return events;
        }

        /// <summary>
        /// Returns true if any of the whitelisted junk targets are present at
        /// <paramref name="driveRoot"/>. Cheap scan — does not enumerate sizes.
        /// </summary>
        public bool HasJunk(string driveRoot)
        {
            if (string.IsNullOrEmpty(driveRoot)) return false;
            foreach (var t in Targets)
            {
                var p = Path.Combine(driveRoot, t.RelativePath);
                if (t.IsDirectory ? Directory.Exists(p) : File.Exists(p)) return true;
            }
            try
            {
                if (Directory.EnumerateFiles(driveRoot, "._*", SearchOption.TopDirectoryOnly).Any())
                    return true;
            }
            catch { }
            return false;
        }

        private static string EnsureQuarantineRoot(string driveRoot)
        {
            var path = Path.Combine(driveRoot, QuarantineFolderName);
            Directory.CreateDirectory(path);
            if (OperatingSystem.IsWindows())
            {
                try { File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.System | FileAttributes.Directory); }
                catch { }
            }
            return path;
        }

        private static void MoveToQuarantine(string sourcePath, JunkTarget target, string quarantineRoot)
        {
            string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
            string safeName = target.RelativePath.Replace('/', '_').Replace('\\', '_');
            string dest = Path.Combine(quarantineRoot, $"{stamp}__{safeName}");

            if (target.IsDirectory)
            {
                Directory.Move(sourcePath, dest);
            }
            else
            {
                File.Move(sourcePath, dest);
            }
        }

        private static void HardDelete(string path, bool isDirectory)
        {
            if (isDirectory) Directory.Delete(path, recursive: true);
            else File.Delete(path);
        }

        private static long SafeDirectorySize(string path)
        {
            try
            {
                long sum = 0;
                foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { sum += new FileInfo(f).Length; } catch { }
                }
                return sum;
            }
            catch { return 0; }
        }

        private static string? ComputeSha256(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var sha = SHA256.Create();
                var hash = sha.ComputeHash(stream);
                return Convert.ToHexString(hash);
            }
            catch { return null; }
        }

        private static void PurgeExpiredQuarantine(string driveRoot, int retentionDays)
        {
            var qRoot = Path.Combine(driveRoot, QuarantineFolderName);
            if (!Directory.Exists(qRoot)) return;

            var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
            foreach (var entry in Directory.EnumerateFileSystemEntries(qRoot))
            {
                try
                {
                    var created = File.GetCreationTimeUtc(entry);
                    if (created < cutoff.UtcDateTime)
                    {
                        if (Directory.Exists(entry))
                            Directory.Delete(entry, recursive: true);
                        else
                            File.Delete(entry);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[UsbJunkScrubber] purge skipped '{entry}': {ex.Message}");
                }
            }
        }

        private sealed record JunkTarget(string RelativePath, bool IsDirectory, string Reason);
    }
}
