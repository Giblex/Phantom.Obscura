using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views
{
    /// <summary>
    /// Interaction logic for PasswordHealthWindow.axaml. This code-behind
    /// exists solely to call InitializeComponent; all logic should
    /// reside in the associated view model.
    /// </summary>
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