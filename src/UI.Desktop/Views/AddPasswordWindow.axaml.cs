using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PhantomVault.UI.ViewModels;
using System.Threading.Tasks;
using Avalonia;
using PhantomVault.UI.Views;

namespace PhantomVault.UI.Views
{
    public partial class AddPasswordWindow : ThemeAwareWindow
    {
        public AddPasswordWindow()
        {
            InitializeComponent();
            this.DataContextChanged += (s, e) =>
            {
                if (DataContext is AddPasswordViewModel vm && this is Window w)
                {
                    // No-op: owner coupling not needed here but kept for parity
                }
            };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public async void OpenPasswordGenerator_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            // Create viewmodel and window
            var viewModel = new PasswordGeneratorViewModel();
            var window = new PasswordGeneratorWindow
            {
                DataContext = viewModel
            };

            // Set owner to this window
            viewModel.SetOwnerWindow(window);

            // Show as dialog — when it closes, if the user accepted, copy password
            await window.ShowDialog(this);

            if (viewModel.Accepted && !string.IsNullOrEmpty(viewModel.GeneratedPassword) && !viewModel.GeneratedPassword.StartsWith("Please select"))
            {
                if (DataContext is AddPasswordViewModel vm)
                {
                    vm.Password = viewModel.GeneratedPassword;
                }
            }
        }
    }
}