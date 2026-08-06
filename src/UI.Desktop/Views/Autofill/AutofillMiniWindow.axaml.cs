using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels.Autofill;
using PhantomVault.UI.Views;

namespace PhantomVault.UI.Views.Autofill
{

    public partial class AutofillMiniWindow : ThemeAwareWindow
    {
        public AutofillMiniWindow()
        {
            InitializeComponent();

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

        public void PositionNearField(double x, double y)
        {

            var screen = Screens.Primary;
            if (screen != null)
            {
                var workingArea = screen.WorkingArea;

                if (x + Width > workingArea.Width)
                {
                    x = workingArea.Width - Width - 10;
                }

                if (y + Height > workingArea.Height)
                {
                    y = workingArea.Height - Height - 10;
                }

                if (x < 0) x = 10;

                if (y < 0) y = 10;
            }

            Position = new PixelPoint((int)x, (int)y);
        }
    }
}

