using PhantomVault.Android.ViewModels;
using PhantomVault.Core.Models;

namespace PhantomVault.Android.Pages;

public partial class VaultPage : ContentPage
{
    private readonly VaultViewModel _vm;

    public VaultPage(VaultViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VaultViewModel.SelectedCredential) && vm.SelectedCredential != null)
                NavigateToDetail(vm.SelectedCredential);
        };
    }

    private async void NavigateToDetail(Credential credential)
    {
        await Shell.Current.GoToAsync("detail", new Dictionary<string, object>
        {
            ["credential"] = credential
        });

        _vm.SelectedCredential = null;
    }
}
