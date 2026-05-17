using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views;

public partial class SecurityDashboardView : UserControl
{
    public SecurityDashboardView() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
