using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views
{

    public partial class ChangeMasterPasswordDialog : ThemeAwareWindow
    {

        public bool Confirmed { get; private set; }

        public string CurrentPassword => CurrentBox.Text ?? string.Empty;

        public string NewPassword => NewBox.Text ?? string.Empty;

        private readonly bool _hasCurrentPassword;

        public ChangeMasterPasswordDialog() : this(false) { }

        public ChangeMasterPasswordDialog(bool hasCurrentPassword)
        {
            _hasCurrentPassword = hasCurrentPassword;
            InitializeComponent();

            CurrentPanel.IsVisible = hasCurrentPassword;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.IsVisible = true;
        }

        private void Change_Click(object? sender, RoutedEventArgs e)
        {
            if (_hasCurrentPassword && string.IsNullOrEmpty(CurrentPassword))
            {
                ShowError("Enter your current password.");
                return;
            }

            if ((NewBox.Text ?? string.Empty) != (ConfirmBox.Text ?? string.Empty))
            {
                ShowError("New password and confirmation do not match.");
                return;
            }

            Confirmed = true;
            Close();
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }
    }
}

