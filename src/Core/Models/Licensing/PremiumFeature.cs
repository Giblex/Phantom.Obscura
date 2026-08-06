namespace PhantomVault.Core.Models.Licensing
{
    /// <summary>
    /// Individually gateable premium capabilities. A token may grant the whole
    /// Premium tier (all features) or an explicit subset via its feature list.
    /// </summary>
    public enum PremiumFeature
    {
        AdvancedSettings,
        CustomThemes,
        FullSecurityDashboard,
        AdvancedCategoryManager,
        IconManager,
        MultiVault,
        EncryptedDriveMount
    }
}
