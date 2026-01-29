using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views
{
    public partial class ThemeSettingsWindow : ThemeAwareWindow
    {
        public ThemeSettingsWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public void Close_Click(object? sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}