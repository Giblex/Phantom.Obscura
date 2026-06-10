using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PhantomVault.Core.Services.Security;

public static class VirtualMachineDetection
{

    public static bool IsRunningInVirtualMachine()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {

            return CheckLinuxVMIndicators();
        }

        if (CheckHypervisorPresent())
            return true;

        if (CheckVMWareRegistry())
            return true;

        if (CheckVirtualBoxRegistry())
            return true;

        if (CheckHyperVIndicators())
            return true;

        if (CheckBIOSInfo())
            return true;

        if (CheckMACAddress())
            return true;

        if (CheckVMProcesses())
            return true;

        return false;
    }

    [SupportedOSPlatform("windows")]
    private static bool CheckHypervisorPresent()
    {
        try
        {

            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
            using var collection = searcher.Get();

            foreach (ManagementObject obj in collection)
            {
                var hypervisorPresent = obj["HypervisorPresent"];
                if (hypervisorPresent != null && (bool)hypervisorPresent)
                    return true;

                var model = obj["Model"]?.ToString()?.ToLowerInvariant() ?? string.Empty;
                if (model.Contains("virtual") || model.Contains("vmware") ||
                    model.Contains("virtualbox") || model.Contains("hyper-v"))
                    return true;
            }
        }
        catch
        {

        }

        return false;
    }

    [SupportedOSPlatform("windows")]
    private static bool CheckVMWareRegistry()
    {
        try
        {
            string[] vmwareKeys = new[]
            {
                @"SOFTWARE\VMware, Inc.\VMware Tools",
                @"HARDWARE\DEVICEMAP\Scsi\Scsi Port 0\Scsi Bus 0\Target Id 0\Logical Unit Id 0"
            };

            foreach (var keyPath in vmwareKeys)
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath);
                if (key != null)
                {

                    var identifier = key.GetValue("Identifier")?.ToString()?.ToLowerInvariant() ?? string.Empty;
                    if (identifier.Contains("vmware"))
                        return true;

                    return true;
                }
            }
        }
        catch
        {

        }

        return false;
    }

    [SupportedOSPlatform("windows")]
    private static bool CheckVirtualBoxRegistry()
    {
        try
        {
            string[] vboxKeys = new[]
            {
                @"SOFTWARE\Oracle\VirtualBox Guest Additions",
                @"HARDWARE\ACPI\DSDT\VBOX__"
            };

            foreach (var keyPath in vboxKeys)
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath);
                if (key != null)
                    return true;
            }
        }
        catch
        {

        }

        return false;
    }

    [SupportedOSPlatform("windows")]
    private static bool CheckHyperVIndicators()
    {
        try
        {

            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Service WHERE Name LIKE '%vmbus%' OR Name LIKE '%hyperkbd%'");
            using var collection = searcher.Get();

            if (collection.Count > 0)
                return true;
        }
        catch
        {

        }

        return false;
    }

    [SupportedOSPlatform("windows")]
    private static bool CheckBIOSInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_BIOS");
            using var collection = searcher.Get();

            foreach (ManagementObject obj in collection)
            {
                var manufacturer = obj["Manufacturer"]?.ToString()?.ToLowerInvariant() ?? string.Empty;
                var version = obj["Version"]?.ToString()?.ToLowerInvariant() ?? string.Empty;
                var serialNumber = obj["SerialNumber"]?.ToString()?.ToLowerInvariant() ?? string.Empty;

                string[] vmIndicators = new[] { "vmware", "virtualbox", "vbox", "qemu", "hyper-v", "xen", "parallels", "kvm" };

                foreach (var indicator in vmIndicators)
                {
                    if (manufacturer.Contains(indicator) || version.Contains(indicator) || serialNumber.Contains(indicator))
                        return true;
                }
            }
        }
        catch
        {

        }

        return false;
    }

    [SupportedOSPlatform("windows")]
    private static bool CheckMACAddress()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_NetworkAdapter WHERE MACAddress IS NOT NULL");
            using var collection = searcher.Get();

            string[] vmMacPrefixes = new[]
            {
                "00:05:69",
                "00:0C:29",
                "00:1C:14",
                "00:50:56",
                "08:00:27",
                "00:15:5D",
                "00:16:3E",
                "00:1C:42"
            };

            foreach (ManagementObject obj in collection)
            {
                var macAddress = obj["MACAddress"]?.ToString()?.ToUpperInvariant() ?? string.Empty;

                foreach (var prefix in vmMacPrefixes)
                {
                    if (macAddress.StartsWith(prefix.ToUpperInvariant()))
                        return true;
                }
            }
        }
        catch
        {

        }

        return false;
    }

    [SupportedOSPlatform("windows")]
    private static bool CheckVMProcesses()
    {
        try
        {
            string[] vmProcesses = new[]
            {
                "vmtoolsd",
                "vmwaretray",
                "vmwareuser",
                "vboxservice",
                "vboxtray",
                "xenservice",
                "qemu-ga"
            };

            var runningProcesses = System.Diagnostics.Process.GetProcesses()
                .Select(p => p.ProcessName.ToLowerInvariant())
                .ToHashSet();

            foreach (var vmProc in vmProcesses)
            {
                if (runningProcesses.Contains(vmProc.ToLowerInvariant()))
                    return true;
            }
        }
        catch
        {

        }

        return false;
    }

    private static bool CheckLinuxVMIndicators()
    {
        try
        {

            string[] vmDevices = new[]
            {
                "/dev/vboxguest",
                "/dev/vboxuser",
                "/proc/vz",
                "/proc/xen"
            };

            foreach (var device in vmDevices)
            {
                if (Directory.Exists(device) || File.Exists(device))
                    return true;
            }

            if (File.Exists("/sys/class/dmi/id/product_name"))
            {
                var productName = File.ReadAllText("/sys/class/dmi/id/product_name").ToLowerInvariant();
                string[] vmIndicators = new[] { "vmware", "virtualbox", "qemu", "kvm", "xen" };

                if (vmIndicators.Any(indicator => productName.Contains(indicator)))
                    return true;
            }
        }
        catch
        {

        }

        return false;
    }

    public static string GetVirtualMachineType()
    {
        if (!IsRunningInVirtualMachine())
            return "Physical Machine";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (CheckVMWareRegistry())
                return "VMware";
            if (CheckVirtualBoxRegistry())
                return "VirtualBox";
            if (CheckHyperVIndicators())
                return "Hyper-V";
        }

        return "Virtual Machine (Unknown Type)";
    }
}

