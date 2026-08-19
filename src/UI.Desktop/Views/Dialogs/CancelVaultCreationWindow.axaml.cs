using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views.Dialogs
{
    public partial class CancelVaultCreationWindow : ThemeAwareWindow
    {
        public bool CancellationConfirmed { get; private set; }

        public CancelVaultCreationWindow() => InitializeComponent();

        private void OnContinue(object? sender, RoutedEventArgs e) => Close();

        private void OnConfirmCancel(object? sender, RoutedEventArgs e)
        {
            CancellationConfirmed = true;
            Close();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}
