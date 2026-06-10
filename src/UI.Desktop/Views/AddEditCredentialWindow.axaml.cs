using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class AddEditCredentialWindow : ThemeAwareWindow
    {
        public AddEditCredentialWindow()
        {
            InitializeComponent();

            this.Opened += OnWindowOpened;
        }

        private async void OnWindowOpened(object? sender, EventArgs e)
        {

            if (this.Owner is Window owner)
            {

                var ownerBounds = owner.Bounds;

                var dialogWidth = 520.0;
                var dialogHeight = 600.0;
                this.Width = dialogWidth;
                this.Height = dialogHeight;

                this.Position = new Avalonia.PixelPoint(
                    (int)(ownerBounds.Right),
                    (int)(ownerBounds.Top + (ownerBounds.Height - dialogHeight) / 2)
                );

                var targetX = (int)(ownerBounds.Right - dialogWidth);
                var targetY = (int)(ownerBounds.Top + (ownerBounds.Height - dialogHeight) / 2);

                var startX = this.Position.X;
                var duration = 400;
                var frames = 40;
                var delay = duration / frames;

                for (int i = 0; i <= frames; i++)
                {
                    var progress = (double)i / frames;

                    var eased = 1 - Math.Pow(1 - progress, 3);
                    var currentX = (int)(startX + (targetX - startX) * eased);

                    this.Position = new Avalonia.PixelPoint(currentX, targetY);

                    await System.Threading.Tasks.Task.Delay(delay);
                }
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            if (DataContext is AddEditCredentialViewModel viewModel)
            {
                viewModel.SetOwnerWindow(this);
            }

            this.DataContextChanged += (s, e) =>
            {
                if (DataContext is AddEditCredentialViewModel vm)
                {
                    vm.SetOwnerWindow(this);
                }
            };
        }

        private void OnIconFlyoutButtonClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
            {
                return;
            }

            var flyout = FlyoutBase.GetAttachedFlyout(button);
            flyout?.ShowAt(button);
        }
    }
}

