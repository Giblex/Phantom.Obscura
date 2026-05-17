using PhantomVault.Android.ViewModels;
using PhantomVault.Core.Models;

namespace PhantomVault.Android.Pages;

public partial class UnlockPage : ContentPage
{
    private readonly UnlockViewModel _vm;
    private readonly VaultViewModel _vaultVm;

    public UnlockPage(UnlockViewModel vm, VaultViewModel vaultVm)
    {
        InitializeComponent();
        _vm = vm;
        _vaultVm = vaultVm;
        BindingContext = vm;

        vm.VaultOpened += OnVaultOpened;
        vm.GateLost    += OnGateLost;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // The Welcome gate owns USB+vault checking. If we landed here with no
        // vault present (e.g. user popped the drive on the previous screen),
        // bounce back to the gate immediately.
        if (!_vm.VaultExists)
        {
            OnGateLost();
            return;
        }
        PasswordEntry.Focus();
    }

    private async void OnVaultOpened(VaultDatabase database, string masterPassword)
    {
        _vaultVm.SetDatabase(database, masterPassword);
        await Shell.Current.GoToAsync("//main");
    }

    private async void OnGateLost()
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try { await Shell.Current.GoToAsync("//welcome"); }
            catch { /* ignore: we may already be navigating */ }
        });
    }

    protected override bool OnBackButtonPressed() => true; // Prevent back to nothing
}
