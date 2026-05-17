using PhantomVault.Android.ViewModels;

namespace PhantomVault.Android.Pages;

public partial class RecoveryPage : ContentPage
{
    public RecoveryPage(RecoveryViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
