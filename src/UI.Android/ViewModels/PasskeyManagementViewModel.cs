using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Core.Models;

namespace PhantomVault.Android.ViewModels
{
    public sealed partial class PasskeyManagementViewModel : BaseViewModel
    {
        private readonly VaultViewModel _vaultVm;

        [ObservableProperty] private ObservableCollection<Credential> _passkeys = new();
        [ObservableProperty] private bool _hasPasskeys;

        public PasskeyManagementViewModel(VaultViewModel vaultVm)
        {
            _vaultVm = vaultVm;
            Refresh();
            _vaultVm.Credentials.CollectionChanged += (_, _) => Refresh();
        }

        private void Refresh()
        {
            Passkeys.Clear();
            foreach (var c in _vaultVm.Credentials.Where(c => c.IsPasskey).OrderBy(c => c.Title))
                Passkeys.Add(c);
            HasPasskeys = Passkeys.Count > 0;
        }

        [RelayCommand]
        private async Task OpenPasskeyAsync(Credential? c)
        {
            if (c is null) return;
            await Shell.Current.GoToAsync("detail", new Dictionary<string, object> { ["credential"] = c });
        }

        [RelayCommand]
        private void DeletePasskey(Credential? c)
        {
            if (c is null) return;
            _vaultVm.DeleteCredentialCommand.Execute(c);
        }

        [RelayCommand]
        private async Task AddPasskeyPlaceholderAsync()
        {
            // Passkeys are created by the OS authenticator and registered via FIDO2 / WebAuthn.
            // On Android this is done via CredentialManager API (not directly available in MAUI).
            // This action creates a manual placeholder credential so users can track which
            // sites they have passkeys registered on.
            var credential = new Credential
            {
                Id = Guid.NewGuid().ToString(),
                EntryType = EntryType.Password,
                IsPasskey = true,
                Title = "New Passkey",
                CreatedUtc = DateTimeOffset.UtcNow,
                LastUpdatedUtc = DateTimeOffset.UtcNow
            };
            await Shell.Current.GoToAsync("edit", new Dictionary<string, object>
            {
                ["credential"] = credential,
                ["isEdit"] = false
            });
        }
    }
}
