using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views.Overlays
{
    public partial class FlaggedPasswordsOverlay : UserControl
    {
        public FlaggedPasswordsOverlay()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
