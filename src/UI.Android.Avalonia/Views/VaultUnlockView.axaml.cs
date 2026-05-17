using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views;

public partial class VaultUnlockView : UserControl
{
    public VaultUnlockView() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
