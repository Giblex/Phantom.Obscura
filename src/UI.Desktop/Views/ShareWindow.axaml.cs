using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views
{

    public partial class ShareWindow : ThemeAwareWindow
    {
        public ShareWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}

