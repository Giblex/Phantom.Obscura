using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views.Details;

public partial class BankAccountDetailView : UserControl
{
    public BankAccountDetailView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
