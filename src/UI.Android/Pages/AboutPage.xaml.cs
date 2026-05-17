using PhantomVault.Android.ViewModels;
namespace PhantomVault.Android.Pages;
public partial class AboutPage : ContentPage
{
    public AboutPage(AboutViewModel vm) { InitializeComponent(); BindingContext = vm; }
}
