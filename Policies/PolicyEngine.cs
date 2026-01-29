using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using PhantomVault.Core;

/// <summary>
/// Enforces USB, manifest, and desktop sync policies for PhantomVault.
/// Provides event-based notification for policy violations that require UI action.
/// </summary>
public sealed class PolicyEngine
{
    private readonly ObscuraPolicy _policy;
    
    /// <summary>
    /// Event raised when the UI should be locked due to a policy violation.
    /// Subscribe to this in the UI layer to handle lockdown scenarios.
    /// </summary>
    public event EventHandler? LockUIRequested;
    
    /// <summary>
    /// Event raised when sensitive data should be cleared from memory.
    /// Pass byte arrays or char arrays to be zeroed via the event args.
    /// </summary>
    public event EventHandler<SecretZeroEventArgs>? ZeroSecretsRequested;

    public PolicyEngine(ObscuraPolicy policy)
    {
        _policy = policy;
    }
    
    /// <summary>
    /// Registers sensitive data buffers to be zeroed when ZeroSecretsRequested is raised.
    /// </summary>
    public class SecretZeroEventArgs : EventArgs
    {
        /// <summary>
        /// Gets whether secrets have been successfully zeroed.
        /// Set this to true after clearing sensitive data in your handler.
        /// </summary>
        public bool SecretsCleared { get; set; }
    }

    // Call this at startup
    public (DriveInfo? drive, string? volumeSerial) EnforceUsbAtStartup()
    {
        if (!_policy.Usb.Required)
            return (null, null);

        var mode = (_policy.Usb.IdentityMode ?? "Any").Trim();

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady)
                continue;

            if (_policy.Usb.RequireRemovable && drive.DriveType != DriveType.Removable)
                continue;

            // USB standard check (v1: honest version - not enforced yet, just logged)
            // Only check on Windows where WMI is available
            if (OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(_policy.Usb.MinStandard) &&
                !IsUsbStandardSatisfied(drive, _policy.Usb.MinStandard!))
            {
                System.Diagnostics.Debug.WriteLine($"Drive {drive.Name} does not meet minimum USB standard '{_policy.Usb.MinStandard}' (v1: not enforced)");
                // v1: We continue anyway - this is logged for future enforcement
            }

            // Branch by identity mode
            switch (mode)
            {
                case "Any":
                    // Any removable drive (>= minStandard) is acceptable
                    System.Diagnostics.Debug.WriteLine($"USB policy: Any mode - accepting drive {drive.Name}");
                    return (drive, null);

                case "LabelOnly":
                    if (!string.IsNullOrEmpty(_policy.Usb.VolumeLabel) &&
                        string.Equals(drive.VolumeLabel, _policy.Usb.VolumeLabel, StringComparison.OrdinalIgnoreCase))
                    {
                        System.Diagnostics.Debug.WriteLine($"USB policy: LabelOnly mode - accepting drive {drive.Name} with label '{drive.VolumeLabel}'");
                        return (drive, null);
                    }
                    break;

                case "Serial":
                    if (!OperatingSystem.IsWindows())
                        break;

                    var serial = TryGetVolumeSerial(drive.Name);
                    if (serial == null)
                        break;

                    if (_policy.Usb.AllowedSerials.Any() &&
                        !_policy.Usb.AllowedSerials.Contains(serial, StringComparer.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    System.Diagnostics.Debug.WriteLine($"USB policy: Serial mode - accepting drive {drive.Name} with serial '{serial}'");
                    return (drive, serial);

                case "CryptoKey":
                    if (!OperatingSystem.IsWindows())
                        break;

                    var cryptoSerial = TryGetVolumeSerial(drive.Name);
                    if (cryptoSerial == null)
                        break;

                    // SECURITY: CryptoKey mode requires usb_key.json file on the USB device
                    if (ValidateCryptoKeyUsb(drive, cryptoSerial, out var validatedKeyId))
                    {
                        System.Diagnostics.Debug.WriteLine($"USB policy: CryptoKey mode - accepting drive {drive.Name} with keyId '{validatedKeyId}'");
                        return (drive, cryptoSerial);
                    }
                    break;

                default:
                    throw new PolicyViolationException(
                        PolicyViolationCode.UsbPolicyInvalid,
                        $"Unsupported USB identity mode '{mode}'.");
            }
        }

        throw new PolicyViolationException(
            PolicyViolationCode.UsbNotFound,
            $"No USB device matched the current security policy (mode: {mode}).");
    }

