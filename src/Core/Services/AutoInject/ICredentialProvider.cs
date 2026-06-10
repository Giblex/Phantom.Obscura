using System.Collections.Generic;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.AutoInject
{

    public interface ICredentialProvider
    {

        IEnumerable<Credential> GetCredentials();

        Credential? GetCredentialByTitle(string title);

        bool IsVaultUnlocked();

        void UpdateLastUsed(string credentialTitle);
    }
}

