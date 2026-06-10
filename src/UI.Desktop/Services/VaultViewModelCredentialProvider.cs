using System;
using System.Collections.Generic;
using System.Linq;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services.AutoInject;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Desktop.Services
{

    public class VaultViewModelCredentialProvider : ICredentialProvider
    {
        private readonly VaultViewModel _vaultViewModel;

        public VaultViewModelCredentialProvider(VaultViewModel vaultViewModel)
        {
            _vaultViewModel = vaultViewModel ?? throw new ArgumentNullException(nameof(vaultViewModel));
        }

        public IEnumerable<Credential> GetCredentials()
        {

            return _vaultViewModel.FilteredCredentials
                .Select(vm => vm.GetCredential())
                .ToList();
        }

        public Credential? GetCredentialByTitle(string title)
        {
            if (string.IsNullOrEmpty(title))
                return null;

            var credentialVm = _vaultViewModel.FilteredCredentials
                .FirstOrDefault(c => c.Title.Equals(title, StringComparison.OrdinalIgnoreCase));

            return credentialVm?.GetCredential();
        }

        public bool IsVaultUnlocked()
        {

            return _vaultViewModel.FilteredCredentials.Any();
        }

        public void UpdateLastUsed(string credentialTitle)
        {
            if (string.IsNullOrEmpty(credentialTitle))
                return;

            var credentialVm = _vaultViewModel.FilteredCredentials
                .FirstOrDefault(c => c.Title.Equals(credentialTitle, StringComparison.OrdinalIgnoreCase));

            if (credentialVm != null)
            {
                var credential = credentialVm.GetCredential();
                credential.LastUsedUtc = DateTime.UtcNow;

            }
        }
    }
}

