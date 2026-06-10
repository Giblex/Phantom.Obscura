using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.Autofill
{

    public interface ICredentialRepository
    {

        Task<List<Credential>> GetCredentialsByDomainAsync(string domain, CancellationToken cancellationToken = default);

        Task SaveCredentialAsync(Credential credential, CancellationToken cancellationToken = default);

        Task UpdateCredentialAsync(Credential credential, CancellationToken cancellationToken = default);

        Task DeleteCredentialAsync(string title, CancellationToken cancellationToken = default);

        Task<List<Credential>> GetAllCredentialsAsync(CancellationToken cancellationToken = default);
    }
}

