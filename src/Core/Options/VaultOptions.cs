namespace PhantomVault.Core.Options
{
    /// <summary>
    /// Holds configuration parameters for vault operations. These options
    /// should be supplied via dependency injection or configured per user
    /// environment. Avoid hard‑coding sensitive paths here.
    /// </summary>
    public class VaultOptions
    {
        /// <summary>
        /// Gets or sets the base directory used when mounting volumes on
        /// Unix‑like systems. For example "/mnt" or "/media/username". On
        /// Windows this property is ignored and drive letters are used
        /// instead.
        /// </summary>
        public string MountRoot { get; set; } = "/mnt";
    }
}