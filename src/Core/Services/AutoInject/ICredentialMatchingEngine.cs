using System.Collections.Generic;
using PhantomVault.Core.Models;
using PhantomVault.Core.Models.AutoInject;

namespace PhantomVault.Core.Services.AutoInject
{

    public interface ICredentialMatchingEngine
    {

        IReadOnlyList<CredentialMatch> FindMatches(
            AutoInjectContext context,
            IEnumerable<Credential> credentials);

        CredentialMatch? GetBestMatch(
            AutoInjectContext context,
            IEnumerable<Credential> credentials);
    }
}

