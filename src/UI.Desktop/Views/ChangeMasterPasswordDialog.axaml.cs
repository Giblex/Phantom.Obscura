using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views
{
    /// <summary>
    /// Collects the (optional) new master password for a vault rekey. The
    /// keyfile is mandatory and rotated by the rekey service; the password is
    /// optional, so an empty new password is valid (vault protected by keyfile
    /// alone). Verification of the current password is handled by the caller.
    /// </summary>
    public partial class ChangeMasterPasswordDialog : ThemeAwareWindow
    {
        /// <summary>True when the user confirmed the change.</summary>
        public bool Confirmed { get; private set; }

        /// <summary>The current password the user entered (empty if no current password).</summary>
        public string CurrentPassword => CurrentBox.Text ?? string.Empty;

        /// <summary>The new password (may be empty — keyfile-only protection).</summary>
        public string NewPassword => NewBox.Text ?? string.Empty;

        private readonly bool _hasCurrentPassword;

        // Parameterless ctor for the XAML designer / loader.
        public ChangeMasterPasswordDialog() : this(false) { }

        public ChangeMasterPasswordDialog(bool hasCurrentPassword)
        {
            _hasCurrentPassword = hasCurrentPassword;
            InitializeComponent();
            // Hide the current-password field when the vault has no password set.
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
