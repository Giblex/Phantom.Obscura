using PhantomVault.Android.ViewModels;
using PhantomVault.Android.Services;
using PhantomVault.Core.Models;

namespace PhantomVault.Android.Pages;

public partial class SetupWizardPage : ContentPage
{
    private readonly SetupWizardViewModel _vm;
    private readonly VaultViewModel _vaultVm;
    private readonly MobileVaultService _vaultService;

    public SetupWizardPage(SetupWizardViewModel vm, VaultViewModel vaultVm, MobileVaultService vaultService)
    {
        InitializeComponent();
        _vm = vm;
        _vaultVm = vaultVm;
        _vaultService = vaultService;
        BindingContext = vm;

        vm.VaultCreated += OnVaultCreated;
    }

    private async void OnVaultCreated()
    {
        // Cache password now — VM clears it on next navigation
        var password = _vm.Password;
        try
        {
            var db = await _vaultService.OpenVaultAsync(password);
            _vaultVm.SetDatabase(db, password);
        }
        catch
        {
            // If open fails for any reason, still navigate to vault (empty state is handled there)
        }
        finally
        {
            password = string.Empty; // clear local copy
        }

        await Shell.Current.GoToAsync("//vault");
    }

    protected override bool OnBackButtonPressed() => true; // No going back from setup
}
