using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.Autofill
{
    /// <summary>
    /// Interface for credential storage and retrieval.
    /// Implementations should handle encrypted storage of credentials.
    /// </summary>
    public interface ICredentialRepository
    {
        /// <summary>
        /// Retrieves credentials matching the specified domain.
        /// </summary>
        /// <param name="domain">The domain to search for (e.g., "example.com")</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of matching credentials</returns>
        Task<List<Credential>> GetCredentialsByDomainAsync(string domain, CancellationToken cancellationToken = default);

        /// <summary>
        /// Saves a credential to the repository.
        /// </summary>
        /// <param name="credential">The credential to save</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task SaveCredentialAsync(Credential credential, CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates an existing credential.
        /// </summary>
        /// <param name="credential">The credential to update</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task UpdateCredentialAsync(Credential credential, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a credential from the repository.
        /// </summary>
        /// <param name="title">The title/ID of the credential to delete</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task DeleteCredentialAsync(string title, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves all stored credentials.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of all credentials</returns>
        Task<List<Credential>> GetAllCredentialsAsync(CancellationToken cancellationToken = default);
    }
}
