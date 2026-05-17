using PhantomVault.Android.ViewModels;

namespace PhantomVault.Android.Pages;

public partial class PasswordGeneratorPage : ContentPage
{
    public PasswordGeneratorPage(PasswordGeneratorViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
