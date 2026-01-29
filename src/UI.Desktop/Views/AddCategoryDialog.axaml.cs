using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;
using System.Linq;

namespace PhantomVault.UI.Views
{
    public partial class AddCategoryDialog : Window
    {
        private ListBoxItem? _draggedItem;
        private Point _dragStartPoint;
        private const double DragThreshold = 5.0;

        public AddCategoryDialog()
        {
            InitializeComponent();
            AddHandler(DragDrop.DropEvent, Drop);
            AddHandler(DragDrop.DragOverEvent, DragOver);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            var sourceList = this.FindControl<ListBox>("SourceListBox");
            var destList = this.FindControl<ListBox>("DestinationListBox");

            if (sourceList != null)
            {
                sourceList.AddHandler(PointerPressedEvent, OnSourcePointerPressed, handledEventsToo: true);
                sourceList.AddHandler(PointerMovedEvent, OnSourcePointerMoved, handledEventsToo: true);
                sourceList.AddHandler(PointerReleasedEvent, OnPointerReleased, handledEventsToo: true);
            }

            if (destList != null)
            {
                destList.AddHandler(PointerPressedEvent, OnDestPointerPressed, handledEventsToo: true);
                destList.AddHandler(PointerMovedEvent, OnDestPointerMoved, handledEventsToo: true);
                destList.AddHandler(PointerReleasedEvent, OnPointerReleased, handledEventsToo: true);
            }
        }

        private void OnSourcePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not ListBox listBox) return;
            var point = e.GetCurrentPoint(listBox);
            if (!point.Properties.IsLeftButtonPressed) return;

            var item = GetListBoxItemAtPoint(listBox, point.Position);
            if (item != null)
            {
                _draggedItem = item;
                _dragStartPoint = point.Position;
            }
        }

        private void OnDestPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not ListBox listBox) return;
            var point = e.GetCurrentPoint(listBox);
            if (!point.Properties.IsLeftButtonPressed) return;

            var item = GetListBoxItemAtPoint(listBox, point.Position);
            if (item != null)
            {
                _draggedItem = item;
                _dragStartPoint = point.Position;
            }
        }

        private async void OnSourcePointerMoved(object? sender, PointerEventArgs e)
        {
            if (_draggedItem?.DataContext == null) return;
            if (sender is not ListBox listBox) return;

            var point = e.GetCurrentPoint(listBox);
            if (!point.Properties.IsLeftButtonPressed)
            {
                _draggedItem = null;
                return;
            }

            // Check if we've moved far enough to start drag
            var diff = point.Position - _dragStartPoint;
            if (Math.Abs(diff.X) < DragThreshold && Math.Abs(diff.Y) < DragThreshold)
                return;

            if (DataContext is not AddCategoryDialogViewModel vm) return;

#pragma warning disable CS0618 // Type or member is obsolete
            var data = new DataObject();
            data.Set("entry", _draggedItem.DataContext!);
            data.Set("source", "source");

            await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
#pragma warning restore CS0618 // Type or member is obsolete
            _draggedItem = null;
        }

        private async void OnDestPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_draggedItem?.DataContext == null) return;
            if (sender is not ListBox listBox) return;

            var point = e.GetCurrentPoint(listBox);
            if (!point.Properties.IsLeftButtonPressed)
            {
                _draggedItem = null;
                return;
            }

            // Check if we've moved far enough to start drag
            var diff = point.Position - _dragStartPoint;
            if (Math.Abs(diff.X) < DragThreshold && Math.Abs(diff.Y) < DragThreshold)
                return;

            if (DataContext is not AddCategoryDialogViewModel vm) return;

#pragma warning disable CS0618 // Type or member is obsolete
            var data = new DataObject();
            data.Set("entry", _draggedItem.DataContext!);
            data.Set("source", "destination");

            await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
#pragma warning restore CS0618 // Type or member is obsolete
            _draggedItem = null;
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            _draggedItem = null;
        }

        private void DragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = DragDropEffects.Move;
        }

        private void Drop(object? sender, DragEventArgs e)
        {
            if (DataContext is not AddCategoryDialogViewModel vm) return;

#pragma warning disable CS0618 // Type or member is obsolete
            var entry = e.Data.Get("entry");
            var source = e.Data.Get("source") as string;
#pragma warning restore CS0618 // Type or member is obsolete

            if (entry == null || source == null) return;

            var targetControl = e.Source as Control;
            var destList = this.FindControl<ListBox>("DestinationListBox");
            var sourceList = this.FindControl<ListBox>("SourceListBox");

            bool droppedOnDestination = IsWithinVisualTree(targetControl, destList);
            bool droppedOnSource = IsWithinVisualTree(targetControl, sourceList);

            if (source == "source" && droppedOnDestination)
            {
                vm.MoveToDestination(entry);
            }
            else if (source == "destination" && droppedOnSource)
            {
                vm.MoveToSource(entry);
            }
        }

        private ListBoxItem? GetListBoxItemAtPoint(ListBox listBox, Point point)
        {
            var element = listBox.InputHitTest(point) as Control;
            while (element != null && element != listBox)
            {
                if (element is ListBoxItem item)
                    return item;
                element = element.Parent as Control;
            }
            return null;
        }

        private bool IsWithinVisualTree(Control? child, Control? parent)
        {
            if (child == null || parent == null) return false;
            var current = child;
            while (current != null)
            {
                if (current == parent) return true;
                current = current.Parent as Control;
            }
            return false;
        }
    }
}
