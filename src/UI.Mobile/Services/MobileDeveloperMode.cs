using System;
using System.IO;

namespace PhantomVault.UI.Mobile.Services
{
    /// <summary>
    /// Developer mode configuration for PhantomVault Mobile
    /// Provides bypass options for testing on Android emulators
    /// </summary>
    public static class MobileDeveloperMode
    {
        private static bool _isEnabled;
        private static string? _devVaultPath;
        private static string? _devUsbPath;

        /// <summary>
        /// Enable developer mode for mobile vault testing
        /// Automatically enabled in DEBUG builds on emulators
        /// </summary>
        public static bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                if (value)
                {
                    InitializeDeveloperPaths();
                }
            }
        }

        /// <summary>
        /// Developer vault path (mobile storage)
        /// </summary>
        public static string DeveloperVaultPath
        {
            get
            {
                if (string.IsNullOrEmpty(_devVaultPath))
                {
#if ANDROID
                    _devVaultPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "PhantomVault_DevAndroid"
                    );
#else
                    _devVaultPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "PhantomVault_DevMobile"
                    );
#endif
                }
                return _devVaultPath;
            }
            set => _devVaultPath = value;
        }

        /// <summary>
        /// Developer USB path (simulated USB for emulator testing)
        /// </summary>
        public static string DeveloperUsbPath
        {
            get
            {
                if (string.IsNullOrEmpty(_devUsbPath))
                {
#if ANDROID
                    _devUsbPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "PhantomVault_SimulatedUSB_Android"
                    );
#else
                    _devUsbPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "PhantomVault_SimulatedUSB_Mobile"
                    );
#endif
                }
                return _devUsbPath;
            }
            set => _devUsbPath = value;
        }

        /// <summary>
        /// Default master password for developer mode
        /// </summary>
        public const string DeveloperMasterPassword = "DevMaster2025!";

        /// <summary>
        /// Default PIN for developer mode
        /// </summary>
        public const string DeveloperPin = "1234";

        /// <summary>
        /// Skip USB binding checks in developer mode
        /// </summary>
        public static bool SkipUsbBindingCheck { get; set; } = true;

        /// <summary>
        /// Skip biometric authentication in developer mode
        /// </summary>
        public static bool SkipBiometricAuth { get; set; } = true;

        /// <summary>
        /// Auto-create vault structure in developer mode
        /// </summary>
        public static bool AutoCreateVault { get; set; } = true;

        /// <summary>
        /// Bypass PhantomKey requirement (for emulator testing)
        /// </summary>
        public static bool BypassPhantomKey { get; set; } = true;

        /// <summary>
        /// Verbose logging in developer mode
        /// </summary>
        public static bool VerboseLogging { get; set; } = true;

        /// <summary>
        /// Check if running on Android emulator
        /// </summary>
        public static bool IsAndroidEmulator
        {
            get
            {
#if ANDROID
                var brand = Android.OS.Build.Brand;
                var device = Android.OS.Build.Device;
                var product = Android.OS.Build.Product;
                var model = Android.OS.Build.Model;

                // Common emulator identifiers
                return brand?.Contains("generic", StringComparison.OrdinalIgnoreCase) == true ||
                       device?.Contains("generic", StringComparison.OrdinalIgnoreCase) == true ||
                       product?.Contains("sdk", StringComparison.OrdinalIgnoreCase) == true ||
                       product?.Contains("emulator", StringComparison.OrdinalIgnoreCase) == true ||
                       model?.Contains("Emulator", StringComparison.OrdinalIgnoreCase) == true ||
                       model?.Contains("Android SDK", StringComparison.OrdinalIgnoreCase) == true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Auto-enable developer mode on emulators in DEBUG builds
        /// </summary>
        public static void AutoConfigureForEmulator()
        {
#if DEBUG
            if (IsAndroidEmulator)
            {
                IsEnabled = true;
                Log("Developer mode auto-enabled for Android emulator");
            }
#endif
        }

        private static void InitializeDeveloperPaths()
        {
            try
            {
                // Ensure developer directories exist
                if (!Directory.Exists(DeveloperVaultPath))
                {
                    Directory.CreateDirectory(DeveloperVaultPath);
                    Log($"Created developer vault directory: {DeveloperVaultPath}");
                }

                if (!Directory.Exists(DeveloperUsbPath))
                {
                    Directory.CreateDirectory(DeveloperUsbPath);
                    Log($"Created developer USB directory: {DeveloperUsbPath}");
                }

                Log("Developer mode initialized");
            }
            catch (Exception ex)
            {
                Log($"Failed to initialize developer mode: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets developer mode launch options
        /// </summary>
        public static (string vaultPath, string? usbPath, bool skipChecks) GetDeveloperLaunchOptions()
        {
            return (
                DeveloperVaultPath,
                SkipUsbBindingCheck ? null : DeveloperUsbPath,
                true
            );
        }

        /// <summary>
        /// Cleans up developer mode data
        /// </summary>
        public static void CleanupDeveloperData()
        {
            try
            {
                if (Directory.Exists(DeveloperVaultPath))
                {
                    Directory.Delete(DeveloperVaultPath, true);
                }

                if (Directory.Exists(DeveloperUsbPath))
                {
                    Directory.Delete(DeveloperUsbPath, true);
                }

                Log("Developer data cleaned up");
            }
            catch (Exception ex)
            {
                Log($"Cleanup failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Logs a message in developer mode
        /// </summary>
        public static void Log(string message)
        {
            if (VerboseLogging)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[MobileDeveloperMode] {message}");
                Console.WriteLine($"[MobileDeveloperMode] {message}");
#endif
            }
        }

        /// <summary>
        /// Get device information for debugging
        /// </summary>
        public static string GetDeviceInfo()
        {
#if ANDROID
            return $"Brand: {Android.OS.Build.Brand}, " +
                   $"Device: {Android.OS.Build.Device}, " +
                   $"Model: {Android.OS.Build.Model}, " +
                   $"Product: {Android.OS.Build.Product}, " +
                   $"Emulator: {IsAndroidEmulator}";
#else
            return "Non-Android platform";
#endif
        }
    }
}
