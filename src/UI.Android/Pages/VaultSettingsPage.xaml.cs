using PhantomVault.Android.ViewModels;
namespace PhantomVault.Android.Pages;
public partial class VaultSettingsPage : ContentPage
{
    public VaultSettingsPage(VaultSettingsViewModel vm) { InitializeComponent(); BindingContext = vm; }
}
