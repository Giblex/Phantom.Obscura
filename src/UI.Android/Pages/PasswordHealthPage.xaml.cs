using PhantomVault.Android.ViewModels;

namespace PhantomVault.Android.Pages;

public partial class PasswordHealthPage : ContentPage
{
    public PasswordHealthPage(PasswordHealthViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
