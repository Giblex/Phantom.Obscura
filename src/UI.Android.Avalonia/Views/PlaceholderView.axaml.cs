using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views;

public partial class PlaceholderView : UserControl
{
    public PlaceholderView() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Convenience factory used by the navigator.</summary>
    public static PlaceholderView Create(string title, string sourceView, string description, string portStatus)
        => new()
        {
            DataContext = new PlaceholderViewModel
            {
                Title = title,
                SourceViewName = sourceView,
                Description = description,
                PortStatus = portStatus,
            }
        };
}
