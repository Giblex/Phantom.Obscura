using PhantomVault.Android.ViewModels;
namespace PhantomVault.Android.Pages;
public partial class BackupSnapshotsPage : ContentPage
{
    public BackupSnapshotsPage(BackupSnapshotsViewModel vm) { InitializeComponent(); BindingContext = vm; }
    protected override void OnAppearing() { base.OnAppearing(); if (BindingContext is BackupSnapshotsViewModel vm) _ = vm.RefreshAsync(); }
}
