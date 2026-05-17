using PhantomVault.Android.ViewModels;
using PhantomVault.Core.Models;

namespace PhantomVault.Android.Pages;

[QueryProperty(nameof(Credential), "credential")]
public partial class CredentialDetailPage : ContentPage
{
    private readonly CredentialDetailViewModel _vm;

    public CredentialDetailPage(CredentialDetailViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    public Credential? Credential
    {
        set => _vm.Credential = value;
    }

    private async void OnEditTapped(object sender, EventArgs e)
    {
        if (_vm.Credential is null) return;
        await Shell.Current.GoToAsync("edit", new Dictionary<string, object>
        {
            ["credential"] = _vm.Credential,
            ["isEdit"] = true
        });
    }

    private async void OnCopyWifiPasswordTapped(object s, EventArgs e)
        => await CopyAsync(_vm.Credential?.WiFiPassword, "WiFi password copied");

    private async void OnCopyCardNumberTapped(object s, EventArgs e)
        => await CopyAsync(_vm.Credential?.CardNumber, "Card number copied");

    private async void OnCopyCvvTapped(object s, EventArgs e)
        => await CopyAsync(_vm.Credential?.CardCVV, "CVV copied");

    private async void OnCopyBankAccountTapped(object s, EventArgs e)
        => await CopyAsync(_vm.Credential?.BankAccountNumber, "Account number copied");

    private async void OnCopyApiKeyTapped(object s, EventArgs e)
        => await CopyAsync(_vm.Credential?.ApiKeyValue, "API key copied");

    private async void OnCopyPinTapped(object s, EventArgs e)
        => await CopyAsync(_vm.Credential?.PinValue, "PIN copied");

    private static async Task CopyAsync(string? value, string message)
    {
        if (string.IsNullOrEmpty(value)) return;
        await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.SetTextAsync(value);
        // Auto-clear after 30s
        _ = Task.Delay(30_000).ContinueWith(_ =>
            Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.SetTextAsync(string.Empty));
    }
}
