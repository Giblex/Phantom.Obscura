using Avalonia.Input;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    /// <summary>
    /// Settings window for vault configuration.
    /// </summary>
    public partial class VaultSettingsWindow : ThemeAwareWindow
    {
        public VaultSettingsWindow()
        {
            InitializeComponent();

            this.DataContextChanged += (s, e) =>
            {
                if (DataContext is VaultSettingsViewModel vm)
                {
                    vm.SetOwnerWindow(this);
                }
            };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnCategoryManagerOverlayBackgroundPressed(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is VaultSettingsViewModel vm && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                vm.DismissCategoryManagerPanel();
                e.Handled = true;
            }
        }
    }
}
