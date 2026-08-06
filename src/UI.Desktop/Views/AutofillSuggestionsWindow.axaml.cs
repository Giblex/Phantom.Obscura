using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views;

public partial class AutofillSuggestionsWindow : ThemeAwareWindow
{
    public AutofillSuggestionsWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

