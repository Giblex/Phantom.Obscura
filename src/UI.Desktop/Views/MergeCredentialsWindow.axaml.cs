using Avalonia.Controls;
using Avalonia.Input;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class MergeCredentialsWindow : ThemeAwareWindow
    {
        public MergeCredentialsWindow()
        {
            InitializeComponent();
        }

        private void OnCredentialCardPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.DataContext is MergeItemViewModel item)
            {
                var tag = border.Tag?.ToString();
                if (tag == "new")
                {
                    item.SelectNew();
                }
                else if (tag == "existing")
                {
                    item.SelectExisting();
                }
            }
        }
    }
}
