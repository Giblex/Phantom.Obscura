using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class IconManagerView : UserControl
    {
        private Popup? _variantPopup;
        private Polygon? _caretTop;
        private Polygon? _caretBottom;

        public IconManagerView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            // Get references to popup and carets
            _variantPopup = this.FindControl<Popup>("VariantPopup");
            _caretTop = this.FindControl<Polygon>("CaretTop");
            _caretBottom = this.FindControl<Polygon>("CaretBottom");

            // Hook up popup placement logic
            if (_variantPopup != null)
            {
                _variantPopup.Opened += OnVariantPopupOpened;
            }

            // Hook up DataContext changes
            this.DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            // If the ViewModel doesn't have an owner window set, try to get one from the visual tree
            if (DataContext is IconManagerViewModel vm && !vm.HasOwnerWindow)
            {
                var window = this.FindAncestorOfType<Window>();
                if (window != null)
                {
                    vm.SetOwnerWindow(window);
                }
            }
        }

        private void OnVariantPopupOpened(object? sender, EventArgs e)
        {
            // Adjust caret visibility based on placement
            if (_variantPopup?.Placement == PlacementMode.Bottom)
            {
                if (_caretTop != null) _caretTop.IsVisible = true;
                if (_caretBottom != null) _caretBottom.IsVisible = false;
            }
            else if (_variantPopup?.Placement == PlacementMode.Top)
            {
                if (_caretTop != null) _caretTop.IsVisible = false;
                if (_caretBottom != null) _caretBottom.IsVisible = true;
            }
        }
    }
}
