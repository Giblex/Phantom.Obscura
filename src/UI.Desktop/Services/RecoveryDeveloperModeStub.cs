namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Stub implementation for RecoveryDeveloperMode when PhantomRecovery is not available
    /// </summary>
    public static class RecoveryDeveloperMode
    {
        public static bool IsEnabled { get; set; } = false;
        public static string DeveloperVaultPath => string.Empty;
        public static string DeveloperUsbPath => string.Empty;
        public static bool SkipUsbBindingCheck { get; set; } = false;
        public static bool VerboseLogging { get; set; } = false;
        
        public static void Log(string message) { }
    }
}
