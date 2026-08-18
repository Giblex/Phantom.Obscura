using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.Services.Mount
{
    /// <summary>
    /// Mounts an OBSCUR01 encrypted container as a Windows drive letter via WinFsp.
    /// Phase 1 is read-only: the drive is a live projection of the container (no temp
    /// extraction) that appears on mount and vanishes on unmount/lock. When WinFsp is
    /// not installed the service reports <see cref="IsWinFspAvailable"/> = false and
    /// the mount calls throw a descriptive error instead of crashing.
    /// </summary>
    public sealed class PhantomMountService : IDisposable
    {
        private readonly ObscuraVolumeService _volumeService = new();
        private readonly object _gate = new();
#if WINFSP
        private Fsp.FileSystemHost? _host;
#endif

        public bool IsMounted { get; private set; }
        public string? MountPoint { get; private set; }
        public bool IsReadOnly { get; private set; }

        /// <summary>True when the WinFsp runtime is present on this machine.</summary>
        public static bool IsWinFspAvailable => ProbeWinFsp();

        /// <summary>
        /// Mounts <paramref name="volumePath"/> read-only at <paramref name="driveLetter"/>
        /// (e.g. "P:") or the next free letter. Returns the actual mount point.
        /// </summary>
        public async Task<string?> MountReadOnlyAsync(string volumePath, string keyfilePath,
            string? driveLetter = null,
            CancellationToken cancellationToken = default)
        {
#if WINFSP
            if (IsMounted)
                return MountPoint;
            if (!IsWinFspAvailable)
                throw new InvalidOperationException("WinFsp is not installed on this machine.");
            if (string.IsNullOrWhiteSpace(volumePath) || !File.Exists(volumePath))
                throw new FileNotFoundException("Encrypted volume not found.", volumePath);

            // Manifest and payload offset from one authenticated read. The offset used to
            // come from a private helper here that assumed the v1 layout and never checked
            // the signature — under v2 it would have mounted at a garbage offset.
            var header = await _volumeService.ReadHeaderInfoAsync(volumePath, keyfilePath, cancellationToken).ConfigureAwait(false);
            var manifest = header.Manifest;
            long payloadStart = header.PayloadStart;

            lock (_gate)
            {
                if (IsMounted)
                    return MountPoint;

                var fs = new PhantomReadOnlyFileSystem(volumePath, manifest, payloadStart);
                return MountHost(fs, driveLetter, readOnly: true);
            }
#else
            await Task.CompletedTask;
            throw new PlatformNotSupportedException(
                "This build was compiled without WinFsp support; install WinFsp and rebuild.");
#endif
        }

        /// <summary>
        /// Mounts <paramref name="volumePath"/> writable at <paramref name="driveLetter"/>.
        /// Edits are buffered in a copy-on-write in-memory overlay and repacked into the
        /// container atomically on unmount; <paramref name="onCommitted"/> fires after a
        /// successful repack so the caller can bump the rollback save-sequence and anchor.
        /// </summary>
        public async Task<string?> MountWritableAsync(string volumePath, string keyfilePath,
            string? driveLetter = null,
            Action? onCommitted = null, CancellationToken cancellationToken = default)
        {
#if WINFSP
            if (IsMounted)
                return MountPoint;
            if (!IsWinFspAvailable)
                throw new InvalidOperationException("WinFsp is not installed on this machine.");
            if (string.IsNullOrWhiteSpace(volumePath) || !File.Exists(volumePath))
                throw new FileNotFoundException("Encrypted volume not found.", volumePath);

            var header = await _volumeService.ReadHeaderInfoAsync(volumePath, keyfilePath, cancellationToken).ConfigureAwait(false);
            var manifest = header.Manifest;
            long payloadStart = header.PayloadStart;

            lock (_gate)
            {
                if (IsMounted)
                    return MountPoint;

                var fs = new PhantomWritableFileSystem(volumePath, manifest, payloadStart, keyfilePath, onCommitted);
                return MountHost(fs, driveLetter, readOnly: false);
            }
#else
            await Task.CompletedTask;
            throw new PlatformNotSupportedException(
                "This build was compiled without WinFsp support; install WinFsp and rebuild.");
#endif
        }

#if WINFSP
        private string? MountHost(Fsp.FileSystemBase fs, string? driveLetter, bool readOnly)
        {
            var host = new Fsp.FileSystemHost(fs)
            {
                SectorSize = 4096,
                SectorsPerAllocationUnit = 1,
                MaxComponentLength = 255,
                FileInfoTimeout = 1000,
                CaseSensitiveSearch = false,
                CasePreservedNames = true,
                UnicodeOnDisk = true,
                PersistentAcls = false,
                PostCleanupWhenModifiedOnly = true,
                FileSystemName = "PhantomFS",
            };

            string mountPoint = driveLetter ?? NextFreeDriveLetter();
            int status = host.Mount(mountPoint, null, false, 0);
            if (status < 0)
            {
                try { host.Unmount(); } catch { /* best effort */ }
                throw new IOException($"WinFsp mount failed (status 0x{status:X8}).");
            }

            _host = host;
            MountPoint = host.MountPoint();
            IsMounted = true;
            IsReadOnly = readOnly;
            return MountPoint;
        }
#endif

        public void Unmount()
        {
            lock (_gate)
            {
#if WINFSP
                try { _host?.Unmount(); }
                catch { /* best effort */ }
                _host = null;
#endif
                IsMounted = false;
                MountPoint = null;
                IsReadOnly = false;
            }
        }

        public void Dispose() => Unmount();


        private static string NextFreeDriveLetter()
        {
            for (char c = 'Z'; c >= 'G'; c--)
            {
                if (!Directory.Exists(c + ":\\"))
                    return c + ":";
            }
            throw new IOException("No free drive letter is available for mounting.");
        }

        private static bool ProbeWinFsp()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\WinFsp")
                                ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WinFsp");
                if (key?.GetValue("InstallDir") is not string dir || string.IsNullOrEmpty(dir))
                    return false;
                return File.Exists(Path.Combine(dir, "bin", "winfsp-msil.dll"));
            }
            catch
            {
                return false;
            }
        }
    }
}
