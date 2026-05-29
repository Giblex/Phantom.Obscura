using System;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Giblex.Controls;

namespace PhantomVault.UI.Views;

/// <summary>
/// Header/toolbar view with logo, search box, view toggle, and action buttons.
/// </summary>
public partial class HeaderView : UserControl
{
    public event EventHandler<RoutedEventArgs>? PasswordGeneratorRequested;
    public event EventHandler<RoutedEventArgs>? SettingsRequested;

    public HeaderView()
    {
        AvaloniaXamlLoader.Load(this);
        
        var passwordGeneratorButton = this.FindControl<Button>("PasswordGeneratorButton");
        if (passwordGeneratorButton != null)
        {
            passwordGeneratorButton.Click += OnPasswordGeneratorClick;
        }
        
        var settingsButton = this.FindControl<Button>("SettingsButton");
        if (settingsButton != null)
        {
            settingsButton.Click += OnSettingsClick;
        }

        // Wire up Add button to toggle popup
        var addButton = this.FindControl<Button>("AddButton");
        var addPopup = this.FindControl<Popup>("AddPopup");
        if (addButton != null && addPopup != null)
        {
            addButton.Click += (_, _) => addPopup.IsOpen = !addPopup.IsOpen;
        }

        // Actions menu: toggle "menu-open" morph class on the button + animate
        // the dropdown inner Border (slide down + fade in) when the flyout opens.
        var actionsButton = this.FindControl<Button>("ActionsMenuButton");
        if (actionsButton?.Flyout is FlyoutBase flyout)
        {
            flyout.Opened += (_, _) =>
            {
                actionsButton.Classes.Add("menu-open");
                var root = (actionsButton.Flyout as Flyout)?.Content as Border;
                if (root == null) return;
                // Reset to start state, then animate to rest on next tick.
                root.Opacity = 0;
                root.RenderTransform = TransformOperations.Parse("translateY(-14px)");
                Dispatcher.UIThread.Post(() =>
                {
                    root.Opacity = 1;
                    root.RenderTransform = TransformOperations.Parse("translateY(0)");
                }, DispatcherPriority.Render);
            };
            flyout.Closed += (_, _) => actionsButton.Classes.Remove("menu-open");
        }
    }

    private void OnPasswordGeneratorClick(object? sender, RoutedEventArgs e)
    {
        PasswordGeneratorRequested?.Invoke(this, e);
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        SettingsRequested?.Invoke(this, e);
    }

    private void CloseAddPopup(object? sender, RoutedEventArgs e)
    {
        var addPopup = this.FindControl<Popup>("AddPopup");
        if (addPopup != null)
        {
            addPopup.IsOpen = false;
        }
    }
}
