using System;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ReactiveUI;
using Serilog;
using PhantomVault.Core.Models.Licensing;
using PhantomVault.UI.Services.Entitlements;
using PhantomVault.UI.Services.Licensing;
using PhantomVault.UI.ViewModels;
using PhantomVault.UI.Views.Dialogs;

namespace PhantomVault.UI.ViewModels.Settings
{
    public class SubscriptionSettingsViewModel : ReactiveObject
    {
        private readonly VaultViewModel _host;
        private readonly IEntitlementService? _entitlements;
        private readonly ILicensingClient? _client;

        public SubscriptionSettingsViewModel(VaultViewModel host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _entitlements = host.EntitlementService;

            var services = (Application.Current as App)?.Services;
            _client = services?.GetService(typeof(ILicensingClient)) as ILicensingClient;

            UpgradeCommand = ReactiveCommand.CreateFromTask(UpgradeAsync);
            RefreshCommand = ReactiveCommand.Create(RaiseAll);

            if (_entitlements != null)
                _entitlements.Changed += (_, _) => RaiseAll();
        }

        public ReactiveCommand<Unit, Unit> UpgradeCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

        private LicenseStatus Status => _entitlements?.Status ?? LicenseStatus.Free(LicenseFailureReason.Empty);

        public bool IsPremium => Status.IsValid && Status.Tier == PremiumTier.Premium;

        public string TierName => IsPremium ? "Premium" : "Free";

        public string StatusSummary => IsPremium
            ? (Status.InGracePeriod ? "Premium — renewal needed" : "Premium — active")
            : "Free plan";

        public string ExpiryText
        {
            get
            {
                if (!IsPremium || Status.ExpiresUtc is null) return string.Empty;
                int days = Status.DaysRemaining;
                string when = Status.ExpiresUtc.Value.ToLocalTime().ToString("d MMM yyyy");
                return days >= 0
                    ? $"Renews on {when} ({days} day{(days == 1 ? "" : "s")} remaining)"
                    : $"Expired on {when} — subscribe to restore premium";
            }
        }

        public bool ShowExpiry => IsPremium;

        public string UpgradeButtonText => IsPremium ? "Manage subscription" : "Upgrade to Premium";

        private void RaiseAll()
        {
            this.RaisePropertyChanged(nameof(IsPremium));
            this.RaisePropertyChanged(nameof(TierName));
            this.RaisePropertyChanged(nameof(StatusSummary));
            this.RaisePropertyChanged(nameof(ExpiryText));
            this.RaisePropertyChanged(nameof(ShowExpiry));
            this.RaisePropertyChanged(nameof(UpgradeButtonText));
        }

        private async Task UpgradeAsync()
        {
            Log.Information("UpgradeAsync: invoked. _client is {ClientState}", _client is null ? "null" : _client.GetType().Name);

            if (_client is null)
            {
                Log.Warning("UpgradeAsync: aborting — ILicensingClient did not resolve from DI.");
                _host.StatusMessage = "Licensing is unavailable in this build.";
                return;
            }

            var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            Log.Information("UpgradeAsync: owner window is {OwnerState}", owner is null ? "null" : $"{owner.GetType().Name} (Title='{owner.Title}', IsVisible={owner.IsVisible})");
            if (owner is null)
            {
                _host.StatusMessage = "Checkout cannot open because no desktop window is active.";
                return;
            }

            PayWindow window;
            try
            {
                // Create on UI thread synchronously, then await ShowDialog normally so the
                // dialog stays open until the user dismisses it (InvokeAsync+async lambda
                // does not unwrap the inner Task in Avalonia 11 and closes the dialog early).
                window = Dispatcher.UIThread.Invoke(() => new PayWindow(_client, _host.CurrentUsbBindingId));
                Log.Information("UpgradeAsync: PayWindow constructed, calling ShowDialog.");
                await window.ShowDialog(owner);
                Log.Information("UpgradeAsync: ShowDialog returned. ResultToken is {TokenState}", string.IsNullOrWhiteSpace(window.ResultToken) ? "empty" : "set");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Premium checkout window failed to open.");
                _host.StatusMessage = "Checkout could not be opened. Please try again.";
                return;
            }

            if (!string.IsNullOrWhiteSpace(window.ResultToken))
            {
                try
                {
                    await _host.PersistLicenseTokenAsync(window.ResultToken);
                    RaiseAll();
                    _host.StatusMessage = "Premium activated successfully.";
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to persist premium token after checkout.");
                    _host.StatusMessage = "Payment succeeded, but saving the license failed. Please retry.";
                }
            }
            else
            {
                _host.StatusMessage = "Checkout was not completed.";
            }
        }
    }
}
