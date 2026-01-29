using System;
using System.Collections.Generic;
using System.Linq;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services.AutoInject;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Desktop.Services
{
    /// <summary>
    /// Adapter that implements ICredentialProvider by wrapping VaultViewModel
    /// Allows auto-inject system to access credentials from the vault
    /// </summary>
    public class VaultViewModelCredentialProvider : ICredentialProvider
    {
        private readonly VaultViewModel _vaultViewModel;

        public VaultViewModelCredentialProvider(VaultViewModel vaultViewModel)
        {
            _vaultViewModel = vaultViewModel ?? throw new ArgumentNullException(nameof(vaultViewModel));
        }

        public IEnumerable<Credential> GetCredentials()
        {
            // Convert CredentialViewModel collection to Credential collection
            // FilteredCredentials includes both regular credentials and passkeys
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
            // Vault is considered unlocked if it has been mounted
            // _mountPath is set when vault is successfully mounted
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

                // Note: VaultViewModel will auto-save changes when credentials are modified
                // The PropertyChanged event will trigger the save mechanism
            }
        }
    }
}
