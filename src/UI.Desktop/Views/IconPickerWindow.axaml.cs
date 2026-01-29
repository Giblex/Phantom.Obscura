using Avalonia.Controls;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace PhantomVault.UI.Views
{
    public partial class IconPickerWindow : ThemeAwareWindow
    {
        public IconPickerWindow()
        {
            InitializeComponent();
            this.Opened += (_, __) =>
            {
                var tb = this.FindControl<TextBox>("SearchBox");
                tb?.Focus();
            };
        }
    }
}
