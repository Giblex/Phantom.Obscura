using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views
{
    /// <summary>
    /// Interaction logic for ShareWindow.axaml. All business logic
    /// resides in the associated <see cref="PhantomVault.UI.ViewModels.ShareViewModel"/>.
    /// </summary>
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