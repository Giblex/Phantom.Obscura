using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views
{
    public partial class AboutWindow : ThemeAwareWindow
    {
        public string AppVersion { get; }

        public AboutWindow()
        {
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            AppVersion = info?.InformationalVersion?.Split('+')[0] ?? "5.0.0";

            DataContext = this;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
