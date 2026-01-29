namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Severity level of a security threat. Used to filter and prioritize defence actions.
    /// </summary>
    public enum ThreatLevel
    {
        /// <summary>
        /// Informational event, logged for audit trail but no immediate action required.
        /// </summary>
        Info,

        /// <summary>
        /// Suspicious activity detected, warrants increased scrutiny and defensive measures.
        /// </summary>
        Warning,

        /// <summary>
        /// Critical security threat requiring immediate defensive response.
        /// </summary>
        Critical
    }
}
