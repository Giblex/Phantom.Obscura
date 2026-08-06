using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PhantomVault.PrivilegedBroker
{
    /// <summary>
    /// Elevated VHDX operations for the whole Phantom Suite (PhantomKey, Obscura,
    /// Attestor). Runs inside the privileged broker service so callers never see
    /// per-launch UAC. Provisioning uses DiscUtils out-of-process (via the caller);
    /// this service only handles the OS-level Attach/Detach that requires admin.
    ///
    /// This is intentionally standalone — no reference to PhantomKey.Core, no
    /// DiscUtils dependency inside the broker. Provisioning arrives already-created
    /// on disk (the setup wizards use DiscUtils in-process, which does not need
    /// elevation), and this service just attaches it.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class PhantomVolumeService
    {
        // Maps container path → attached disk handle so Unmount can Detach without
        // re-opening. The mount ROOT (drive letter) is discovered by scanning
        // DriveInfo for the PHANTOMKEY volume label after attach.
        private readonly System.Collections.Generic.Dictionary<string, IntPtr> _attached = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new();

        public bool Provision(string containerPath, long sizeBytes)
        {
            // Provisioning uses DiscUtils in-process from the setup wizards, so this
            // path is only reached as a "make sure it exists" check — no create.
            // If the wizard didn't provision, we don't attempt it here; that keeps
            // NuGet dependencies out of the broker.
            if (File.Exists(containerPath)) return true;
            throw new InvalidOperationException(
                "Container has not been provisioned yet. Run Obscura or Attestor setup first, or call the provisioner from the caller (unelevated) before invoking the broker.");
        }

        public string Mount(string containerPath)
        {
            if (!File.Exists(containerPath))
                throw new FileNotFoundException("VHDX container not found.", containerPath);

            lock (_gate)
            {
                if (_attached.ContainsKey(containerPath))
                {
                    // Already attached — find its root.
                    var root = FindMountRoot();
                    return root ?? "";
                }

                var handle = NativeOpen(containerPath);
                try
                {
                    NativeAttach(handle);
                }
                catch
                {
                    try { CloseHandle(handle); } catch { }
                    throw;
                }

                var mountedRoot = WaitForMountRoot(TimeSpan.FromSeconds(10));
                if (mountedRoot is null)
                {
                    try { DetachVirtualDisk(handle, DETACH_VIRTUAL_DISK_FLAG.NONE, 0); } catch { }
                    try { CloseHandle(handle); } catch { }
                    throw new TimeoutException("VHDX attached but no drive letter appeared within 10s.");
                }

                _attached[containerPath] = handle;
                return mountedRoot;
            }
        }

        public bool Unmount(string containerPath)
        {
            lock (_gate)
            {
                if (!_attached.TryGetValue(containerPath, out var handle))
                    return true; // already detached
                try { DetachVirtualDisk(handle, DETACH_VIRTUAL_DISK_FLAG.NONE, 0); }
                catch { /* best-effort */ }
                try { CloseHandle(handle); } catch { }
                _attached.Remove(containerPath);
                return true;
            }
        }

        /// <summary>Detach everything on shutdown so the SCM doesn't leak orphaned mounts.</summary>
        public void DetachAll()
        {
            lock (_gate)
            {
                foreach (var (_, handle) in _attached)
                {
                    try { DetachVirtualDisk(handle, DETACH_VIRTUAL_DISK_FLAG.NONE, 0); } catch { }
                    try { CloseHandle(handle); } catch { }
                }
                _attached.Clear();
            }
        }

        private static string? WaitForMountRoot(TimeSpan timeout)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                var root = FindMountRoot();
                if (root is not null) return root;
                System.Threading.Thread.Sleep(150);
            }
            return null;
        }

        private static string? FindMountRoot()
        {
            foreach (var di in DriveInfo.GetDrives())
            {
                try
                {
                    if (di.IsReady && string.Equals(di.VolumeLabel, "PHANTOMKEY", StringComparison.OrdinalIgnoreCase))
                        return di.RootDirectory.FullName;
                }
                catch { /* not ready */ }
            }
            return null;
        }

        // ── Native VirtDisk P/Invoke ─────────────────────────────────────────

        private static IntPtr NativeOpen(string path)
        {
            var storageType = new VIRTUAL_STORAGE_TYPE
            {
                DeviceId = VIRTUAL_STORAGE_TYPE_DEVICE_VHDX,
                VendorId = VIRTUAL_STORAGE_TYPE_VENDOR_MICROSOFT
            };
            var parameters = new OPEN_VIRTUAL_DISK_PARAMETERS
            {
                Version = OPEN_VIRTUAL_DISK_VERSION.VERSION_2,
                Version2 = new OPEN_VIRTUAL_DISK_PARAMETERS_VERSION2
                {
                    GetInfoOnly = false,
                    ReadOnly = false,
                    ResiliencyGuid = Guid.Empty
                }
            };

            var rc = OpenVirtualDisk(ref storageType, path, VIRTUAL_DISK_ACCESS_MASK.NONE,
                OPEN_VIRTUAL_DISK_FLAG.NONE, ref parameters, out var handle);
            if (rc != 0) throw new Win32Exception(rc, $"OpenVirtualDisk failed (0x{rc:X8}).");
            return handle;
        }

        private static void NativeAttach(IntPtr handle)
        {
            var parameters = new ATTACH_VIRTUAL_DISK_PARAMETERS { Version = ATTACH_VIRTUAL_DISK_VERSION.VERSION_1 };
            var rc = AttachVirtualDisk(handle, IntPtr.Zero, ATTACH_VIRTUAL_DISK_FLAG.PERMANENT_LIFETIME,
                0, ref parameters, IntPtr.Zero);
            if (rc != 0) throw new Win32Exception(rc, $"AttachVirtualDisk failed (0x{rc:X8}).");
        }

        private static readonly Guid VIRTUAL_STORAGE_TYPE_VENDOR_MICROSOFT =
            new("EC984AEC-A0F9-47e9-901F-71415A66345B");
        private const uint VIRTUAL_STORAGE_TYPE_DEVICE_VHDX = 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct VIRTUAL_STORAGE_TYPE { public uint DeviceId; public Guid VendorId; }

        private enum OPEN_VIRTUAL_DISK_VERSION : uint { UNSPECIFIED = 0, VERSION_1 = 1, VERSION_2 = 2 }

        [Flags] private enum OPEN_VIRTUAL_DISK_FLAG : uint { NONE = 0 }
        [Flags] private enum VIRTUAL_DISK_ACCESS_MASK : uint { NONE = 0 }

        [StructLayout(LayoutKind.Sequential)]
        private struct OPEN_VIRTUAL_DISK_PARAMETERS_VERSION2
        {
            [MarshalAs(UnmanagedType.Bool)] public bool GetInfoOnly;
            [MarshalAs(UnmanagedType.Bool)] public bool ReadOnly;
            public Guid ResiliencyGuid;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct OPEN_VIRTUAL_DISK_PARAMETERS
        {
            [FieldOffset(0)] public OPEN_VIRTUAL_DISK_VERSION Version;
            [FieldOffset(4)] public OPEN_VIRTUAL_DISK_PARAMETERS_VERSION2 Version2;
        }

        private enum ATTACH_VIRTUAL_DISK_VERSION : uint { UNSPECIFIED = 0, VERSION_1 = 1 }

        [Flags]
        private enum ATTACH_VIRTUAL_DISK_FLAG : uint
        {
            NONE = 0, READ_ONLY = 1, NO_DRIVE_LETTER = 2,
            PERMANENT_LIFETIME = 4, NO_LOCAL_HOST = 8
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ATTACH_VIRTUAL_DISK_PARAMETERS
        {
            public ATTACH_VIRTUAL_DISK_VERSION Version;
            public uint Reserved;
        }

        [Flags] private enum DETACH_VIRTUAL_DISK_FLAG : uint { NONE = 0 }

        [DllImport("VirtDisk.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern int OpenVirtualDisk(ref VIRTUAL_STORAGE_TYPE virtualStorageType,
            string path, VIRTUAL_DISK_ACCESS_MASK virtualDiskAccessMask,
            OPEN_VIRTUAL_DISK_FLAG flags, ref OPEN_VIRTUAL_DISK_PARAMETERS parameters, out IntPtr handle);

        [DllImport("VirtDisk.dll", SetLastError = false)]
        private static extern int AttachVirtualDisk(IntPtr virtualDiskHandle,
            IntPtr securityDescriptor, ATTACH_VIRTUAL_DISK_FLAG flags,
            uint providerSpecificFlags, ref ATTACH_VIRTUAL_DISK_PARAMETERS parameters, IntPtr overlapped);

        [DllImport("VirtDisk.dll", SetLastError = false)]
        private static extern int DetachVirtualDisk(IntPtr virtualDiskHandle,
            DETACH_VIRTUAL_DISK_FLAG flags, uint providerSpecificFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
