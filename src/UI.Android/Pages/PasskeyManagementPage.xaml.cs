using PhantomVault.Android.ViewModels;

namespace PhantomVault.Android.Pages;

public partial class PasskeyManagementPage : ContentPage
{
    public PasskeyManagementPage(PasskeyManagementViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
