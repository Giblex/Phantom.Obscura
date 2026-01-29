using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views;

/// <summary>
/// Credential list view with header, sort controls, and list/grid display modes.
/// </summary>
public partial class CredentialListView : UserControl
{
    public CredentialListView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
