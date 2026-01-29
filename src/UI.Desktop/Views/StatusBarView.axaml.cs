using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views;

/// <summary>
/// Status bar view showing vault status, sync time, and encryption indicator.
/// </summary>
public partial class StatusBarView : UserControl
{
    public StatusBarView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
