using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using ReactiveUI;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model for sharing credentials with trusted contacts. It
    /// allows the user to select a credential, enter the recipient's
    /// RSA public key (in PEM format) and generate a share package
    /// using <see cref="SharingService"/>. The resulting JSON can be
    /// transmitted via email or messaging. A real application would
    /// include additional UI for importing keys and sending shares.
    /// </summary>
    public sealed class ShareViewModel : ReactiveObject
    {
        private readonly SharingService _sharingService;
        private Credential? _selectedCredential;
        private string _recipientPublicKeyPem = string.Empty;
        private string _sharePackageJson = string.Empty;

        public ShareViewModel(SharingService sharingService)
        {
            _sharingService = sharingService;
            Credentials = new ObservableCollection<Credential>();
            ShareCommand = ReactiveCommand.CreateFromTask(ShareAsync, this.WhenAnyValue(vm => vm.SelectedCredential, vm => vm.RecipientPublicKeyPem, (cred, pem) => cred != null && !string.IsNullOrWhiteSpace(pem)));
        }

        /// <summary>
        /// List of available credentials to share. Populated by the
        /// caller from the decrypted vault database.
        /// </summary>
        public ObservableCollection<Credential> Credentials { get; }

        /// <summary>
        /// Credential selected for sharing.
        /// </summary>
        public Credential? SelectedCredential
        {
            get => _selectedCredential;
            set => this.RaiseAndSetIfChanged(ref _selectedCredential, value);
        }

        /// <summary>
        /// Recipient's RSA public key in PEM format. The PEM should
        /// contain a base64‑encoded SubjectPublicKeyInfo. Only the
        /// public key is required; private key material must never be
        /// entered into this field.
        /// </summary>
        public string RecipientPublicKeyPem
        {
            get => _recipientPublicKeyPem;
            set => this.RaiseAndSetIfChanged(ref _recipientPublicKeyPem, value);
        }

        /// <summary>
        /// JSON representation of the generated share package. After
        /// calling <see cref="ShareCommand"/>, this property contains
        /// the Base64‑encoded ciphertext, encrypted symmetric key and
        /// metadata required by the recipient to decrypt the
        /// credential.
        /// </summary>
        public string SharePackageJson
        {
            get => _sharePackageJson;
            private set => this.RaiseAndSetIfChanged(ref _sharePackageJson, value);
        }

        /// <summary>
        /// Command that triggers creation of the share. Enabled only
        /// when a credential is selected and a PEM has been entered.
        /// </summary>
        public ReactiveCommand<Unit, Unit> ShareCommand { get; }

        private async Task ShareAsync()
        {
            if (SelectedCredential == null) return;
            try
            {
                using RSA rsa = RSA.Create();
                rsa.ImportFromPem(RecipientPublicKeyPem.ToCharArray());
                RSAParameters pub = rsa.ExportParameters(false);
                var share = await _sharingService.CreateShareAsync(SelectedCredential, pub);
                SharePackageJson = JsonSerializer.Serialize(share, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                SharePackageJson = $"Error: {ex.Message}";
            }
        }
    }
}