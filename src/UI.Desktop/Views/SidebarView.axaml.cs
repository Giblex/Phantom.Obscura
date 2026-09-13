using System;
using System.Diagnostics;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PhantomVault.UI.Services;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views;

public partial class SidebarView : UserControl
{
    public SidebarView()
    {
        AvaloniaXamlLoader.Load(this);
        ApplyColourTintPreference();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        CategoryBlurPreference.Changed += OnCategoryBlurPreferenceChanged;
        ApplyColourTintPreference();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CategoryBlurPreference.Changed -= OnCategoryBlurPreferenceChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnCategoryBlurPreferenceChanged(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(ApplyColourTintPreference);

    // Coloured blur off: the category pills drop their colour wash and keep only the
    // accent bar (see the .no-colour-tint style in SidebarView.axaml).
    private void ApplyColourTintPreference()
        => Classes.Set("no-colour-tint", !CategoryBlurPreference.UseColouredBlur);

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

