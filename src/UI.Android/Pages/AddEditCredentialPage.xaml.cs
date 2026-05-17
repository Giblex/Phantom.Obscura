using PhantomVault.Android.ViewModels;
using PhantomVault.Core.Models;

namespace PhantomVault.Android.Pages;

[QueryProperty(nameof(Credential), "credential")]
[QueryProperty(nameof(IsEdit), "isEdit")]
public partial class AddEditCredentialPage : ContentPage
{
    private readonly AddEditCredentialViewModel _vm;
    private readonly VaultViewModel _vaultVm;

    public AddEditCredentialPage(AddEditCredentialViewModel vm, VaultViewModel vaultVm)
    {
        InitializeComponent();
        _vm = vm;
        _vaultVm = vaultVm;
        BindingContext = vm;

        vm.CredentialSaved += OnCredentialSaved;
    }

    private bool _initializedViaQuery;

    public Credential? Credential
    {
        set
        {
            if (value != null)
            {
                _vm.InitEdit(value);
                _initializedViaQuery = true;
            }
        }
    }

    public bool IsEdit
    {
        set
        {
            _initializedViaQuery = true;
            if (!value)
                _vm.InitNew();
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Called when navigating with no query params (plain "add new" path)
        if (!_initializedViaQuery)
            _vm.InitNew();
        _initializedViaQuery = false;
    }

    private async void OnCredentialSaved(Credential credential)
    {
        _vaultVm.UpsertCredential(credential);
        await Shell.Current.GoToAsync("..");
    }
}
