using PhantomVault.Android.ViewModels;
namespace PhantomVault.Android.Pages;
public partial class SharePage : ContentPage
{
    public SharePage(ShareViewModel vm) { InitializeComponent(); BindingContext = vm; }
    protected override void OnAppearing() { base.OnAppearing(); if (BindingContext is ShareViewModel vm) vm.LoadCredentials(); }
}
