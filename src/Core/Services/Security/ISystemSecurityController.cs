using System.Threading.Tasks;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Controls system-level security operations like cache clearing and sensitive data scrubbing.
    /// </summary>
    public interface ISystemSecurityController
    {
        /// <summary>
        /// Clears clipboard contents to prevent password leakage.
        /// </summary>
        Task ClearClipboardAsync();

        /// <summary>
        /// Scrubs short-lived sensitive data from memory (cached passwords, decrypted credentials).
        /// Does not affect the main vault which remains encrypted at rest.
        /// </summary>
        void ScrubSensitiveCaches();
    }
}
