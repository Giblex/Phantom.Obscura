using System;
using System.Collections.Generic;
using System.IO;
#if !ANDROID
using System.Management;
using System.Runtime.Versioning;
#endif
using System.Text;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Locks a Phantom Obscura-bound USB partition against OS-side index/system
    /// folder injection (LOST.DIR, .Spotlight-V100, System Volume Information,
    /// etc.) by combining three layers:
    ///
    /// <list type="bullet">
    /// <item>GPT ReadOnly attribute (bit 60) — Windows / macOS / Android honour.</item>
    /// <item>(Optional) GPT Hidden attribute (bit 62) + drive-letter removal —
    /// blocks Windows Search and AutoPlay.</item>
    /// <item>OS-indexer suppression sentinel files at the drive root —
    /// <c>.metadata_never_index</c>, <c>.nomedia</c>, <c>.Trashes</c> file,
    /// <c>.fseventsd/no_log</c>.</item>
    /// </list>
    ///
    /// Toggling between "locked for write" and "PO is editing" is done by
    /// flipping the GPT ReadOnly bit via <c>MSFT_Partition.SetAttributes</c>.
    /// Windows-only impl; <see cref="IsSupported"/> returns false on other
    /// platforms and all mutation methods are no-ops there.
    /// </summary>
    public sealed class UsbWriteProtectionService
    {
        private static readonly IReadOnlyList<(string Name, byte[] Contents)> SentinelFiles = new[]
        {
            (".metadata_never_index", Array.Empty<byte>()),
            (".metadata_never_index_unless_rootfs", Array.Empty<byte>()),
            (".nomedia", Array.Empty<byte>()),
            (".Trashes", Encoding.ASCII.GetBytes("PhantomObscura sentinel — not a folder")),
        };

        private const string FseventsdDir = ".fseventsd";
        private const string FseventsdSentinel = "no_log";

        /// <summary>Custom GPT partition type GUID applied by Layer B re-tag.</summary>
        public const string PhantomObscuraPartitionTypeGuid = "{7C1B6BEF-2E0F-4F2A-9C8E-50414F50414FE5}";

        /// <summary>Standard Microsoft Basic Data partition type GUID.</summary>
        public const string BasicDataPartitionTypeGuid = "{EBD0A0A2-B9E5-4433-87C0-68B6B72699C7}";

        /// <summary>True when this platform supports the mutation methods.</summary>
        public bool IsSupported => OperatingSystem.IsWindows();

        /// <summary>
        /// Applies the full write-protection posture described by <paramref name="state"/>
        /// to the partition that hosts <paramref name="driveRoot"/>. Safe to call
        /// repeatedly. On non-Windows platforms or for partitions that cannot be
        /// resolved via WMI, returns false without throwing so callers can degrade
        /// gracefully.
        /// </summary>
        public bool ApplyProtection(string driveRoot, UsbWriteProtectionState state)
        {
            if (string.IsNullOrEmpty(driveRoot)) throw new ArgumentException(nameof(driveRoot));
            if (state == null) throw new ArgumentNullException(nameof(state));

            // Sentinel files always apply (cross-platform).
            // Drive must be writable to write them. If RO is already on we cannot
            // self-heal sentinels — record that state.Hidden/ReadOnly may need a
            // brief RW window first; for now we attempt and tolerate failures.
            TryWriteSentinels(driveRoot, state);

            if (!OperatingSystem.IsWindows()) return false;

            try
            {
                if (!state.CompatibilityMode && !string.IsNullOrEmpty(state.PartitionTypeGuid))
                {
                    SetPartitionTypeGuid(driveRoot, state.PartitionTypeGuid);
                }

                SetPartitionAttributes(driveRoot, isReadOnly: state.ReadOnly, isHidden: state.Hidden);
                state.LastAsserted = DateTimeOffset.UtcNow;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UsbWriteProtection] ApplyProtection failed: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Temporarily clears the GPT ReadOnly bit so Phantom Obscura can write to
        /// the volume. Hidden bit and partition type GUID are left untouched.
        /// Returns true on success.
        /// </summary>
        public bool EnableWriteAccess(string driveRoot)
        {
            if (string.IsNullOrEmpty(driveRoot)) throw new ArgumentException(nameof(driveRoot));
            if (!OperatingSystem.IsWindows()) return false;

            // 1. Try WMI / MSFT_Partition (requires elevation; best path)
            try
            {
                SetPartitionAttributes(driveRoot, isReadOnly: false, isHidden: null);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UsbWriteProtection] EnableWriteAccess WMI failed: {ex}");
            }

            // 2. Clear Windows registry write-protect policy (no elevation needed)
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\StorageDevicePolicies", writable: true);
                if (key != null)
                    key.SetValue("WriteProtect", 0, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UsbWriteProtection] EnableWriteAccess registry failed: {ex}");
            }

            // 3. Diskpart fallback — elevated subprocess
            try
            {
                var driveLetter = driveRoot.TrimEnd('\\').TrimEnd(':').ToUpperInvariant();
                if (driveLetter.Length == 1)
                {
                    var script = Path.Combine(Path.GetTempPath(), $"phantom_dp_{Guid.NewGuid():N}.txt");
                    File.WriteAllText(script,
                        $"select volume {driveLetter}\r\nattributes disk clear readonly\r\nattributes volume clear readonly\r\nexit\r\n");
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo("diskpart.exe", $"/s \"{script}\"")
                        {
                            UseShellExecute = true,
                            Verb = "runas",
                            CreateNoWindow = true,
                            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                        };
                        var proc = System.Diagnostics.Process.Start(psi);
                        proc?.WaitForExit(10_000);
                        return true;
                    }
                    finally
                    {
                        try { File.Delete(script); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UsbWriteProtection] EnableWriteAccess diskpart failed: {ex}");
            }

            return false;
        }

        /// <summary>
        /// Re-applies the GPT ReadOnly bit. Equivalent to a partial
        /// <see cref="ApplyProtection"/> that only flips the RO flag.
        /// </summary>
        public bool DisableWriteAccess(string driveRoot)
        {
            if (string.IsNullOrEmpty(driveRoot)) throw new ArgumentException(nameof(driveRoot));
            if (!OperatingSystem.IsWindows()) return false;

            try
            {
                SetPartitionAttributes(driveRoot, isReadOnly: true, isHidden: null);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UsbWriteProtection] DisableWriteAccess failed: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Writes the OS-indexer-suppression sentinel files at the drive root if
        /// they are missing. Marks them hidden+system on Windows. Updates
        /// <paramref name="state"/>'s <c>ExpectedSentinelFiles</c> list to match
        /// what's now on disk.
        /// </summary>
        public void EnsureSentinelFiles(string driveRoot, UsbWriteProtectionState state)
        {
            if (string.IsNullOrEmpty(driveRoot)) throw new ArgumentException(nameof(driveRoot));
            if (state == null) throw new ArgumentNullException(nameof(state));
            TryWriteSentinels(driveRoot, state);
        }

        /// <summary>
        /// Returns true if the drive root contains all of the expected sentinel
        /// files referenced by <paramref name="state"/>. Useful for tamper hints
        /// when scrubbing.
        /// </summary>
        public bool VerifySentinelFiles(string driveRoot, UsbWriteProtectionState state)
        {
            if (string.IsNullOrEmpty(driveRoot)) return false;
            if (state == null || state.ExpectedSentinelFiles.Count == 0) return true;

            foreach (var name in state.ExpectedSentinelFiles)
            {
                if (!File.Exists(Path.Combine(driveRoot, name)) &&
                    !Directory.Exists(Path.Combine(driveRoot, name)))
                {
                    return false;
                }
            }
            return true;
        }

        private static void TryWriteSentinels(string driveRoot, UsbWriteProtectionState state)
        {
            state.ExpectedSentinelFiles.Clear();
            foreach (var (name, contents) in SentinelFiles)
            {
                var path = Path.Combine(driveRoot, name);
                try
                {
                    if (!File.Exists(path))
                    {
                        File.WriteAllBytes(path, contents);
                    }

                    if (OperatingSystem.IsWindows())
                    {
                        try { File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.System); }
                        catch { /* read-only volume — acceptable */ }
                    }

                    state.ExpectedSentinelFiles.Add(name);
                }
                catch (Exception ex)
                {
                    // Likely the drive is currently RO. Caller will surface via
                    // VerifySentinelFiles later; don't fail the whole apply.
                    System.Diagnostics.Debug.WriteLine($"[UsbWriteProtection] sentinel '{name}' write failed: {ex.Message}");
                }
            }

            // Special case: .fseventsd is a folder containing a 'no_log' marker.
            var fseventsdPath = Path.Combine(driveRoot, FseventsdDir);
            try
            {
                Directory.CreateDirectory(fseventsdPath);
                var noLogPath = Path.Combine(fseventsdPath, FseventsdSentinel);
                if (!File.Exists(noLogPath))
                {
                    File.WriteAllBytes(noLogPath, Array.Empty<byte>());
                }
                if (OperatingSystem.IsWindows())
                {
                    try { File.SetAttributes(fseventsdPath, FileAttributes.Hidden | FileAttributes.System | FileAttributes.Directory); }
                    catch { /* ignore */ }
                }
                state.ExpectedSentinelFiles.Add(FseventsdDir + "/" + FseventsdSentinel);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UsbWriteProtection] fseventsd sentinel failed: {ex.Message}");
            }
        }

