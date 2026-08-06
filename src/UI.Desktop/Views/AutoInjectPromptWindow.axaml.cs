using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PhantomVault.Core.Models.AutoInject;

namespace PhantomVault.UI.Views
{
    public partial class AutoInjectPromptWindow : ThemeAwareWindow
    {
        private CredentialMatch[]? _matches;
        private CredentialMatch? _selectedCredential;

        public CredentialMatch? SelectedCredential => _selectedCredential;
        public AutoInjectPromptResult Result { get; private set; }

        public AutoInjectPromptWindow()
        {
            InitializeComponent();
            AttachEventHandlers();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public void SetCredentials(CredentialMatch[] matches, AutoInjectContext context)
        {
            var topMatches = matches?.Take(3).ToArray() ?? Array.Empty<CredentialMatch>();
            _matches = topMatches;

            var contextText = this.FindControl<TextBlock>("ContextText");
            if (contextText != null)
            {
                if (!string.IsNullOrEmpty(context?.Domain))
                {
                    contextText.Text = $"Found credentials for {context.Domain}:";
                }
                else if (!string.IsNullOrEmpty(context?.WindowTitle))
                {
                    contextText.Text = $"Found credentials for {context.WindowTitle}:";
                }
                else
                {
                    contextText.Text = "Found credentials:";
                }

                if ((matches?.Length ?? 0) > topMatches.Length)
                {
                    contextText.Text += $" Showing top {topMatches.Length} matches.";
                }
            }

            var listBox = this.FindControl<ItemsControl>("CredentialsListBox");
            if (listBox != null)
            {
                listBox.ItemsSource = topMatches;

                if (topMatches.Length == 1)
                {
                    _selectedCredential = topMatches[0];
                }
            }
        }

        private void AttachEventHandlers()
        {
            var yesButton = this.FindControl<Button>("YesButton");
            if (yesButton != null)
            {
                yesButton.Click += OnYesClicked;
            }

            var noButton = this.FindControl<Button>("NoButton");
            if (noButton != null)
            {
                noButton.Click += OnNoClicked;
            }

            var moreButton = this.FindControl<Button>("MoreOptionsButton");
            if (moreButton != null)
            {
                moreButton.Click += OnMoreOptionsClicked;
            }

            var listBox = this.FindControl<ItemsControl>("CredentialsListBox");
            if (listBox != null)
            {

                listBox.PointerPressed += OnCredentialSelected;
            }

            this.KeyDown += OnKeyDown;
        }

        private void OnCredentialSelected(object? sender, PointerPressedEventArgs e)
        {
            if (e.Source is Control control)
            {

                var dataContext = control.DataContext;
                if (dataContext is CredentialMatch match)
                {
                    _selectedCredential = match;
                }
            }
        }

        private void OnYesClicked(object? sender, RoutedEventArgs e)
        {

            if (_selectedCredential == null && _matches?.Length > 0)
            {
                _selectedCredential = _matches[0];
            }

            Result = AutoInjectPromptResult.Yes;
            Close();
        }

        private void OnNoClicked(object? sender, RoutedEventArgs e)
        {
            Result = AutoInjectPromptResult.No;
            Close();
        }

        private void OnMoreOptionsClicked(object? sender, RoutedEventArgs e)
        {
            Result = AutoInjectPromptResult.MoreOptions;
            Close();
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    OnYesClicked(null, new RoutedEventArgs());
                    break;
                case Key.Escape:
                    OnNoClicked(null, new RoutedEventArgs());
                    break;
                case Key.D1:
                case Key.NumPad1:
                    SelectCredentialByIndex(0);
                    break;
                case Key.D2:
                case Key.NumPad2:
                    SelectCredentialByIndex(1);
                    break;
                case Key.D3:
                case Key.NumPad3:
                    SelectCredentialByIndex(2);
                    break;
            }
        }

        private void SelectCredentialByIndex(int index)
        {
            if (_matches != null && index < _matches.Length)
            {
                _selectedCredential = _matches[index];
                OnYesClicked(null, new RoutedEventArgs());
            }
        }
    }

    public enum AutoInjectPromptResult
    {
        Yes,
        No,
        MoreOptions
    }
}

