namespace PhantomVault.Core.Models.Licensing
{
    /// <summary>
    /// Subscription tier a vault is currently entitled to. Free is the failsafe
    /// default whenever no valid signed license token is present.
    /// </summary>
    public enum PremiumTier
    {
        Free = 0,
        Premium = 1
    }
}
