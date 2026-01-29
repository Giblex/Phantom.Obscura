using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class SignInDialog : ThemeAwareWindow
    {
        public SignInDialog()
        {
            InitializeComponent();
            DataContext = new SignInDialogViewModel();
        }

        public SignInDialog(PhantomVault.Core.Services.ManifestService manifestService, PhantomVault.Core.Services.VaultService vaultService)
        {
            InitializeComponent();
            var zk = new PhantomVault.Core.Services.ZeroKnowledge.ZkVaultService();
            DataContext = new SignInDialogViewModel(manifestService, vaultService, zk);
            if (DataContext is SignInDialogViewModel vm) vm.SetOwnerWindow(this);
        }

        public SignInDialog(PhantomVault.Core.Services.ManifestService manifestService, PhantomVault.Core.Services.VaultService vaultService, PhantomVault.Core.Services.ZeroKnowledge.IZkVaultService zkVaultService)
        {
            InitializeComponent();
            DataContext = new SignInDialogViewModel(manifestService, vaultService, zkVaultService);
            if (DataContext is SignInDialogViewModel vm) vm.SetOwnerWindow(this);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private async void OnPickManifestClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (!(DataContext is SignInDialogViewModel vm)) return;

            var input = new Window
            {
                Title = "Enter manifest path",
                Width = 640,
                Height = 140,
                WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            };

            var tb = new TextBox { Text = vm.ManifestPath ?? string.Empty, Margin = new Thickness(8) };
            var ok = new Button { Width = 100 };
            ok.Content = new TextBlock { Text = "OK" };
            var cancel = new Button { Width = 100 };
            cancel.Content = new TextBlock { Text = "Cancel" };

            var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);

            var panel = new StackPanel { Spacing = 8, Margin = new Thickness(8) };
            panel.Children.Add(new TextBlock { Text = "Enter full path to vault.manifest (or paste path from USB):" });
            panel.Children.Add(tb);
            panel.Children.Add(buttons);

            input.Content = panel;

            ok.Click += (_, __) => input.Close(tb.Text);
            cancel.Click += (_, __) => input.Close(null);

            var result = await input.ShowDialog<string?>(this);
            if (!string.IsNullOrEmpty(result)) vm.ManifestPath = result;
        }

        private async void OnPickKeyfileClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (!(DataContext is SignInDialogViewModel vm)) return;

            var input = new Window
            {
                Title = "Enter keyfile path",
                Width = 640,
                Height = 140,
                WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            };

            var tb = new TextBox { Text = vm.KeyfilePath ?? string.Empty, Margin = new Thickness(8) };
            var ok = new Button { Width = 100 };
            ok.Content = new TextBlock { Text = "OK" };
            var cancel = new Button { Width = 100 };
            cancel.Content = new TextBlock { Text = "Cancel" };

            var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);

            var panel = new StackPanel { Spacing = 8, Margin = new Thickness(8) };
            panel.Children.Add(new TextBlock { Text = "Enter full path to keyfile (or paste path from USB):" });
            panel.Children.Add(tb);
            panel.Children.Add(buttons);

            input.Content = panel;

            ok.Click += (_, __) => input.Close(tb.Text);
            cancel.Click += (_, __) => input.Close(null);

            var result = await input.ShowDialog<string?>(this);
            if (!string.IsNullOrEmpty(result)) vm.KeyfilePath = result;
        }
    }
}
