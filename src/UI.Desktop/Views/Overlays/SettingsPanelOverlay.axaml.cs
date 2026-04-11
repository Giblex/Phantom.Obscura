using System.Reactive;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views.Overlays
{
    public partial class SettingsPanelOverlay : UserControl
    {
        public SettingsPanelOverlay()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnBackgroundClicked(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is VaultViewModel vm)
            {
                vm.CloseSettingsPanelCommand.Execute(Unit.Default).Subscribe();
            }
        }
    }
}
