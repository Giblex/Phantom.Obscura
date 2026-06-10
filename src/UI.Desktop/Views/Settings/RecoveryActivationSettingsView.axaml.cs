using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views.Settings
{
    public partial class RecoveryActivationSettingsView : UserControl
    {
        public RecoveryActivationSettingsView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
