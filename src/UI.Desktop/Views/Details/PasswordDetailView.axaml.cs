using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views.Details;

public partial class PasswordDetailView : UserControl
{
    public PasswordDetailView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void TotpCodeTile_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border tile && tile.RenderTransform is ScaleTransform st)
        {
            st.ScaleX = 0.97;
            st.ScaleY = 0.97;
        }
    }

    private void TotpCodeTile_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Border tile && tile.RenderTransform is ScaleTransform st)
        {
            st.ScaleX = 1.0;
            st.ScaleY = 1.0;
        }
    }

    private void CopyUsername_Tapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is VaultViewModel vm && vm.SelectedCredential != null)
        {
            vm.CopyUsernameCommand?.Execute(vm.SelectedCredential);
        }
    }

    private void CopyPassword_Tapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is VaultViewModel vm && vm.SelectedCredential != null)
        {
            vm.CopyPasswordCommand?.Execute(vm.SelectedCredential);
        }
    }

    private void CopyTotpCode_Tapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is VaultViewModel vm && vm.SelectedCredential != null)
        {
            vm.CopyTotpCodeCommand?.Execute(vm.SelectedCredential);
        }
    }
}
