namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Defines automated defensive actions that can be taken in response to threats.
    /// </summary>
    public enum DefenceActionType
    {
        /// <summary>
        /// Add artificial delay to authentication attempts to slow down attackers.
        /// </summary>
        AddDelay,

        /// <summary>
        /// Temporarily lock the vault for a cooldown period.
        /// </summary>
        TempLockout,

        /// <summary>
        /// Require physical PhantomKey/Keyfile for next unlock attempt.
        /// </summary>
        RequirePhantomKey,

        /// <summary>
        /// Switch to decoy vault showing fake credentials to mislead attacker.
        /// </summary>
        SwitchToDecoyVault,

        /// <summary>
        /// Enter read-only mode preventing modifications to vault data.
        /// </summary>
        EnterReadOnlyMode,

        /// <summary>
        /// Clear clipboard and scrub short-lived sensitive data from memory.
        /// </summary>
        ScrubShortLivedData
    }
}