#if !ANDROID
        #region WMI / MSFT_Partition manipulation (Windows-only)

        [SupportedOSPlatform("windows")]
        private static void SetPartitionAttributes(string driveRoot, bool? isReadOnly, bool? isHidden)
        {
            using var partition = ResolveMsftPartition(driveRoot);
            if (partition == null)
                throw new InvalidOperationException($"Could not resolve MSFT_Partition for '{driveRoot}'.");

            var inParams = partition.GetMethodParameters("SetAttributes");
            if (isReadOnly.HasValue) inParams["IsReadOnly"] = isReadOnly.Value;
            if (isHidden.HasValue) inParams["IsHidden"] = isHidden.Value;

            var outParams = partition.InvokeMethod("SetAttributes", inParams, null);
            var status = outParams?["ReturnValue"];
            if (status is uint rv && rv != 0)
            {
                throw new InvalidOperationException($"MSFT_Partition.SetAttributes returned {rv}.");
            }
        }

        [SupportedOSPlatform("windows")]
        private static void SetPartitionTypeGuid(string driveRoot, string gptTypeGuid)
        {
            using var partition = ResolveMsftPartition(driveRoot);
            if (partition == null)
                throw new InvalidOperationException($"Could not resolve MSFT_Partition for '{driveRoot}'.");

            // MSFT_Partition exposes SetAttributes(GptType) on newer Windows;
            // fall back to a direct property set for older builds.
            try
            {
                var inParams = partition.GetMethodParameters("SetAttributes");
                inParams["GptType"] = gptTypeGuid;
                var outParams = partition.InvokeMethod("SetAttributes", inParams, null);
                var status = outParams?["ReturnValue"];
                if (status is uint rv && rv != 0)
                {
                    throw new InvalidOperationException($"MSFT_Partition.SetAttributes(GptType) returned {rv}.");
                }
            }
            catch (ManagementException)
            {
                partition["GptType"] = gptTypeGuid;
                partition.Put();
            }
        }

        [SupportedOSPlatform("windows")]
        private static ManagementObject? ResolveMsftPartition(string driveRoot)
        {
            var letter = driveRoot.TrimEnd('\\').TrimEnd(':');
            if (letter.Length == 0) return null;

            // LogicalDisk → Partition (Win32) → DiskIndex
            int? diskIndex = null;
            uint? partitionIndex = null;
            using (var assoc = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{letter}:'}} WHERE AssocClass=Win32_LogicalDiskToPartition"))
            {
                foreach (ManagementObject part in assoc.Get())
                {
                    diskIndex = Convert.ToInt32(part["DiskIndex"]);
                    partitionIndex = Convert.ToUInt32(part["Index"]);
                    part.Dispose();
                    break;
                }
            }

            if (diskIndex == null || partitionIndex == null) return null;

            // Win32 Index is 0-based; MSFT_Partition PartitionNumber is 1-based.
            uint msftPartitionNumber = partitionIndex.Value + 1;

            using var storageQuery = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\Storage",
                $"SELECT * FROM MSFT_Partition WHERE DiskNumber = {diskIndex} AND PartitionNumber = {msftPartitionNumber}");

            foreach (ManagementObject p in storageQuery.Get())
            {
                return p;
            }
            return null;
        }

        #endregion
#else
        // Android fallbacks: WMI is unavailable; these are unreachable because
        // every public caller short-circuits via OperatingSystem.IsWindows().
        private static void SetPartitionAttributes(string driveRoot, bool? isReadOnly, bool? isHidden)
            => throw new PlatformNotSupportedException();

        private static void SetPartitionTypeGuid(string driveRoot, string gptTypeGuid)
            => throw new PlatformNotSupportedException();
#endif
    }
}
