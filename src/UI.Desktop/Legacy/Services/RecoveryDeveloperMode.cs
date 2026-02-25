using System;
using System.IO;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Developer mode configuration for PhantomRecovery integration
    /// Provides easy testing paths and bypass options
    /// </summary>
    public static class RecoveryDeveloperMode
    {
        private static bool _isEnabled;
        private static string? _devVaultPath;
        private static string? _devUsbPath;

        /// <summary>
        /// Enable developer mode for recovery vault
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
        /// Developer vault path (defaults to a test directory)
        /// </summary>
        public static string DeveloperVaultPath
        {
            get
            {
                if (string.IsNullOrEmpty(_devVaultPath))
                {
                    _devVaultPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "PhantomRecoveryVault_Dev"
                    );
                }
                return _devVaultPath;
            }
            set => _devVaultPath = value;
        }

        /// <summary>
        /// Developer USB path (simulated USB directory for testing)
        /// </summary>
        public static string DeveloperUsbPath
        {
            get
            {
                if (string.IsNullOrEmpty(_devUsbPath))
                {
                    _devUsbPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "PhantomRecovery_SimulatedUSB"
                    );
                }
                return _devUsbPath;
            }
            set => _devUsbPath = value;
        }

        /// <summary>
        /// Default master secret for developer mode
        /// </summary>
        public const string DeveloperMasterSecret = "dev-phantom-recovery-master-2025";

        /// <summary>
        /// Default recovery PIN for developer mode
        /// </summary>
        public const string DeveloperRecoveryPin = "1234";

        /// <summary>
        /// Skip USB binding checks in developer mode
        /// </summary>
        public static bool SkipUsbBindingCheck { get; set; } = true;

        /// <summary>
        /// Auto-create vault structure in developer mode
        /// </summary>
        public static bool AutoCreateVault { get; set; } = true;

        /// <summary>
        /// Verbose logging in developer mode
        /// </summary>
        public static bool VerboseLogging { get; set; } = true;

        private static void InitializeDeveloperPaths()
        {
            try
            {
                // Ensure developer directories exist
                if (!Directory.Exists(DeveloperVaultPath))
                {
                    Directory.CreateDirectory(DeveloperVaultPath);
                }

                if (!Directory.Exists(DeveloperUsbPath))
                {
                    Directory.CreateDirectory(DeveloperUsbPath);
                }

                if (VerboseLogging)
                {
                    System.Diagnostics.Debug.WriteLine($"[RecoveryDeveloperMode] Initialized");
                    System.Diagnostics.Debug.WriteLine($"  Vault Path: {DeveloperVaultPath}");
                    System.Diagnostics.Debug.WriteLine($"  USB Path: {DeveloperUsbPath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RecoveryDeveloperMode] Failed to initialize: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets configuration for developer mode vault launch
        /// </summary>
        public static PhantomRecovery.App.Integration.VaultLaunchOptions GetDeveloperLaunchOptions()
        {
            return new PhantomRecovery.App.Integration.VaultLaunchOptions
            {
                VaultPath = DeveloperVaultPath,
                MasterSecret = DeveloperMasterSecret,
                RecoveryPin = DeveloperRecoveryPin,
                AutoOpenOnLaunch = true,
                CreateIfMissing = AutoCreateVault
            };
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

                if (VerboseLogging)
                {
                    System.Diagnostics.Debug.WriteLine("[RecoveryDeveloperMode] Cleaned up developer data");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RecoveryDeveloperMode] Cleanup failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Logs a message in developer mode
        /// </summary>
        public static void Log(string message)
        {
            if (IsEnabled && VerboseLogging)
            {
                System.Diagnostics.Debug.WriteLine($"[RecoveryDev] {message}");
            }
        }
    }
}
