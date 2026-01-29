using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels.Autofill;

namespace PhantomVault.UI.Views.Autofill
{
    /// <summary>
    /// Mini-window for autofill suggestions and password capture.
    /// </summary>
    public partial class AutofillMiniWindow : Window
    {
        public AutofillMiniWindow()
        {
            InitializeComponent();
            
            // Handle keyboard shortcuts
            KeyDown += OnKeyDown;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (DataContext is not AutofillMiniWindowViewModel viewModel)
                return;

            switch (e.Key)
            {
                case Key.Escape:
                    viewModel.CloseWindowCommand.Execute(System.Reactive.Unit.Default);
                    e.Handled = true;
                    break;

                case Key.Enter:
                    if (viewModel.SelectedSuggestion != null)
                    {
                        viewModel.SelectSuggestionCommand.Execute(viewModel.SelectedSuggestion);
                        e.Handled = true;
                    }
                    break;
            }
        }

        /// <summary>
        /// Positions the window near the specified field.
        /// </summary>
        public void PositionNearField(double x, double y)
        {
            // Adjust position to ensure window stays on screen
            var screen = Screens.Primary;
            if (screen != null)
            {
                var workingArea = screen.WorkingArea;
                
                // Ensure window doesn't go off right edge
                if (x + Width > workingArea.Width)
                {
                    x = workingArea.Width - Width - 10;
                }
                
                // Ensure window doesn't go off bottom edge
                if (y + Height > workingArea.Height)
                {
                    y = workingArea.Height - Height - 10;
                }
                
                // Ensure window doesn't go off left edge
                if (x < 0) x = 10;
                
                // Ensure window doesn't go off top edge
                if (y < 0) y = 10;
            }

            Position = new PixelPoint((int)x, (int)y);
        }
    }
}