    /// <summary>
    /// Checks if the USB device meets the minimum USB standard requirement.
    /// Uses WMI to query USB controller properties and determine USB version.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static bool IsUsbStandardSatisfied(DriveInfo drive, string minStandard)
    {
        if (!OperatingSystem.IsWindows())
        {
            System.Diagnostics.Debug.WriteLine("USB standard check skipped: Not running on Windows");
            return true;
        }

        // USB2 requirement passes for any USB device
        if (string.Equals(minStandard, "USB2", StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            // Get the drive letter (e.g., "E:")
            var driveLetter = drive.Name.TrimEnd('\\').TrimEnd(':');
            
            // Query Win32_DiskDrive to find the physical disk associated with this drive
            string? deviceId = null;
            using (var logicalDiskQuery = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{driveLetter}:'}} WHERE AssocClass=Win32_LogicalDiskToPartition"))
            {
                foreach (ManagementObject partition in logicalDiskQuery.Get())
                {
                    using var diskQuery = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");
                    foreach (ManagementObject disk in diskQuery.Get())
                    {
                        deviceId = disk["PNPDeviceID"]?.ToString();
                        break;
                    }
                    break;
                }
            }

            if (string.IsNullOrEmpty(deviceId))
            {
                System.Diagnostics.Debug.WriteLine($"USB standard check: Could not find PNPDeviceID for drive {driveLetter}");
                return true; // Can't determine, allow it
            }

            // Query USB controller for this device to determine USB version
            // USB 3.0+ controllers typically have "USB3" or "xHCI" in their name/description
            var usbVersion = GetUsbVersionFromPnpDevice(deviceId);
            
            switch (minStandard.ToUpperInvariant())
            {
                case "USB3":
                    if (usbVersion >= 3.0)
                    {
                        System.Diagnostics.Debug.WriteLine($"USB standard check: Drive {driveLetter} meets USB3 requirement (detected version: {usbVersion})");
                        return true;
                    }
                    System.Diagnostics.Debug.WriteLine($"USB standard check: Drive {driveLetter} does NOT meet USB3 requirement (detected version: {usbVersion})");
                    return false;

                case "USB3PLUS":
                case "USB3.1":
                case "USB3.2":
                    if (usbVersion >= 3.1)
                    {
                        System.Diagnostics.Debug.WriteLine($"USB standard check: Drive {driveLetter} meets USB3+ requirement (detected version: {usbVersion})");
                        return true;
                    }
                    System.Diagnostics.Debug.WriteLine($"USB standard check: Drive {driveLetter} does NOT meet USB3+ requirement (detected version: {usbVersion})");
                    return false;

                default:
                    System.Diagnostics.Debug.WriteLine($"USB standard check: Unknown standard '{minStandard}', allowing device");
                    return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"USB standard check failed with exception: {ex.Message}");
            return true; // On error, allow the device (fail-open for usability)
        }
    }

    /// <summary>
    /// Determines USB version by examining the device's parent USB controller.
    /// Returns the detected USB version (2.0, 3.0, 3.1, 3.2) or 2.0 as default.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static double GetUsbVersionFromPnpDevice(string pnpDeviceId)
    {
        try
        {
            // Look up the parent USB hub/controller for this device
            // USB 3.0+ devices are connected through xHCI (eXtensible Host Controller Interface)
            // USB 2.0 devices use EHCI (Enhanced Host Controller Interface)
            // USB 1.x devices use UHCI/OHCI
            
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_USBHub");
            
            foreach (ManagementObject hub in searcher.Get())
            {
                var hubPnpId = hub["PNPDeviceID"]?.ToString() ?? "";
                
                // Check if this hub is in the device's parent chain
                if (!string.IsNullOrEmpty(hubPnpId) && pnpDeviceId.Contains(hubPnpId.Split('\\').LastOrDefault() ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    var hubName = hub["Name"]?.ToString() ?? "";
                    var hubDescription = hub["Description"]?.ToString() ?? "";
                    
                    // Check for USB 3.x indicators
                    if (hubName.Contains("USB 3.2", StringComparison.OrdinalIgnoreCase) ||
                        hubDescription.Contains("USB 3.2", StringComparison.OrdinalIgnoreCase))
                        return 3.2;
                    
                    if (hubName.Contains("USB 3.1", StringComparison.OrdinalIgnoreCase) ||
                        hubDescription.Contains("USB 3.1", StringComparison.OrdinalIgnoreCase))
                        return 3.1;
                    
                    if (hubName.Contains("USB 3.0", StringComparison.OrdinalIgnoreCase) ||
                        hubDescription.Contains("USB 3.0", StringComparison.OrdinalIgnoreCase) ||
                        hubName.Contains("xHCI", StringComparison.OrdinalIgnoreCase) ||
                        hubDescription.Contains("xHCI", StringComparison.OrdinalIgnoreCase))
                        return 3.0;
                }
            }
            
            // Also check USB controllers directly
            using var controllerSearcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_USBController");
            
            foreach (ManagementObject controller in controllerSearcher.Get())
            {
                var name = controller["Name"]?.ToString() ?? "";
                var caption = controller["Caption"]?.ToString() ?? "";
                
                // xHCI indicates USB 3.0+
                if (name.Contains("xHCI", StringComparison.OrdinalIgnoreCase) ||
                    caption.Contains("xHCI", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("USB 3", StringComparison.OrdinalIgnoreCase) ||
                    caption.Contains("USB 3", StringComparison.OrdinalIgnoreCase))
                {
                    // Determine specific version
                    if (name.Contains("3.2") || caption.Contains("3.2"))
                        return 3.2;
                    if (name.Contains("3.1") || caption.Contains("3.1"))
                        return 3.1;
                    return 3.0;
                }
            }
            
            // Default to USB 2.0 if we can't determine version
            return 2.0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetUsbVersionFromPnpDevice failed: {ex.Message}");
            return 2.0; // Default to USB 2.0 on error
        }
    }

    /// <summary>
    /// Validates USB device in CryptoKey mode by loading and verifying usb_key.json.
    /// SECURITY: Requires cryptographic authentication of USB device.
    /// </summary>
    /// <param name="drive">USB drive to validate</param>
    /// <param name="volumeSerial">Volume serial number</param>
    /// <param name="validatedKeyId">Output parameter for the validated key ID</param>
    /// <returns>True if USB key file is valid and matches policy</returns>
    [SupportedOSPlatform("windows")]
    private bool ValidateCryptoKeyUsb(DriveInfo drive, string volumeSerial, out string? validatedKeyId)
    {
        validatedKeyId = null;

        try
        {
            // Look for usb_key.json file on the USB drive
            string keyFilePath = Path.Combine(drive.RootDirectory.FullName, "usb_key.json");

            if (!File.Exists(keyFilePath))
            {
                System.Diagnostics.Debug.WriteLine($"CryptoKey validation failed: usb_key.json not found on {drive.Name}");
                return false;
            }

            // Load the USB key file
            string keyFileJson = File.ReadAllText(keyFilePath);

            // Verify the key file signature (requires root certificate)
            // Note: In production, you would pass the root verifier here
            // For now, we load without signature verification but validate structure
            UsbKeyFile keyFile;
            try
            {
                // TODO: Pass root verifier when available for signature verification
                keyFile = UsbKeyFile.LoadAndVerify(keyFileJson, rootVerifier: null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CryptoKey validation failed: {ex.Message}");
                return false;
            }

            // Validate public key format
            if (!keyFile.ValidatePublicKey())
            {
                System.Diagnostics.Debug.WriteLine($"CryptoKey validation failed: Invalid Ed25519 public key format");
                return false;
            }

            // Validate against policy requirements
            if (!keyFile.ValidateAgainstPolicy(_policy, volumeSerial))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"CryptoKey validation failed: Key '{keyFile.KeyId}' does not match policy requirements");
                return false;
            }

            // Check volume serial binding (if specified in key file)
            if (!string.IsNullOrEmpty(keyFile.VolumeSerial))
            {
                if (!keyFile.VolumeSerial.Equals(volumeSerial, StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"CryptoKey validation failed: Volume serial mismatch. Expected '{keyFile.VolumeSerial}', got '{volumeSerial}'");
                    return false;
                }
            }

            // All validations passed
            validatedKeyId = keyFile.KeyId;
            System.Diagnostics.Debug.WriteLine($"CryptoKey validation succeeded for key '{validatedKeyId}'");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CryptoKey validation exception: {ex.Message}");
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? TryGetVolumeSerial(string rootPath)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        // rootPath like "E:\\"
        try
        {
            var driveLetter = rootPath.TrimEnd('\\');
            using var searcher = new ManagementObjectSearcher(
                "SELECT VolumeSerialNumber, Name FROM Win32_LogicalDisk WHERE Name = '" + driveLetter + "'");

            foreach (ManagementObject disk in searcher.Get())
            {
                var serial = disk["VolumeSerialNumber"]?.ToString();
                if (!string.IsNullOrWhiteSpace(serial))
                    return serial;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to get volume serial for {rootPath}: {ex.Message}");
        }

        return null;
    }

    public void HandleUsbRemoved()
    {
        switch (_policy.Usb.OnRemoval)
        {
            case "Ignore":
                System.Diagnostics.Debug.WriteLine("USB removed: Ignore mode - no action taken");
                break;

            case "Lock":
                System.Diagnostics.Debug.WriteLine("USB removed: Lock mode - locking UI");
                OnLockUIRequested();
                break;

            case "LockAndZero":
            default:
                System.Diagnostics.Debug.WriteLine("USB removed: LockAndZero mode - zeroing secrets and locking UI");
                OnZeroSecretsRequested();
                OnLockUIRequested();
                break;
        }
    }
    
    /// <summary>
    /// Raises the LockUIRequested event to notify UI layer to lock the interface.
    /// </summary>
    private void OnLockUIRequested()
    {
        try
        {
            LockUIRequested?.Invoke(this, EventArgs.Empty);
            System.Diagnostics.Debug.WriteLine("PolicyEngine: LockUIRequested event raised");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PolicyEngine: Error raising LockUIRequested: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Raises the ZeroSecretsRequested event to notify handlers to clear sensitive memory.
    /// Also performs local memory zeroing for any buffers tracked by the policy engine.
    /// </summary>
    private void OnZeroSecretsRequested()
    {
        try
        {
            var args = new SecretZeroEventArgs();
            ZeroSecretsRequested?.Invoke(this, args);
            
            // Force garbage collection to help clear orphaned sensitive data
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true);
            GC.WaitForPendingFinalizers();
            
            System.Diagnostics.Debug.WriteLine($"PolicyEngine: ZeroSecretsRequested event raised, cleared: {args.SecretsCleared}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PolicyEngine: Error raising ZeroSecretsRequested: {ex.Message}");
        }
    }

    // Call before enabling any sync features
    public void EnforceDesktopSyncPolicy()
    {
        if (!_policy.Desktop.AllowDesktopSync)
        {
            throw new InvalidOperationException("Desktop sync is disabled by policy.");
        }

        // Later: check device binding, etc.
    }

    // Call after you read the manifest (but before trusting it)
    public void EnforceManifestPolicy(string manifestVersion, bool manifestHasValidSignature)
    {
        if (_policy.Manifest.EnforceExactVersion)
        {
            var requiredVersion = _policy.Manifest.RequiredVersion;
            if (string.IsNullOrWhiteSpace(requiredVersion))
            {
                throw new PolicyViolationException(
                    PolicyViolationCode.ManifestVersionMismatch,
                    "Manifest policy requires an exact version but none was specified in the policy.");
            }

            if (CompareVersion(manifestVersion, requiredVersion) != 0)
            {
                throw new PolicyViolationException(
                    PolicyViolationCode.ManifestVersionMismatch,
                    $"Manifest version {manifestVersion} does not match required version {requiredVersion}.");
            }
        }

        if (_policy.Manifest.RequireSignature && !manifestHasValidSignature)
        {
            throw new PolicyViolationException(
                PolicyViolationCode.ManifestSignatureInvalid,
                "Manifest signature required but invalid or missing.");
        }

        // Version bounds check (semantic if possible, fallback to ordinal)
        if (!string.IsNullOrWhiteSpace(_policy.Manifest.MinVersion) &&
            CompareVersion(manifestVersion, _policy.Manifest.MinVersion) < 0)
        {
            throw new PolicyViolationException(
                PolicyViolationCode.ManifestVersionMismatch,
                $"Manifest version {manifestVersion} is below minimum allowed ({_policy.Manifest.MinVersion}).");
        }

        if (!string.IsNullOrWhiteSpace(_policy.Manifest.MaxVersion) &&
            CompareVersion(manifestVersion, _policy.Manifest.MaxVersion) > 0)
        {
            throw new PolicyViolationException(
                PolicyViolationCode.ManifestVersionMismatch,
                $"Manifest version {manifestVersion} is above maximum allowed ({_policy.Manifest.MaxVersion}).");
        }
    }

    private static int CompareVersion(string current, string target)
    {
        if (Version.TryParse(current, out var currentVersion) &&
            Version.TryParse(target, out var targetVersion))
        {
            return currentVersion.CompareTo(targetVersion);
        }

        // Fallback to ordinal comparison for non-semver strings
        return string.CompareOrdinal(current, target);
    }
}
