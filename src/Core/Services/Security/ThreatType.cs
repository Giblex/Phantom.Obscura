namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Defines categories of security threats that the Defence Engine monitors.
    /// Each type corresponds to a specific attack vector or suspicious behavior.
    /// </summary>
    public enum ThreatType
    {
        /// <summary>
        /// Multiple failed login attempts in quick succession, indicating brute-force attack.
        /// </summary>
        FailedLoginBurst,

        /// <summary>
        /// Vault accessed from a new device fingerprint not previously seen.
        /// </summary>
        NewDeviceFingerprint,

        /// <summary>
        /// Integrity check failed - vault or manifest tampered with.
        /// </summary>
        IntegrityMismatch,

        /// <summary>
        /// Unusually high number of credential exports in short timespan.
        /// </summary>
        ExcessiveExports,

        /// <summary>
        /// Rapid creation of many entries, possibly automated data exfiltration.
        /// </summary>
        HighRiskEntryFlood,

        /// <summary>
        /// User behavior deviates significantly from established patterns.
        /// </summary>
        BehaviourDeviation,

        /// <summary>
        /// Physical removal of vault device while unlocked or during sensitive operation.
        /// </summary>
        PhysicalRemoval
    }
}
