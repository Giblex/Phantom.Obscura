using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views.EditForms;

public partial class BankAccountEditForm : UserControl
{
    public BankAccountEditForm()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
