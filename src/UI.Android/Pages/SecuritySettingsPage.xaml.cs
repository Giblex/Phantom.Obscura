using PhantomVault.Android.ViewModels;

namespace PhantomVault.Android.Pages;

public partial class SecuritySettingsPage : ContentPage
{
    private readonly SecuritySettingsViewModel _vm;

    public SecuritySettingsPage(SecuritySettingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.InitializeAsync();
    }
}
