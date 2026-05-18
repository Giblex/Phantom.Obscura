using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views;

public partial class CategoryLandingView : UserControl
{
    public CategoryLandingView() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
