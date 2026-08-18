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
            RedeemCommand = ReactiveCommand.CreateFromTask(RedeemAsync);

            if (_entitlements != null)
                _entitlements.Changed += (_, _) => RaiseAll();
        }

        public ReactiveCommand<Unit, Unit> UpgradeCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

        /// <summary>
        /// Redeems a licence code pasted by the user.
        ///
        /// The in-app checkout can only auto-activate when the licensing backend returns
        /// a signed token. When checkout happens through a Stripe payment link instead,
        /// the code arrives by email and there was previously no way to enter it — the
        /// user could pay and still have no route to Premium. This closes that path.
        /// </summary>
        public ReactiveCommand<Unit, Unit> RedeemCommand { get; }

        private string _licenseCodeInput = string.Empty;
        public string LicenseCodeInput
        {
            get => _licenseCodeInput;
            set => this.RaiseAndSetIfChanged(ref _licenseCodeInput, value);
        }

        private string _redeemMessage = string.Empty;
        public string RedeemMessage
        {
            get => _redeemMessage;
            private set => this.RaiseAndSetIfChanged(ref _redeemMessage, value);
        }

        public bool HasRedeemMessage => !string.IsNullOrWhiteSpace(RedeemMessage);

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

        /// <summary>
        /// Verifies a pasted licence token and, if it is genuine and current, stores it
        /// on the vault manifest so the app treats this device as Premium.
        ///
        /// Verification is the entitlement service's job — it checks the Ed25519
        /// signature, the expiry and the USB binding. Nothing here trusts the input,
        /// and a bad code changes no state.
        /// </summary>
        private async Task RedeemAsync()
        {
            var code = (LicenseCodeInput ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(code))
            {
                RedeemMessage = "Paste the licence code from your confirmation email.";
                this.RaisePropertyChanged(nameof(HasRedeemMessage));
                return;
            }

            try
            {
                // Persist first, then re-apply: PersistLicenseTokenAsync writes the token
                // to the manifest and the entitlement service re-evaluates it. If the
                // token is invalid the status simply stays Free and we report that,
                // rather than silently appearing to succeed.
                await _host.PersistLicenseTokenAsync(code);
                RaiseAll();

                if (IsPremium)
                {
                    RedeemMessage = "Licence accepted — Premium is active on this device.";
                    LicenseCodeInput = string.Empty;
                    _host.StatusMessage = "Premium activated successfully.";
                }
                else
                {
                    var reason = Status.Reason switch
                    {
                        LicenseFailureReason.Expired => "That licence code has expired. Renew to get a new one.",
                        LicenseFailureReason.BadSignature => "That code is not a valid Phantom Obscura licence.",
                        LicenseFailureReason.BindingMismatch => "That licence was issued for a different USB key.",
                        LicenseFailureReason.MalformedToken => "That code is not a complete licence code — check it copied fully.",
                        LicenseFailureReason.ClockRollback => "Your system clock looks incorrect. Fix the date and time, then try again.",
                        _ => "That licence code could not be verified."
                    };
                    RedeemMessage = reason;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Licence redemption failed");
                RedeemMessage = "The licence code could not be saved. Please try again.";
            }

            this.RaisePropertyChanged(nameof(HasRedeemMessage));
        }
    }
}
