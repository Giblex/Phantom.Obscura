using PhantomVault.Android.ViewModels;

namespace PhantomVault.Android.Pages;

public partial class DuplicateScanPage : ContentPage
{
    private readonly DuplicateScanViewModel _vm;

    public DuplicateScanPage(DuplicateScanViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Auto-scan on first open
        if (!_vm.Scanned) _vm.ScanCommand.Execute(null);
    }
}
