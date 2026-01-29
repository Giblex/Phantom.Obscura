namespace PhantomVault.Core.Models.Security
{
    /// <summary>
    /// Represents the overall security posture of a vault based on detected threats.
    /// Used by the Defence Engine to track vault compromise level.
    /// </summary>
    public enum VaultSecurityState
    {
        /// <summary>
        /// Normal operation - no threats detected or all threats resolved.
        /// </summary>
        Normal,

        /// <summary>
        /// Suspicious activity detected - vault remains functional but under increased scrutiny.
        /// May trigger additional authentication requirements or defensive measures.
        /// </summary>
        Suspicious,

        /// <summary>
        /// Vault compromised - critical security breach detected.
        /// Vault should be locked, data scrubbed, and rekey required before normal operation resumes.
        /// </summary>
        Compromised
    }
}
