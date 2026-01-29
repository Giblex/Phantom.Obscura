using System.Collections.Generic;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.AutoInject
{
    /// <summary>
    /// Abstraction for accessing vault credentials
    /// This decouples auto-inject from specific vault implementation
    /// </summary>
    public interface ICredentialProvider
    {
        /// <summary>
        /// Gets all credentials from the currently unlocked vault
        /// </summary>
        /// <returns>List of credentials, or empty if vault is locked</returns>
        IEnumerable<Credential> GetCredentials();

        /// <summary>
        /// Gets a specific credential by its title (unique identifier)
        /// </summary>
        Credential? GetCredentialByTitle(string title);

        /// <summary>
        /// Checks if the vault is currently unlocked
        /// </summary>
        bool IsVaultUnlocked();

        /// <summary>
        /// Updates the last used timestamp for a credential
        /// </summary>
        void UpdateLastUsed(string credentialTitle);
    }
}
