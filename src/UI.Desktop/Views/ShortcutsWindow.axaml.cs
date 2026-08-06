using Avalonia.Controls;

namespace PhantomVault.UI.Views
{
    public partial class ShortcutsWindow : ThemeAwareWindow
    {
        public ShortcutsWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
        }
    }
}

