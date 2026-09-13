using System;
using System.ComponentModel;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// One account tile on a shared display card. When several entries belong to the same
    /// service (e.g. two GitHub logins), the card shows each as its own tile. The detail sections
    /// inside a tile bind exactly as they do on the single card, so this exposes the tile's entry
    /// as <see cref="SelectedCredential"/> and forwards the few vault-level members they use.
    /// </summary>
    public sealed class AccountTileContext : ReactiveObject, IDetailHost, IDisposable
    {
        private readonly VaultViewModel _vault;

        public AccountTileContext(VaultViewModel vault, CredentialViewModel account, int index, int count)
        {
            _vault = vault ?? throw new ArgumentNullException(nameof(vault));
            SelectedCredential = account ?? throw new ArgumentNullException(nameof(account));
            Index = index;
            Count = count;
            _vault.PropertyChanged += OnVaultPropertyChanged;
        }

        /// <summary>This tile's entry. Named like the vault's so the detail views bind unchanged.</summary>
        public CredentialViewModel SelectedCredential { get; }

        CredentialViewModel? IDetailHost.SelectedCredential => SelectedCredential;

        /// <summary>Shows the per-account edit button (the card header's Edit reaches only the first account).</summary>
        public bool IsAccountTile => true;

        public int Index { get; }
        public int Count { get; }

        public string AccountLabel => $"ACCOUNT {Index + 1} OF {Count}";

        public string AccountName => string.IsNullOrWhiteSpace(SelectedCredential.Username)
            ? SelectedCredential.Title
            : SelectedCredential.Username;

        // Vault-level members the detail sections bind to.
        public bool PrivacyModeEnabled => _vault.PrivacyModeEnabled;
        public bool IsAttestorAvailable => _vault.IsAttestorAvailable;
        public string AttestorStatus => _vault.AttestorStatus;
        public ReactiveCommand<CredentialViewModel, Unit> EditCredentialCommand => _vault.EditCredentialCommand;
        public ReactiveCommand<CredentialViewModel, Unit> DeleteCredentialCommand => _vault.DeleteCredentialCommand;
        public ReactiveCommand<CredentialViewModel, Unit> EditTotpCommand => _vault.EditTotpCommand;
        public ReactiveCommand<CredentialViewModel, Unit> OpenUrlCommand => _vault.OpenUrlCommand;
        public ReactiveCommand<Unit, Unit> OpenAttestorCommand => _vault.OpenAttestorCommand;

        public Task<bool> CopyFieldValueAsync(string? value, string label) => _vault.CopyFieldValueAsync(value, label);

        private void OnVaultPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(VaultViewModel.PrivacyModeEnabled):
                case nameof(VaultViewModel.IsAttestorAvailable):
                case nameof(VaultViewModel.AttestorStatus):
                    this.RaisePropertyChanged(e.PropertyName);
                    break;
            }
        }

        public void Dispose() => _vault.PropertyChanged -= OnVaultPropertyChanged;
    }
}
