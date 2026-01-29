using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views.Overlays
{
    public partial class CategoryManagerOverlay : UserControl
    {
        public CategoryManagerOverlay()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnBackgroundClicked(object? sender, PointerPressedEventArgs e)
        {
            // Dismiss the category manager panel when clicking outside
            if (DataContext is VaultViewModel vm)
            {
                vm.DismissCategoryManagerPanel();
            }
        }
    }
}
