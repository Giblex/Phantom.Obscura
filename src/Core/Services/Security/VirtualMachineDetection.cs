using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PhantomVault.Core.Services.Security;

/// <summary>
/// Detects if the application is running inside a virtual machine.
/// SECURITY: Some policies may restrict execution in virtual environments to prevent analysis.
/// </summary>
public static class VirtualMachineDetection
{
    /// <summary>
    /// Performs comprehensive VM detection using multiple heuristics.
    /// </summary>
    /// <returns>True if running in a VM, false otherwise</returns>
    public static bool IsRunningInVirtualMachine()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // For non-Windows platforms, use basic checks
            return CheckLinuxVMIndicators();
        }

        // Multiple detection methods for Windows
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

    /// <summary>
    /// Checks if a hypervisor is present using CPUID instruction.
    /// This is the most reliable method on modern systems.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static bool CheckHypervisorPresent()
    {
        try
        {
            // Use WMI to check for hypervisor
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
            // Continue with other checks
        }

        return false;
    }

    /// <summary>
    /// Checks Windows Registry for VMware indicators.
    /// </summary>
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
                    // Check for VMware-specific values
                    var identifier = key.GetValue("Identifier")?.ToString()?.ToLowerInvariant() ?? string.Empty;
                    if (identifier.Contains("vmware"))
                        return true;

                    return true; // Key exists
                }
            }
        }
        catch
        {
            // Continue with other checks
        }

        return false;
    }

    /// <summary>
    /// Checks Windows Registry for VirtualBox indicators.
    /// </summary>
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
            // Continue with other checks
        }

        return false;
    }

    /// <summary>
    /// Checks for Hyper-V indicators.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static bool CheckHyperVIndicators()
    {
        try
        {
            // Check for Hyper-V services
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Service WHERE Name LIKE '%vmbus%' OR Name LIKE '%hyperkbd%'");
            using var collection = searcher.Get();

            if (collection.Count > 0)
                return true;
        }
        catch
        {
            // Continue with other checks
        }

        return false;
    }

    /// <summary>
    /// Checks BIOS information for VM indicators.
    /// </summary>
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
            // Continue with other checks
        }

        return false;
    }

    /// <summary>
    /// Checks MAC addresses for known VM vendor prefixes.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static bool CheckMACAddress()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_NetworkAdapter WHERE MACAddress IS NOT NULL");
            using var collection = searcher.Get();

            // Known VM MAC address prefixes
            string[] vmMacPrefixes = new[]
            {
                "00:05:69", // VMware
                "00:0C:29", // VMware
                "00:1C:14", // VMware
                "00:50:56", // VMware
                "08:00:27", // VirtualBox
                "00:15:5D", // Hyper-V
                "00:16:3E", // Xen
                "00:1C:42"  // Parallels
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
            // Continue with other checks
        }

        return false;
    }

    /// <summary>
    /// Checks for VM-specific processes running.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static bool CheckVMProcesses()
    {
        try
        {
            string[] vmProcesses = new[]
            {
                "vmtoolsd",      // VMware Tools
                "vmwaretray",    // VMware Tray
                "vmwareuser",    // VMware User
                "vboxservice",   // VirtualBox Service
                "vboxtray",      // VirtualBox Tray
                "xenservice",    // Xen Service
                "qemu-ga"        // QEMU Guest Agent
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
            // Continue with other checks
        }

        return false;
    }

    /// <summary>
    /// Checks for VM indicators on Linux systems.
    /// </summary>
    private static bool CheckLinuxVMIndicators()
    {
        try
        {
            // Check for common VM device files
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

            // Check DMI information
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
            // Fail closed - if we can't check, assume not in VM
        }

        return false;
    }

    /// <summary>
    /// Gets a detailed VM detection report.
    /// </summary>
    /// <returns>String describing detected VM environment or "Physical Machine"</returns>
    public static string GetVirtualMachineType()
    {
        if (!IsRunningInVirtualMachine())
            return "Physical Machine";

        // Try to determine specific VM type
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
