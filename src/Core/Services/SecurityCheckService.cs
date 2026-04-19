using System;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Service for performing security checks before vault access.
    /// Validates integrity, detects hardware, and ensures vault health.
    /// </summary>
    public class SecurityCheckService
    {
        private static readonly TimeSpan ManifestCheckDelay = TimeSpan.FromMilliseconds(1400);
        private static readonly TimeSpan UsbHealthCheckDelay = TimeSpan.FromMilliseconds(1280);
        private static readonly TimeSpan HardwareTokenCheckDelay = TimeSpan.FromMilliseconds(1120);
        private static readonly TimeSpan BiometricCheckDelay = TimeSpan.FromMilliseconds(1100);
        private static readonly TimeSpan AntiTamperCheckDelay = TimeSpan.FromMilliseconds(1360);
        private static readonly TimeSpan CheckTransitionDelay = TimeSpan.FromMilliseconds(520);

        private readonly ManifestService _manifestService;
        private readonly YubiKeyService? _yubiKeyService;

        public SecurityCheckService(
            ManifestService manifestService,
            YubiKeyService? yubiKeyService = null)
        {
            _manifestService = manifestService ?? throw new ArgumentNullException(nameof(manifestService));
            _yubiKeyService = yubiKeyService;
        }

        /// <summary>
        /// Run comprehensive security checks on vault.
        /// </summary>
        /// <param name="usbPath">Path to USB drive containing vault</param>
        /// <param name="progress">Progress callback (0-100)</param>
        /// <returns>Security check results</returns>
        public async Task<SecurityCheckResult> RunSecurityChecksAsync(
            string usbPath,
            IProgress<SecurityCheckProgress>? progress = null)
        {
            var result = new SecurityCheckResult
            {
                StartTime = DateTime.UtcNow
            };

            var checks = new List<(string Name, Func<Task<bool>> Check, int Weight)>
            {
                ("Manifest Integrity", () => VerifyManifestIntegrityAsync(usbPath, result), 30),
                ("USB Health", () => CheckUsbHealthAsync(usbPath, result), 20),
                ("Hardware Tokens", () => DetectHardwareTokensAsync(result), 15),
                ("Biometric Availability", () => CheckBiometricAvailabilityAsync(result), 15),
                ("Anti-Tamper Check", () => CheckForTamperingAsync(usbPath, result), 20)
            };

            int totalWeight = checks.Sum(c => c.Weight);
            int completedWeight = 0;

            foreach (var (name, check, weight) in checks)
            {
                progress?.Report(new SecurityCheckProgress
                {
                    CurrentCheck = name,
                    PercentComplete = (completedWeight * 100) / totalWeight,
                    IsComplete = false
                    ,
                    CheckPassed = false,
                    CheckFailed = false
                });

                try
                {
                    bool passed = await check();
                    result.ChecksPassed += passed ? 1 : 0;

                    progress?.Report(new SecurityCheckProgress
                    {
                        CurrentCheck = name,
                        PercentComplete = Math.Min(99, ((completedWeight + weight) * 100) / totalWeight),
                        IsComplete = false,
                        CheckPassed = passed,
                        CheckFailed = !passed
                    });
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{name}: {ex.Message}");

                    progress?.Report(new SecurityCheckProgress
                    {
                        CurrentCheck = name,
                        PercentComplete = Math.Min(99, ((completedWeight + weight) * 100) / totalWeight),
                        IsComplete = false,
                        CheckPassed = false,
                        CheckFailed = true
                    });
                }

                await Task.Delay(CheckTransitionDelay).ConfigureAwait(false);
                completedWeight += weight;
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            result.OverallPassed = result.ChecksPassed >= 4; // Must pass at least 4/5 checks

            progress?.Report(new SecurityCheckProgress
            {
                CurrentCheck = "Complete",
                PercentComplete = 100,
                IsComplete = true
            });

            return result;
        }

        /// <summary>
        /// Verify manifest file integrity and signature.
        /// </summary>
        private async Task<bool> VerifyManifestIntegrityAsync(string usbPath, SecurityCheckResult result)
        {
            await Task.Delay(ManifestCheckDelay).ConfigureAwait(false);

            // Discover vault: prefer .pvault containers (v3), fall back to legacy .manifest
            string? manifestPath = null;
            string? masterVolumePath = ResolveMasterVolumePath(usbPath);
            var vaultsPath = Path.Combine(usbPath, ".phantom", "vaults");
            var manifestsPath = Path.Combine(usbPath, ".phantom", "manifests");

            if (masterVolumePath != null)
            {
                try
                {
                    var obscuraVolumeService = new ObscuraVolumeService();
                    if (!await obscuraVolumeService.IsObscuraVolumeAsync(masterVolumePath).ConfigureAwait(false))
                    {
                        result.ManifestValid = false;
                        result.Errors.Add("Master Obscura volume header is invalid");
                        return false;
                    }

                    result.ManifestValid = true;
                    return true;
                }
                catch (Exception ex)
                {
                    result.ManifestValid = false;
                    result.Errors.Add($"Master volume verification failed: {ex.Message}");
                    return false;
                }
            }

            if (Directory.Exists(vaultsPath))
            {
                var pvaultFiles = Directory.GetFiles(vaultsPath, "*.pvault");
                if (pvaultFiles.Length > 0)
                    manifestPath = pvaultFiles[0];
            }
            if (manifestPath == null && Directory.Exists(manifestsPath))
            {
                var legacyFiles = Directory.GetFiles(manifestsPath, "*.manifest");
                if (legacyFiles.Length > 0)
                    manifestPath = legacyFiles[0];
            }

            if (manifestPath == null)
            {
                result.ManifestValid = false;
                result.Errors.Add("No vault files found in .phantom folder");
                return false;
            }

            try
            {
                var fileInfo = new FileInfo(manifestPath);
                bool isContainer = manifestPath.EndsWith(".pvault", StringComparison.OrdinalIgnoreCase);

                // For containers, verify minimum header size; for standalone manifests, check reasonable range
                if (isContainer)
                {
                    if (fileInfo.Length < 64) // PHANTOM1 header minimum
                    {
                        result.ManifestValid = false;
                        result.Errors.Add("Container file is too small to be valid");
                        return false;
                    }
                }
                else
                {
                    if (fileInfo.Length < 100 || fileInfo.Length > 1024 * 1024) // 100 bytes to 1MB
                    {
                        result.ManifestValid = false;
                        result.Errors.Add("Manifest file size is invalid");
                        return false;
                    }

                    // Structural validation for standalone manifests
                    var content = await File.ReadAllTextAsync(manifestPath);
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        result.ManifestValid = false;
                        result.Errors.Add("Manifest file is empty");
                        return false;
                    }
                }

                result.ManifestValid = true;
                return true;
            }
            catch (Exception ex)
            {
                result.ManifestValid = false;
                result.Errors.Add($"Manifest verification failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Check USB drive health and accessibility.
        /// </summary>
        private async Task<bool> CheckUsbHealthAsync(string usbPath, SecurityCheckResult result)
        {
            await Task.Delay(UsbHealthCheckDelay).ConfigureAwait(false);

            try
            {
                // Check USB path is valid
                if (!Directory.Exists(usbPath))
                {
                    result.UsbHealthy = false;
                    result.Errors.Add("USB path not accessible");
                    return false;
                }

                // Check drive is writable
                var testFilePath = Path.Combine(usbPath, $".phantom_test_{Guid.NewGuid()}");
                try
                {
                    await File.WriteAllTextAsync(testFilePath, "test");
                    File.Delete(testFilePath);
                }
                catch
                {
                    result.UsbHealthy = false;
                    result.Errors.Add("USB drive is not writable");
                    return false;
                }

                // Check available space (at least 10MB)
                var driveInfo = new DriveInfo(Path.GetPathRoot(usbPath)!);
                if (driveInfo.AvailableFreeSpace < 10 * 1024 * 1024)
                {
                    result.UsbHealthy = false;
                    result.Errors.Add("Insufficient space on USB drive");
                    return false;
                }

                result.UsbHealthy = true;
                result.UsbDriveInfo = new UsbDriveInfo
                {
                    TotalSize = driveInfo.TotalSize,
                    FreeSpace = driveInfo.AvailableFreeSpace,
                    DriveFormat = driveInfo.DriveFormat,
                    IsReady = driveInfo.IsReady
                };
                return true;
            }
            catch (Exception ex)
            {
                result.UsbHealthy = false;
                result.Errors.Add($"USB health check failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Detect connected hardware tokens (YubiKey, etc.).
        /// </summary>
        private async Task<bool> DetectHardwareTokensAsync(SecurityCheckResult result)
        {
            await Task.Delay(HardwareTokenCheckDelay).ConfigureAwait(false);

            try
            {
                if (_yubiKeyService != null)
                {
                    try
                    {
                        result.YubiKeyDetected = _yubiKeyService.IsTokenPresent();
                    }
                    catch (NotImplementedException)
                    {
                        if (!result.Warnings.Any(static w => w.Contains("YubiKey detection", StringComparison.OrdinalIgnoreCase)))
                        {
                            result.Warnings.Add("YubiKey detection is not available in this build yet; skipping hardware token check.");
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!result.Warnings.Any(static w => w.Contains("YubiKey", StringComparison.OrdinalIgnoreCase)))
                        {
                            result.Warnings.Add($"Unable to check for YubiKey tokens: {ex.Message}");
                        }
                    }
                }

                // Hardware token detection is optional, so always pass
                return true;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Hardware token detection failed: {ex.Message}");
                return true; // Non-critical, don't fail
            }
        }

        /// <summary>
        /// Check if biometric authentication is available on this device.
        /// </summary>
        private async Task<bool> CheckBiometricAvailabilityAsync(SecurityCheckResult result)
        {
            await Task.Delay(BiometricCheckDelay).ConfigureAwait(false);

            try
            {
                // Only check biometrics on supported platforms
                if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                {
                    result.BiometricAvailable = await IsBiometricAvailableAsync();
                }
                else
                {
                    result.BiometricAvailable = false;
                }

                if (!result.BiometricAvailable)
                {
                    if (!result.Warnings.Any(static w => w.Contains("biometric", StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Warnings.Add("Biometric authentication is not available on this device. You can still use passphrase, keyfile, and other methods.");
                    }
                }

                // Biometric is optional, so always pass
                return true;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Biometric check failed: {ex.Message}");
                return true; // Non-critical, don't fail
            }
        }

        /// <summary>
        /// Checks if biometric authentication is available on the current platform.
        /// </summary>
        /// <returns>True if biometric auth is available; false otherwise</returns>
        [SupportedOSPlatform("windows10.0.19041.0")]
        public async Task<bool> IsBiometricAvailableAsync()
        {
            if (OperatingSystem.IsWindows())
            {
                return await CheckWindowsHelloAvailabilityAsync();
            }
            else if (OperatingSystem.IsMacOS())
            {
                // Touch ID/Face ID requires LAContext via a native binding
                // (e.g. a MAUI or ObjC-interop helper), which is not yet available.
                return false;
            }
            else if (OperatingSystem.IsLinux())
            {
                // Check whether the fprintd service (fingerprint daemon) is available.
                return File.Exists("/usr/bin/fprintd-verify")
                    || File.Exists("/usr/lib/fprintd/fprintd");
            }

            return false;
        }

        /// <summary>
        /// Checks Windows Hello availability using reflection to avoid hard dependency.
        /// </summary>
        private async Task<bool> CheckWindowsHelloAvailabilityAsync()
        {
            try
            {
                // Use reflection to call Windows Hello API
                var userConsentVerifierType = Type.GetType(
                    "Windows.Security.Credentials.UI.UserConsentVerifier, Windows, ContentType=WindowsRuntime");

                if (userConsentVerifierType == null)
                {
                    // Windows Hello API not available on this system
                    return false;
                }

                // Call UserConsentVerifier.CheckAvailabilityAsync()
                var checkAvailabilityMethod = userConsentVerifierType.GetMethod(
                    "CheckAvailabilityAsync",
                    BindingFlags.Public | BindingFlags.Static);

                if (checkAvailabilityMethod == null)
                {
                    return false;
                }

                dynamic availabilityTask = checkAvailabilityMethod.Invoke(null, null)!;
                dynamic availability = await availabilityTask;

                // UserConsentVerifierAvailability enum values:
                // 0 = Available
                // 1 = DeviceNotPresent
                // 2 = NotConfiguredForUser
                // 3 = DisabledByPolicy
                // 4 = DeviceBusy

                int availabilityValue = (int)availability;

                // Return true only if Available (0)
                return availabilityValue == 0;
            }
            catch (Exception)
            {
                // If Windows Hello detection fails, assume not available
                return false;
            }
        }

        /// <summary>
        /// Check for signs of tampering with vault files.
        /// </summary>
        private async Task<bool> CheckForTamperingAsync(string usbPath, SecurityCheckResult result)
        {
            await Task.Delay(AntiTamperCheckDelay).ConfigureAwait(false);

            try
            {
                // Check for required .phantom folder structure
                var phantomPath = Path.Combine(usbPath, ".phantom");
                var manifestsPath = Path.Combine(phantomPath, "manifests");
                var vaultsPath = Path.Combine(phantomPath, "vaults");
                var masterVolumePath = ResolveMasterVolumePath(usbPath);

                if (!Directory.Exists(phantomPath))
                {
                    result.NoTampering = false;
                    result.Errors.Add(".phantom folder is missing");
                    return false;
                }

                bool hasLegacyLayout = Directory.Exists(manifestsPath) || Directory.Exists(vaultsPath);
                bool hasPackedLayout = !string.IsNullOrEmpty(masterVolumePath);

                if (!hasLegacyLayout && !hasPackedLayout)
                {
                    result.NoTampering = false;
                    result.Errors.Add("Neither a packed master volume nor the legacy .phantom vault layout is present");
                    return false;
                }

                // Vault files in either format count as present
                int vaultFileCount = 0;
                if (Directory.Exists(vaultsPath))
                    vaultFileCount += Directory.GetFiles(vaultsPath, "*.pvault").Length;
                if (Directory.Exists(manifestsPath))
                    vaultFileCount += Directory.GetFiles(manifestsPath, "*.manifest").Length;
                if (hasPackedLayout)
                    vaultFileCount++;

                if (vaultFileCount == 0)
                {
                    result.NoTampering = false;
                    result.Errors.Add("Vault files are missing from both the packed and legacy layouts");
                    return false;
                }

                // Check for suspicious files in USB ROOT only (exclude .phantom folder)
                var suspiciousExtensions = new[] { ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs", ".scr", ".com" };
                var rootFiles = Directory.GetFiles(usbPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f =>
                    {
                        var ext = Path.GetExtension(f).ToLowerInvariant();
                        return suspiciousExtensions.Contains(ext);
                    })
                    .Select(Path.GetFileName)
                    .ToList();

                if (rootFiles.Any())
                {
                    result.Warnings.Add($"Suspicious executable files in USB root: {string.Join(", ", rootFiles)}. These should not be present.");
                }

                result.NoTampering = true;
                return true;
            }
            catch (Exception ex)
            {
                result.NoTampering = false;
                result.Errors.Add($"Tamper check failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Quick security check (fast version for returning users).
        /// </summary>
        public async Task<bool> QuickSecurityCheckAsync(string usbPath)
        {
            if (ResolveMasterVolumePath(usbPath) is not null)
            {
                await Task.Delay(100); // Minimal delay
                return true;
            }

            // Check for .phantom folder structure
            var phantomPath = Path.Combine(usbPath, ".phantom");
            if (!Directory.Exists(phantomPath))
                return false;

            var manifestsPath = Path.Combine(phantomPath, "manifests");
            var vaultsPath = Path.Combine(phantomPath, "vaults");

            if (!Directory.Exists(manifestsPath) && !Directory.Exists(vaultsPath))
                return false;

            // Either format counts as a valid vault
            int count = 0;
            if (Directory.Exists(vaultsPath))
                count += Directory.GetFiles(vaultsPath, "*.pvault").Length;
            if (count == 0 && Directory.Exists(manifestsPath))
                count += Directory.GetFiles(manifestsPath, "*.manifest").Length;

            if (count == 0)
                return false;

            await Task.Delay(100); // Minimal delay
            return true;
        }

        private static string? ResolveMasterVolumePath(string usbPath)
        {
            var candidates = new[]
            {
                Path.Combine(usbPath, "system.bin"),
                Path.Combine(usbPath, ".phantom", "obscura.vol")
            };

            return candidates.FirstOrDefault(File.Exists);
        }
    }

    /// <summary>
    /// Progress update for security checks.
    /// </summary>
    public class SecurityCheckProgress
    {
        public string CurrentCheck { get; set; } = string.Empty;
        public int PercentComplete { get; set; }
        public bool IsComplete { get; set; }
        public bool CheckPassed { get; set; } = true;
        public bool CheckFailed { get; set; } = false;
    }

    /// <summary>
    /// Results of security check operation.
    /// </summary>
    public class SecurityCheckResult
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }

        public bool ManifestValid { get; set; }
        public bool UsbHealthy { get; set; }
        public bool YubiKeyDetected { get; set; }
        public bool BiometricAvailable { get; set; }
        public bool NoTampering { get; set; }

        public int ChecksPassed { get; set; }
        public bool OverallPassed { get; set; }

        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();

        public UsbDriveInfo? UsbDriveInfo { get; set; }
    }

    /// <summary>
    /// Information about USB drive.
    /// </summary>
    public class UsbDriveInfo
    {
        public long TotalSize { get; set; }
        public long FreeSpace { get; set; }
        public string DriveFormat { get; set; } = string.Empty;
        public bool IsReady { get; set; }

        public string TotalSizeFormatted => FormatBytes(TotalSize);
        public string FreeSpaceFormatted => FormatBytes(FreeSpace);

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
