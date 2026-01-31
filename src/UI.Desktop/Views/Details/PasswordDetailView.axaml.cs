using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views.Details;

public partial class PasswordDetailView : UserControl
{
    public PasswordDetailView()
    {
        AvaloniaXamlLoader.Load(this);
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
