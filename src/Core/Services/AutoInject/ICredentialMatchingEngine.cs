using System.Collections.Generic;
using PhantomVault.Core.Models;
using PhantomVault.Core.Models.AutoInject;

namespace PhantomVault.Core.Services.AutoInject
{
    /// <summary>
    /// Service for matching credentials to the current context
    /// </summary>
    public interface ICredentialMatchingEngine
    {
        /// <summary>
        /// Finds credentials that match the given context
        /// </summary>
        /// <param name="context">The current window/application context</param>
        /// <param name="credentials">Available credentials to search</param>
        /// <returns>List of matching credentials, sorted by confidence</returns>
        IReadOnlyList<CredentialMatch> FindMatches(
            AutoInjectContext context,
            IEnumerable<Credential> credentials);

        /// <summary>
        /// Gets the best single match for the context
        /// </summary>
        /// <returns>Best match or null if no good match found</returns>
        CredentialMatch? GetBestMatch(
            AutoInjectContext context,
            IEnumerable<Credential> credentials);
    }
}
