using PhantomVault.Android.ViewModels;
namespace PhantomVault.Android.Pages;
public partial class ThemeSettingsPage : ContentPage
{
    public ThemeSettingsPage(ThemeSettingsViewModel vm) { InitializeComponent(); BindingContext = vm; }
}
