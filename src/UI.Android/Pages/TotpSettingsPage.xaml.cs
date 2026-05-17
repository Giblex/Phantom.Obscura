using PhantomVault.Android.ViewModels;
namespace PhantomVault.Android.Pages;
public partial class TotpSettingsPage : ContentPage
{
    public TotpSettingsPage(TotpSettingsViewModel vm) { InitializeComponent(); BindingContext = vm; }
}
