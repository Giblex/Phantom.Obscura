using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class IconManagerView : UserControl
    {
        private Popup? _variantPopup;
        /// <summary>Tracks the last Button clicked whose DataContext is an IconFileEntryViewModel.</summary>
        private Button? _lastClickedIconButton;

        public IconManagerView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            _variantPopup = this.FindControl<Popup>("VariantPopup");

            // Capture every Button.Click that bubbles through the control
            AddHandler(Button.ClickEvent, OnAnyButtonClick, RoutingStrategies.Bubble, handledEventsToo: true);

            // Hook up DataContext changes
            this.DataContextChanged += OnDataContextChanged;
        }

        /// <summary>Captures every button click so we can reliably anchor the popup.</summary>
        private void OnAnyButtonClick(object? sender, RoutedEventArgs e)
        {
            if (e.Source is Button btn && btn.DataContext is IconFileEntryViewModel)
            {
                _lastClickedIconButton = btn;
            }
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (DataContext is IconManagerViewModel vm)
            {
                if (!vm.HasOwnerWindow)
                {
                    var window = this.FindAncestorOfType<Window>();
                    if (window != null)
                    {
                        vm.SetOwnerWindow(window);
                    }
                }

                vm.PropertyChanged -= Vm_PropertyChanged;
                vm.PropertyChanged += Vm_PropertyChanged;
            }
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not IconManagerViewModel vm) return;
            if (e.PropertyName != nameof(IconManagerViewModel.IsVariantPopupOpen)) return;
            if (_variantPopup == null) return;

            try
            {
                if (vm.IsVariantPopupOpen && vm.VariantOwnerIcon != null)
                {
                    // Use the button we captured from the Click event
                    if (_lastClickedIconButton != null && _lastClickedIconButton.DataContext == vm.VariantOwnerIcon)
                    {
                        _variantPopup.PlacementTarget = _lastClickedIconButton;
                        _variantPopup.Placement = PlacementMode.Bottom;
                    }
                    else
                    {
                        // Fallback: search visual tree
                        var found = FindButtonByDataContext(this, vm.VariantOwnerIcon);
                        if (found != null)
                        {
                            _variantPopup.PlacementTarget = found;
                            _variantPopup.Placement = PlacementMode.Bottom;
                        }
                    }

                    _variantPopup.IsOpen = true;
                }
                else
                {
                    _variantPopup.IsOpen = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IconManagerView] Popup error: {ex.Message}");
            }
        }

        private static Control? FindButtonByDataContext(Visual root, object target)
        {
            foreach (var child in root.GetVisualChildren())
            {
                if (child is Button btn && btn.DataContext == target)
                    return btn;
                if (child is Visual v)
                {
                    var found = FindButtonByDataContext(v, target);
                    if (found != null) return found;
                }
            }
            return null;
        }
    }
}
