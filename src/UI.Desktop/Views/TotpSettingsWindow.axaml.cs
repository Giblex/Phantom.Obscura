using Avalonia.Controls;
using Avalonia.Interactivity;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class TotpSettingsWindow : ThemeAwareWindow
    {
        public TotpSettingsWindow()
        {
            InitializeComponent();
            
            if (DataContext is TotpSettingsViewModel vm)
            {
                vm.SetOwnerWindow(this);
            }
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            
            if (DataContext is TotpSettingsViewModel vm)
            {
                vm.SetOwnerWindow(this);
            }
        }
        
        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
