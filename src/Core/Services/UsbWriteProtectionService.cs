using System;
using System.Collections.Generic;
using System.IO;
#if !ANDROID
using System.Management;
using System.Runtime.Versioning;
#endif
using System.Text;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services.Privileged;

namespace PhantomVault.Core.Services
{

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

        public const string PhantomObscuraPartitionTypeGuid = "{7C1B6BEF-2E0F-4F2A-9C8E-50414F50414FE5}";

        public const string BasicDataPartitionTypeGuid = "{EBD0A0A2-B9E5-4433-87C0-68B6B72699C7}";

        public bool IsSupported => OperatingSystem.IsWindows();

        private static bool IsCurrentProcessElevated()
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
#pragma warning disable CA1416
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
#pragma warning restore CA1416
            }
            catch
            {
                return false;
            }
        }

        public bool ApplyProtection(string driveRoot, UsbWriteProtectionState state)
        {
            if (string.IsNullOrEmpty(driveRoot)) throw new ArgumentException(nameof(driveRoot));
            if (state == null) throw new ArgumentNullException(nameof(state));

            if (PrivilegedExecution.ShouldBroker)
                return PrivilegedExecution.Broker!.ApplyProtection(driveRoot, state);
            if (OperatingSystem.IsWindows() && PrivilegedExecution.RequiresBrokerButMissing)
                throw new PrivilegedBrokerUnavailableException();

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

        public bool EnableWriteAccess(string driveRoot)
        {
            if (string.IsNullOrEmpty(driveRoot)) throw new ArgumentException(nameof(driveRoot));
            if (!OperatingSystem.IsWindows()) return false;

            if (PrivilegedExecution.ShouldBroker)
                return PrivilegedExecution.Broker!.EnableWriteAccess(driveRoot);
            if (PrivilegedExecution.RequiresBrokerButMissing)
                throw new PrivilegedBrokerUnavailableException();

            try
            {
                SetPartitionAttributes(driveRoot, isReadOnly: false, isHidden: null);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UsbWriteProtection] EnableWriteAccess WMI failed: {ex}");
            }

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
                        // When the current process is already elevated (our app.manifest declares
                    // requireAdministrator so this should be the normal case), spawn diskpart
                    // directly without the "runas" verb — otherwise Windows pops a UAC prompt
                    // every time even though we already have admin rights.
                    var elevated = IsCurrentProcessElevated();
                    var psi = new System.Diagnostics.ProcessStartInfo("diskpart.exe", $"/s \"{script}\"")
                    {
                        UseShellExecute = !elevated,
                        Verb = elevated ? string.Empty : "runas",
                        CreateNoWindow = true,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                        RedirectStandardOutput = elevated,
                        RedirectStandardError = elevated
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

        public bool DisableWriteAccess(string driveRoot)
        {
            if (string.IsNullOrEmpty(driveRoot)) throw new ArgumentException(nameof(driveRoot));
            if (!OperatingSystem.IsWindows()) return false;

            if (PrivilegedExecution.ShouldBroker)
                return PrivilegedExecution.Broker!.DisableWriteAccess(driveRoot);
            if (PrivilegedExecution.RequiresBrokerButMissing)
                throw new PrivilegedBrokerUnavailableException();

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

        public void EnsureSentinelFiles(string driveRoot, UsbWriteProtectionState state)
        {
            if (string.IsNullOrEmpty(driveRoot)) throw new ArgumentException(nameof(driveRoot));
            if (state == null) throw new ArgumentNullException(nameof(state));
            TryWriteSentinels(driveRoot, state);
        }

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
                        catch {  }
                    }

                    state.ExpectedSentinelFiles.Add(name);
                }
                catch (Exception ex)
                {

                    System.Diagnostics.Debug.WriteLine($"[UsbWriteProtection] sentinel '{name}' write failed: {ex.Message}");
                }
            }

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
                    catch {  }
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

        private static void SetPartitionAttributes(string driveRoot, bool? isReadOnly, bool? isHidden)
            => throw new PlatformNotSupportedException();

        private static void SetPartitionTypeGuid(string driveRoot, string gptTypeGuid)
            => throw new PlatformNotSupportedException();
#endif
    }
}

