using System;
using System.Diagnostics;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views;

public partial class SidebarView : UserControl
{
    public SidebarView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // View-only fallback: the Recovery buttons fire the VM command via code-behind so that any
    // Avalonia Command/CanExecute drop-out can't silently swallow user clicks. No business logic here.
    private void OnToggleRecoveryPanelClick(object? sender, RoutedEventArgs e)
        => InvokeVaultCommand(sender, "ToggleRecoveryPanel", vm => vm.ToggleRecoveryPanelCommand);

    private void OnOpenPhantomRecoveryClick(object? sender, RoutedEventArgs e)
        => InvokeVaultCommand(sender, "OpenPhantomRecovery", vm => vm.OpenPhantomRecoveryCommand);

    private static void InvokeVaultCommand(object? sender, string label, Func<VaultViewModel, ICommand?> selector)
    {
        var button = sender as Button;
        var dataContext = button?.DataContext;
        Debug.WriteLine($"[Sidebar] {label} click. DataContext={dataContext?.GetType().FullName ?? "<null>"} IsEnabled={button?.IsEnabled}");

        if (dataContext is not VaultViewModel vm)
        {
            Debug.WriteLine($"[Sidebar] {label}: DataContext is not VaultViewModel; aborting.");
            return;
        }

        var command = selector(vm);
        if (command is null)
        {
            Debug.WriteLine($"[Sidebar] {label}: VaultViewModel command property was null.");
            return;
        }

        try
        {
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }
            else
            {
                Debug.WriteLine($"[Sidebar] {label}: CanExecute returned false.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Sidebar] {label}: command invocation threw: {ex}");
        }
    }
}

