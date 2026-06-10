using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.Models;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{

    public partial class CommandPaletteWindow : ThemeAwareWindow
    {
        private CommandPaletteAction? _pendingActionToRun;

        public CommandPaletteWindow()
        {
            InitializeComponent();
        }

        public CommandPaletteWindow(CommandPaletteViewModel vm) : this()
        {
            DataContext = vm ?? throw new ArgumentNullException(nameof(vm));
            vm.ActivateRequested += OnActivateRequested;
        }

        protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);

            var search = this.FindControl<TextBox>("SearchBox");
            search?.Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {

            if (DataContext is not CommandPaletteViewModel vm)
            {
                base.OnKeyDown(e);
                return;
            }

            switch (e.Key)
            {
                case Key.Escape:
                    e.Handled = true;
                    Close();
                    return;
                case Key.Down:
                    e.Handled = true;
                    vm.MoveSelectionDownCommand.Execute().Subscribe();
                    return;
                case Key.Up:
                    e.Handled = true;
                    vm.MoveSelectionUpCommand.Execute().Subscribe();
                    return;
                case Key.Enter:
                    e.Handled = true;
                    vm.ActivateSelectedCommand.Execute().Subscribe();
                    return;
            }

            base.OnKeyDown(e);
        }

        private void OnActivateRequested(object? sender, CommandPaletteAction action)
        {

            _pendingActionToRun = action;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (DataContext is CommandPaletteViewModel vm)
            {
                vm.ActivateRequested -= OnActivateRequested;
            }

            base.OnClosed(e);

            var action = _pendingActionToRun;
            _pendingActionToRun = null;
            if (action is null) return;

            try
            {
                action.Execute();
            }
            catch (Exception ex)
            {

                System.Diagnostics.Debug.WriteLine($"Command palette action '{action.Title}' threw: {ex.Message}");
            }
        }

        private void OnListDoubleTapped(object? sender, RoutedEventArgs e)
        {
            if (DataContext is CommandPaletteViewModel vm)
            {
                vm.ActivateSelectedCommand.Execute().Subscribe();
            }
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}

