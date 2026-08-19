using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views.Dialogs
{
    public partial class InternetConsentWindow : ThemeAwareWindow
    {
        public bool Allowed { get; private set; }

        public InternetConsentWindow()
        {
            InitializeComponent();
        }

        public InternetConsentWindow(IReadOnlyList<string> hosts, TimeSpan duration) : this()
        {
            var hostNames = hosts.Where(host => !string.IsNullOrWhiteSpace(host)).ToArray();
            var hostsText = this.FindControl<TextBlock>("HostsText");
            var durationText = this.FindControl<TextBlock>("DurationText");

            if (hostsText is not null)
                hostsText.Text = hostNames.Length == 0
                    ? "• Connect only to approved licensing services"
                    : $"• Connect only to {string.Join(", ", hostNames)}";

            if (durationText is not null)
            {
                int minutes = Math.Max(1, (int)Math.Ceiling(duration.TotalMinutes));
                durationText.Text = $"• Expire automatically after {minutes} minute{(minutes == 1 ? string.Empty : "s")}";
            }
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void OnAllow(object? sender, RoutedEventArgs e)
        {
            Allowed = true;
            Close();
        }

        private void OnDeny(object? sender, RoutedEventArgs e) => Close();
    }
}
