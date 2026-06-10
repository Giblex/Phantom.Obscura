using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views
{

    public partial class PasswordHealthWindow : ThemeAwareWindow
    {
        public PasswordHealthWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}

