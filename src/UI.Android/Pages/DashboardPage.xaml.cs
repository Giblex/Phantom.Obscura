using PhantomVault.Android.ViewModels;
namespace PhantomVault.Android.Pages;
public partial class DashboardPage : ContentPage
{
    public DashboardPage(DashboardViewModel vm) { InitializeComponent(); BindingContext = vm; }
    protected override void OnAppearing() { base.OnAppearing(); if (BindingContext is DashboardViewModel vm) vm.Refresh(); }
}
