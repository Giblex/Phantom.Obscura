using System;
using System.Collections.ObjectModel;
using System.Linq;
using ReactiveUI;
using PhantomVault.Core.Services.Network;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.ViewModels.Settings
{
    /// <summary>
    /// Read-only "privacy dashboard". Surfaces the honest data-handling posture of
    /// Phantom Obscura: where data lives, whether the app is offline, and exactly
    /// which components are permitted to touch the network right now. Everything
    /// here is derived from live state (the internet gateway + PrivacyShield) or
    /// from invariant product facts — nothing is fabricated.
    /// </summary>
    public sealed class PrivacyPostureViewModel : ReactiveObject, IDisposable
    {
        private readonly IInternetGateway? _gateway;
        private bool _disposed;

        public PrivacyPostureViewModel()
        {
            _gateway = (Avalonia.Application.Current as App)?.Services
                ?.GetService(typeof(IInternetGateway)) as IInternetGateway;

            ActiveGrants = new ObservableCollection<NetworkGrantRow>();
            NetworkComponents = new ObservableCollection<NetworkComponentRow>
            {
                new("Software updates", "Consent-gated", "Only after you click Check for updates; signed manifest is verified before anything downloads."),
                new("Breach check (HIBP)", "Consent-gated", "k-anonymity range query — only the first 5 chars of a password hash ever leave the device, and only if you start a check."),
                new("Website icons", "Consent-gated", "Optional favicon fetch; can be disabled entirely. Never sends credential data."),
                new("Browser auto-fill", "Local only", "Talks to the extension over a local pipe (native messaging). Never uses the network."),
            };

            if (_gateway is not null)
            {
                _gateway.GrantChanged += OnGrantChanged;
            }

            Refresh();
        }

        public ObservableCollection<NetworkGrantRow> ActiveGrants { get; }
        public ObservableCollection<NetworkComponentRow> NetworkComponents { get; }

        private bool _isOffline;
        public bool IsOffline
        {
            get => _isOffline;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isOffline, value);
                this.RaisePropertyChanged(nameof(NetworkStateText));
                this.RaisePropertyChanged(nameof(NetworkStateDetail));
            }
        }

        private bool _hasActiveGrant;
        public bool HasActiveGrant
        {
            get => _hasActiveGrant;
            private set
            {
                this.RaiseAndSetIfChanged(ref _hasActiveGrant, value);
                this.RaisePropertyChanged(nameof(NetworkStateText));
                this.RaisePropertyChanged(nameof(NetworkStateDetail));
            }
        }

        public string NetworkStateText => IsOffline
            ? "Offline — all network access blocked"
            : HasActiveGrant
                ? "Online — a temporary access grant is active"
                : "Idle — no network connection is open";

        public string NetworkStateDetail => IsOffline
            ? "Offline mode is on. Existing grants were revoked and new connection requests are refused until you turn it off."
            : HasActiveGrant
                ? "One or more time-limited, host-pinned grants are active. They expire automatically and can be revoked at any time."
                : "Nothing is talking to the network. Outbound access is off by default and requires your explicit, time-limited consent.";

        // Invariant product facts (USB-bound, local-first, zero-knowledge).
        public string StorageSummary =>
            "Everything is stored locally, encrypted, and bound to your USB key. There is no cloud, no account, and no server copy — your vault cannot be unlocked without the USB key present.";

        public string TelemetrySummary =>
            "No analytics or telemetry are collected. Diagnostic logging is off by default and, when enabled, sensitive values are redacted.";

        public bool RedactDiagnostics => PrivacyShield.RedactDiagnostics;
        public bool DebugLoggingEnabled => PrivacyShield.DebugLoggingEnabled;

        private void OnGrantChanged(object? sender, InternetAccessGrantChangedEventArgs e) => Refresh();

        public void Refresh()
        {
            if (_gateway is null)
            {
                IsOffline = false;
                HasActiveGrant = false;
                ActiveGrants.Clear();
            }
            else
            {
                IsOffline = _gateway.OfflineMode;
                HasActiveGrant = _gateway.HasActiveGrant;

                ActiveGrants.Clear();
                foreach (var g in _gateway.ActiveGrants.Where(g => !g.Revoked))
                {
                    ActiveGrants.Add(new NetworkGrantRow(
                        g.Reason,
                        string.Join(", ", g.AllowedHosts),
                        g.ExpiresAtUtc.ToLocalTime().ToString("g"),
                        g.RequestCount));
                }
            }

            this.RaisePropertyChanged(nameof(RedactDiagnostics));
            this.RaisePropertyChanged(nameof(DebugLoggingEnabled));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_gateway is not null)
            {
                _gateway.GrantChanged -= OnGrantChanged;
            }
        }
    }

    public sealed record NetworkGrantRow(string Reason, string Hosts, string Expires, int RequestCount);

    public sealed record NetworkComponentRow(string Name, string Mode, string Detail);
}
