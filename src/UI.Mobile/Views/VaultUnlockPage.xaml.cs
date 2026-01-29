using PhantomVault.UI.Mobile.ViewModels;

namespace PhantomVault.UI.Mobile.Views;

public partial class VaultUnlockPage : ContentPage
{
    public VaultUnlockPage(VaultUnlockViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Refresh USB drives when page appears
        if (BindingContext is VaultUnlockViewModel vm)
        {
            // Trigger refresh through a command if needed
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Clean up if needed
        if (BindingContext is VaultUnlockViewModel vm)
        {
            vm.Dispose();
        }
    }
}
