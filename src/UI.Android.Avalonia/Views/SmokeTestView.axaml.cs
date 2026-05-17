using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views;

public partial class SmokeTestView : UserControl
{
    public SmokeTestView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
