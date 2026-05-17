using PhantomVault.Android.ViewModels;

namespace PhantomVault.Android.Pages;

public partial class TotpScannerPage : ContentPage
{
    public TotpScannerPage(TotpScannerViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
