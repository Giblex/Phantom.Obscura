using PhantomVault.Android.ViewModels;

namespace PhantomVault.Android.Pages;

public partial class CategoryPage : ContentPage
{
    public CategoryPage(CategoryViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
