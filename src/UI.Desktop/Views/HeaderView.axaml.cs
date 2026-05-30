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

        var addButton = this.FindControl<Button>("AddButton");
        var addPopup = this.FindControl<Popup>("AddPopup");
        if (addButton != null && addPopup != null)
        {
            addButton.Click += (_, _) => addPopup.IsOpen = !addPopup.IsOpen;
        }

        var actionsButton = this.FindControl<Button>("ActionsMenuButton");
        if (actionsButton?.Flyout is Flyout flyout)
        {
            var openCorner = new Avalonia.CornerRadius(14, 14, 0, 0);
            var closedCorner = new Avalonia.CornerRadius(12);
            const int closeAnimMs = 520;
            var openOpacityMs = TimeSpan.FromMilliseconds(360);
            var openTransformMs = TimeSpan.FromMilliseconds(360);
            var closeAnimDuration = TimeSpan.FromMilliseconds(closeAnimMs);
            var allowClose = false;

            flyout.Opened += (_, _) =>
            {
                allowClose = false;
                actionsButton.Classes.Add("menu-open");
                actionsButton.CornerRadius = openCorner;
                var root = (actionsButton.Flyout as Flyout)?.Content as Control;
                if (root == null) return;

                root.RenderTransformOrigin = new Avalonia.RelativePoint(1, 0, Avalonia.RelativeUnit.Relative);
                root.Opacity = 0;
                root.RenderTransform = TransformOperations.Parse("scaleY(0.001)");
                Dispatcher.UIThread.Post(() =>
                {
                    root.Opacity = 1;
                    root.RenderTransform = TransformOperations.Parse("scaleY(1)");
                }, DispatcherPriority.Render);
            };

            flyout.Closing += (_, e) =>
            {
                if (allowClose) return;
                e.Cancel = true;

                // Start button corner-radius animation back to rounded in sync with dropdown retract
                actionsButton.Classes.Remove("menu-open");
                actionsButton.CornerRadius = closedCorner;

                var root = (actionsButton.Flyout as Flyout)?.Content as Control;
                if (root != null && root.Transitions != null)
                {
                    // Slow the transitions for the close animation
                    foreach (var t in root.Transitions)
                    {
                        if (t is DoubleTransition dt && dt.Property == Avalonia.Visual.OpacityProperty)
                            dt.Duration = closeAnimDuration;
                        else if (t is TransformOperationsTransition tt && tt.Property == Avalonia.Visual.RenderTransformProperty)
                            tt.Duration = closeAnimDuration;
                    }

                    root.Opacity = 0;
                    root.RenderTransform = TransformOperations.Parse("scaleY(0.001)");
                }

                DispatcherTimer.RunOnce(() =>
                {
                    // Restore original durations for the next open
                    if (root != null && root.Transitions != null)
                    {
                        foreach (var t in root.Transitions)
                        {
                            if (t is DoubleTransition dt && dt.Property == Avalonia.Visual.OpacityProperty)
                                dt.Duration = openOpacityMs;
                            else if (t is TransformOperationsTransition tt && tt.Property == Avalonia.Visual.RenderTransformProperty)
                                tt.Duration = openTransformMs;
                        }
                    }
                    allowClose = true;
                    flyout.Hide();
                }, closeAnimDuration);
            };

            flyout.Closed += (_, _) =>
            {
                actionsButton.Classes.Remove("menu-open");
                actionsButton.CornerRadius = closedCorner;
                allowClose = false;
            };
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

