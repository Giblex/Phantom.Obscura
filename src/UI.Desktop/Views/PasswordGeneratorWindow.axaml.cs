using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    /// <summary>
    /// Standalone password generator window.
    /// </summary>
    public partial class PasswordGeneratorWindow : ThemeAwareWindow
    {
        public PasswordGeneratorWindow()
        {
            InitializeComponent();

            this.DataContextChanged += (s, e) =>
            {
                if (DataContext is PasswordGeneratorViewModel vm)
                {
                    vm.SetOwnerWindow(this);
                }
            };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
