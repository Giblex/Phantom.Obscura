using PhantomVault.Android.ViewModels;

namespace PhantomVault.Android.Pages;

public partial class TrashPage : ContentPage
{
    private readonly TrashViewModel _vm;

    public TrashPage(TrashViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }
}
