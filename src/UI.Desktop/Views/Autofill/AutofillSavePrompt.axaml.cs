using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using PhantomVault.Core.Services.AutoInject;
using PhantomVault.Core.Services.Security;
using PhantomVault.UI.Views;

namespace PhantomVault.UI.Views.Autofill
{
    /// <summary>
    /// Offered after a login form is submitted with credentials the vault does not
    /// already hold, or holds with a different password.
    ///
    /// Doubles as the in-place generator: the password is editable and Generate
    /// replaces it with a strong one, so changing a password on a site and saving the
    /// new value is a single flow rather than a trip through the vault UI.
    /// </summary>
    public partial class AutofillSavePrompt : ThemeAwareWindow
    {
        public enum PromptResult { Dismissed, Saved, NeverForSite }

        /// <summary>Fires once, with the user's decision and the final field values.</summary>
        public event EventHandler<SavePromptResultEventArgs>? Completed;

        public sealed class SavePromptResultEventArgs : EventArgs
        {
            public PromptResult Result { get; init; }
            public string Domain { get; init; } = string.Empty;
            public string Username { get; init; } = string.Empty;
            public string Password { get; init; } = string.Empty;
            public string? ExistingCredentialId { get; init; }
            public bool IsUpdate { get; init; }
        }

        private readonly SavePromptDecision _decision;
        private TextBox? _usernameBox;
        private TextBox? _passwordBox;
        private TextBlock? _hint;
        private Border? _shell;
        private bool _revealed;
        private bool _completed;

        public AutofillSavePrompt(SavePromptDecision decision)
        {
            AvaloniaXamlLoader.Load(this);
            _decision = decision ?? throw new ArgumentNullException(nameof(decision));

            _usernameBox = this.FindControl<TextBox>("UsernameBox");
            _passwordBox = this.FindControl<TextBox>("PasswordBox");
            _hint = this.FindControl<TextBlock>("HintText");
            _shell = this.FindControl<Border>("Shell");

            bool isUpdate = decision.Kind == SavePromptKind.UpdateExisting;

            var title = this.FindControl<TextBlock>("TitleText");
            if (title != null)
                title.Text = isUpdate ? "Update saved password?" : "Save this password?";

            var domain = this.FindControl<TextBlock>("DomainText");
            if (domain != null) domain.Text = decision.Domain;

            if (_usernameBox != null) _usernameBox.Text = decision.Username;
            if (_passwordBox != null) _passwordBox.Text = decision.Password;

            var save = this.FindControl<Button>("SaveButton");
            if (save != null) save.Content = isUpdate ? "Update" : "Save";

            SetHint(isUpdate
                ? "The stored password for this account is different."
                : "This account is not in your vault yet.");

            Wire("RevealButton", ToggleReveal);
            Wire("GenerateButton", GenerateInPlace);
            Wire("SaveButton", () => Complete(PromptResult.Saved));
            Wire("DismissButton", () => Complete(PromptResult.Dismissed));
            Wire("NeverButton", () => Complete(PromptResult.NeverForSite));

            Opened += (_, _) => PlayEntrance();

            // Unlike the suggestion menu, this prompt does NOT close on deactivate:
            // the user has just submitted a form, so focus is legitimately elsewhere
            // and auto-dismissing would throw away the only chance to save.
        }

        private void Wire(string name, Action handler)
        {
            var btn = this.FindControl<Button>(name);
            if (btn != null) btn.Click += (_, _) => handler();
        }

        private void PlayEntrance()
        {
            if (_shell == null) return;
            _shell.Opacity = 0;
            _shell.RenderTransform = TransformOperations.Parse("translateY(-10px) scale(0.97)");

            // Next frame, so the start state commits before the target is set.
            Dispatcher.UIThread.Post(() =>
            {
                if (_shell == null) return;
                _shell.Opacity = 1;
                _shell.RenderTransform = TransformOperations.Parse("translateY(0px) scale(1)");
            }, DispatcherPriority.Background);
        }

        private void ToggleReveal()
        {
            if (_passwordBox == null) return;
            _revealed = !_revealed;
            _passwordBox.PasswordChar = _revealed ? '\0' : '●';
        }

        /// <summary>
        /// In-place generation. Reveals the result automatically — a freshly generated
        /// password the user cannot see is not something they can sanity-check before
        /// committing to it.
        /// </summary>
        private void GenerateInPlace()
        {
            if (_passwordBox == null) return;
            _passwordBox.Text = PasswordGenerator.Generate(new PasswordGenerationOptions { Length = 20 });
            _revealed = true;
            _passwordBox.PasswordChar = '\0';
            SetHint("Generated — this replaces the password you just submitted, so change it on the site too.");
        }

        private void SetHint(string text)
        {
            if (_hint != null) _hint.Text = text;
        }

        private void Complete(PromptResult result)
        {
            if (_completed) return;
            _completed = true;

            Completed?.Invoke(this, new SavePromptResultEventArgs
            {
                Result = result,
                Domain = _decision.Domain,
                Username = _usernameBox?.Text ?? _decision.Username,
                Password = _passwordBox?.Text ?? _decision.Password,
                ExistingCredentialId = _decision.ExistingCredentialId,
                IsUpdate = _decision.Kind == SavePromptKind.UpdateExisting
            });

            try { Close(); } catch (InvalidOperationException) { }
        }

        /// <summary>Bottom-right of the work area, the conventional spot for this prompt.</summary>
        public void PositionBottomRight()
        {
            var screen = Screens.Primary;
            if (screen == null) return;

            var wa = screen.WorkingArea;
            double scale = screen.Scaling;
            int w = (int)Math.Round(Width * scale);
            int h = (int)Math.Round((double.IsNaN(Height) || Height <= 0 ? 320 : Height) * scale);

            Position = new PixelPoint(
                wa.X + wa.Width - w - (int)(16 * scale),
                wa.Y + wa.Height - h - (int)(16 * scale));
        }
    }
}
