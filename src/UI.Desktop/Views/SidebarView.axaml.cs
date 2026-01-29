using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views;

/// <summary>
/// Sidebar view with entry type filters, quick access buttons, categories, and management options.
/// </summary>
public partial class SidebarView : UserControl
{
    public SidebarView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
