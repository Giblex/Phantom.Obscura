using PhantomVault.Android.ViewModels;

namespace PhantomVault.Android.Pages;

public partial class AutofillSettingsPage : ContentPage
{
    private readonly AutofillSettingsViewModel _vm;

    public AutofillSettingsPage(AutofillSettingsViewModel vm)
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
