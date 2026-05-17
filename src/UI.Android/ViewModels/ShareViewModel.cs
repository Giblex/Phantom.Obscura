using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;

namespace PhantomVault.Android.ViewModels
{
    public sealed partial class ShareViewModel : BaseViewModel
    {
        private readonly VaultViewModel _vault;
        private readonly SharingService _sharingService;

        [ObservableProperty] private ObservableCollection<Credential> _credentials = new();
        [ObservableProperty] private Credential? _selectedCredential;
        [ObservableProperty] private string _recipientPublicKeyPem = string.Empty;
        [ObservableProperty] private string _sharePackageJson = string.Empty;

        public ShareViewModel(VaultViewModel vault, SharingService sharingService)
        {
            _vault = vault;
            _sharingService = sharingService;
        }

        public void LoadCredentials()
        {
            Credentials.Clear();
            foreach (var c in _vault.Credentials) Credentials.Add(c);
        }

        [RelayCommand]
        private async Task ShareAsync()
        {
            if (SelectedCredential is null) { ErrorMessage = "Please select a credential."; return; }
            if (string.IsNullOrWhiteSpace(RecipientPublicKeyPem)) { ErrorMessage = "Please enter the recipient's public key."; return; }

            await RunSafeAsync(async () =>
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(RecipientPublicKeyPem.ToCharArray());
                var pub = rsa.ExportParameters(false);
                var share = await _sharingService.CreateShareAsync(SelectedCredential, pub);
                SharePackageJson = JsonSerializer.Serialize(share, new JsonSerializerOptions { WriteIndented = true });
                StatusMessage = "Share package generated.";
            }, "Generating…");
        }

        [RelayCommand]
        private async Task CopyPackageAsync()
        {
            if (string.IsNullOrEmpty(SharePackageJson)) return;
            await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.SetTextAsync(SharePackageJson);
            StatusMessage = "Share package copied to clipboard.";
        }

        [RelayCommand]
        private async Task NativeShareAsync()
        {
            if (string.IsNullOrEmpty(SharePackageJson)) return;
            await Microsoft.Maui.ApplicationModel.DataTransfer.Share.Default.RequestAsync(
                new Microsoft.Maui.ApplicationModel.DataTransfer.ShareTextRequest
                {
                    Title = $"Share: {SelectedCredential?.Title}",
                    Text = SharePackageJson
                });
        }
    }
}
