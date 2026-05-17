using PhantomVault.Android.ViewModels;
namespace PhantomVault.Android.Pages;
public partial class AccessibilitySettingsPage : ContentPage
{
    public AccessibilitySettingsPage(AccessibilitySettingsViewModel vm) { InitializeComponent(); BindingContext = vm; }
    protected override void OnAppearing() { base.OnAppearing(); if (BindingContext is AccessibilitySettingsViewModel vm) vm.Load(); }
}
