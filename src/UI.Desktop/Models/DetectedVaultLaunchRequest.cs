namespace PhantomVault.UI.Models
{
    public sealed class DetectedVaultLaunchRequest
    {
        public string UsbPath { get; init; } = string.Empty;
        public string UsbDisplayName { get; init; } = string.Empty;
        public string VaultPath { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public bool AutoOpenEligible { get; init; }
    }
}
