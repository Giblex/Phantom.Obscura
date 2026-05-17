using PhantomVault.Android.ViewModels;
namespace PhantomVault.Android.Pages;
public partial class SecurityDashboardPage : ContentPage
{
    public SecurityDashboardPage(SecurityDashboardViewModel vm) { InitializeComponent(); BindingContext = vm; }
    protected override void OnAppearing() { base.OnAppearing(); if (BindingContext is SecurityDashboardViewModel vm) vm.Refresh(); }
}
