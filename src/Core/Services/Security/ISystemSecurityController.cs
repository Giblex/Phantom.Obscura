using System;
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

        /// <summary>
        /// Registers a platform-specific callback that performs the actual clipboard clear.
        /// Called by the UI layer during startup so the Core controller can clear the clipboard
        /// without depending on a UI framework.
        /// </summary>
        void RegisterClipboardClearer(Func<Task> clearer);
    }
}
