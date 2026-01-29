namespace PhantomVault.UI.Models
{
    /// <summary>
    /// Represents the type of status message to display with appropriate icon
    /// </summary>
    public enum StatusType
    {
        /// <summary>
        /// No status - hide icon
        /// </summary>
        None,

        /// <summary>
        /// Success/completed - green checkmark icon
        /// </summary>
        Success,

        /// <summary>
        /// Warning/caution - yellow warning icon
        /// </summary>
        Warning,

        /// <summary>
        /// Error/failure - red X/close icon
        /// </summary>
        Error,

        /// <summary>
        /// Information - blue info icon
        /// </summary>
        Info
    }
}
