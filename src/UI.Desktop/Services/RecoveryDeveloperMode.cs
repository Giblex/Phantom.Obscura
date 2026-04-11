using System;
using System.Diagnostics;
using System.IO;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Developer helpers for suite-local PhantomRecovery integration.
    /// </summary>
    public static class RecoveryDeveloperMode
    {
        public static bool IsAvailable => !string.IsNullOrWhiteSpace(DeveloperVaultPath);
        public static string AvailabilityMessage => IsAvailable
            ? $"Developer recovery vault path resolved to {DeveloperVaultPath}"
            : "Developer recovery vault path could not be resolved.";
        public static bool IsEnabled { get; set; }
        public static string DeveloperVaultPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhantomRecoveryVault");
        public static string DeveloperUsbPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhantomRecoveryUsb");
        public static bool SkipUsbBindingCheck { get; set; }
        public static bool VerboseLogging { get; set; }

        public static void Log(string message)
        {
            if (!VerboseLogging && !IsEnabled)
            {
                return;
            }

            Debug.WriteLine($"[RecoveryDeveloperMode] {message}");
        }
    }
}
