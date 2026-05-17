using PhantomVault.Android.ViewModels;

namespace PhantomVault.Android.Pages;

public partial class ImportExportPage : ContentPage
{
    public ImportExportPage(ImportExportViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
