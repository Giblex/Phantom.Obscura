using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class IconManagerWindow : ThemeAwareWindow
    {
        // Start loading the next batch this far before the bottom, so it is ready by the time
        // the user gets there.
        private const double LoadAheadPixels = 480;

        public IconManagerWindow()
        {
            InitializeComponent();

            DataContextChanged += (_, _) =>
            {
                if (DataContext is IconManagerViewModel vm && !vm.HasOwnerWindow)
                    vm.SetOwnerWindow(this);
            };

            KeyDown += OnKeyDown;

            Opened += (_, _) => this.FindControl<TextBox>("SearchBox")?.Focus();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// Asks for the next batch near the end of the list. ScrollChanged also fires when the
        /// content grows, so a list still shorter than the viewport keeps filling itself.
        /// </summary>
        private void OnIconScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (sender is not ScrollViewer scroller || DataContext is not IconManagerViewModel vm || !vm.HasMore)
                return;

            if (scroller.Offset.Y + scroller.Viewport.Height >= scroller.Extent.Height - LoadAheadPixels)
                Dispatcher.UIThread.Post(vm.LoadMore, DispatcherPriority.Background);
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || DataContext is not IconManagerViewModel vm) return;

            // Escape closes the colour panel first, then the window.
            if (vm.HasSelection)
                vm.SelectedTile = null;
            else
                Close();

            e.Handled = true;
        }
    }
}
