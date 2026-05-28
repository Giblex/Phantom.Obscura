using System;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using PhantomVault.UI.Services;
using PhantomVault.UI.Services.TrayBackground;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model for the auto‑fill settings panel. Manages both the legacy
    /// browser auto‑fill options and the new USB-triggered AutoFill Mode that
    /// runs in the system tray.
    /// </summary>
    public sealed class AutoFillSettingsViewModel : ReactiveObject
    {
        private readonly ITrayBackgroundService? _trayService;
        // Issue #25/AutoFill: shared draft tracker so the overlay-bottom Save
        // button can drive AutoFill persistence too. AutoFill's own SaveCommand
        // continues to work (it just commits the staged 'AutoFill.All' entry).
        private readonly SettingsDraftTracker _draft;
        private AutoFillBaseline _baseline;
        private bool _suppressStage;

        private bool _enableAutoFill;
        private string _domainWhitelist = string.Empty;
        private bool _injectUsername = true;
        private bool _injectPassword = true;
        private bool _autoSubmit;
        private bool _showIcon = true;
        private bool _desktopApps;
        private bool _isBusy;

        // AutoFill Mode (USB-triggered)
        private bool _autoFillModeEnabled;
        private bool _autoFillAutoInputTotp = true;
        private bool _autoFillShowNewEntryOnNoMatch = true;

        private readonly struct AutoFillBaseline
        {
            public bool EnableAutoFill { get; init; }
            public string DomainWhitelist { get; init; }
            public bool InjectUsername { get; init; }
            public bool InjectPassword { get; init; }
            public bool AutoSubmit { get; init; }
            public bool ShowIcon { get; init; }
            public bool DesktopApps { get; init; }
            public bool AutoFillModeEnabled { get; init; }
            public bool AutoFillAutoInputTotp { get; init; }
            public bool AutoFillShowNewEntryOnNoMatch { get; init; }
        }

        public AutoFillSettingsViewModel() : this(null, null) { }
        public AutoFillSettingsViewModel(ITrayBackgroundService? trayService) : this(trayService, null) { }

        public AutoFillSettingsViewModel(ITrayBackgroundService? trayService, SettingsDraftTracker? draftTracker)
        {
            _trayService = trayService;
            _draft = draftTracker
                ?? ((Avalonia.Application.Current as App)?.Services?.GetService(typeof(SettingsDraftTracker)) as SettingsDraftTracker)
                ?? new SettingsDraftTracker();

            _suppressStage = true;
            LoadPreferences();
            _baseline = SnapshotCurrent();
            _suppressStage = false;

            // Local SaveCommand kept for backward compat — commits immediately
            // (and the shared tracker entry was already calling the same
            // SavePreferences path, so HasUnsavedChanges drops to false).
            SaveCommand = ReactiveCommand.Create(() =>
            {
                SavePreferences();
                _baseline = SnapshotCurrent();
                _draft.ClearKey("AutoFill.All");
                LastSaved = DateTimeOffset.UtcNow;
            }, this.WhenAnyValue(vm => vm.IsBusy).Select(b => !b));

            // Start/stop tray service when AutoFill Mode toggle changes
            this.WhenAnyValue(vm => vm.AutoFillModeEnabled)
                .Subscribe(enabled =>
                {
                    if (_trayService is null) return;
                    if (enabled)
                        _ = _trayService.StartAsync();
                    else
                        _ = _trayService.StopAsync();
                });
        }

        private void LoadPreferences()
        {
            var settings = SettingsService.Load();
            _enableAutoFill = settings.EnableAutoFill;
            _domainWhitelist = settings.AutoFillDomainWhitelist;
            _injectUsername = settings.AutoFillInjectUsername;
            _injectPassword = settings.AutoFillInjectPassword;
            _autoSubmit = settings.AutoFillAutoSubmit;
            _showIcon = settings.AutoFillShowIcon;
            _desktopApps = settings.AutoFillDesktopApps;
            _autoFillModeEnabled = settings.AutoFillModeEnabled;
            _autoFillAutoInputTotp = settings.AutoFillAutoInputTotp;
            _autoFillShowNewEntryOnNoMatch = settings.AutoFillShowNewEntryOnNoMatch;
        }

        private void SavePreferences()
        {
            var settings = SettingsService.Load();
            settings.EnableAutoFill = EnableAutoFill;
            settings.AutoFillDomainWhitelist = DomainWhitelist;
            settings.AutoFillInjectUsername = InjectUsername;
            settings.AutoFillInjectPassword = InjectPassword;
            settings.AutoFillAutoSubmit = AutoSubmit;
            settings.AutoFillShowIcon = ShowIcon;
            settings.AutoFillDesktopApps = DesktopApps;
            settings.AutoFillModeEnabled = AutoFillModeEnabled;
            settings.AutoFillAutoInputTotp = AutoFillAutoInputTotp;
            settings.AutoFillShowNewEntryOnNoMatch = AutoFillShowNewEntryOnNoMatch;
            SettingsService.Save(settings);
        }

        /// <summary>
        /// Enables or disables the auto‑fill engine as a whole. When
        /// disabled no credentials will be injected automatically into
        /// websites or applications.
        /// </summary>
        public bool EnableAutoFill
        {
            get => _enableAutoFill;
            set { this.RaiseAndSetIfChanged(ref _enableAutoFill, value); StageAll(); }
        }

        /// <summary>
        /// A comma separated list of domain names for which auto‑fill is
        /// allowed. If empty, all domains are eligible. Domains should be
        /// specified without protocol (e.g. "example.com").
        /// </summary>
        public string DomainWhitelist
        {
            get => _domainWhitelist;
            set { this.RaiseAndSetIfChanged(ref _domainWhitelist, value); StageAll(); }
        }

        /// <summary>
        /// Indicates whether a save operation is in progress. Used to
        /// disable the Save button while persisting settings.
        /// </summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        public DateTimeOffset? LastSaved { get; private set; }

        /// <summary>
        /// Determines whether the username part of a credential should be
        /// automatically injected. Some users may prefer to type the
        /// username manually while still having the password auto‑filled.
        /// </summary>
        public bool InjectUsername
        {
            get => _injectUsername;
            set { this.RaiseAndSetIfChanged(ref _injectUsername, value); StageAll(); }
        }

        /// <summary>
        /// Determines whether the password component of a credential
        /// should be auto‑filled. This can be disabled to require manual
        /// entry of the password, while still providing convenience for
        /// other fields.
        /// </summary>
        public bool InjectPassword
        {
            get => _injectPassword;
            set { this.RaiseAndSetIfChanged(ref _injectPassword, value); StageAll(); }
        }

        /// <summary>
        /// Automatically submit the login form after credentials are filled.
        /// </summary>
        public bool AutoSubmit
        {
            get => _autoSubmit;
            set { this.RaiseAndSetIfChanged(ref _autoSubmit, value); StageAll(); }
        }

        /// <summary>
        /// Show the PhantomVault icon inside detected password fields.
        /// </summary>
        public bool ShowIcon
        {
            get => _showIcon;
            set { this.RaiseAndSetIfChanged(ref _showIcon, value); StageAll(); }
        }

        /// <summary>
        /// Allow auto-fill into native desktop applications (not just browsers).
        /// </summary>
        public bool DesktopApps
        {
            get => _desktopApps;
            set { this.RaiseAndSetIfChanged(ref _desktopApps, value); StageAll(); }
        }

        // ===== AutoFill Mode (USB-triggered) =====

        /// <summary>
        /// Enables USB-triggered AutoFill Mode. When active the app runs in the
        /// system tray and auto-fills login portals when the USB key is inserted.
        /// </summary>
        public bool AutoFillModeEnabled
        {
            get => _autoFillModeEnabled;
            set { this.RaiseAndSetIfChanged(ref _autoFillModeEnabled, value); StageAll(); }
        }

        /// <summary>
        /// When true, TOTP codes are automatically detected and input after password fill.
        /// </summary>
        public bool AutoFillAutoInputTotp
        {
            get => _autoFillAutoInputTotp;
            set { this.RaiseAndSetIfChanged(ref _autoFillAutoInputTotp, value); StageAll(); }
        }

        /// <summary>
        /// When true, shows the no-match dialog when no stored credential matches
        /// the detected login portal. Passkey handoff only appears when Attestor
        /// integration is linked.
        /// </summary>
        public bool AutoFillShowNewEntryOnNoMatch
        {
            get => _autoFillShowNewEntryOnNoMatch;
            set { this.RaiseAndSetIfChanged(ref _autoFillShowNewEntryOnNoMatch, value); StageAll(); }
        }

        public ReactiveCommand<Unit, Unit> SaveCommand { get; }

        private AutoFillBaseline SnapshotCurrent() => new AutoFillBaseline
        {
            EnableAutoFill = EnableAutoFill,
            DomainWhitelist = DomainWhitelist,
            InjectUsername = InjectUsername,
            InjectPassword = InjectPassword,
            AutoSubmit = AutoSubmit,
            ShowIcon = ShowIcon,
            DesktopApps = DesktopApps,
            AutoFillModeEnabled = AutoFillModeEnabled,
            AutoFillAutoInputTotp = AutoFillAutoInputTotp,
            AutoFillShowNewEntryOnNoMatch = AutoFillShowNewEntryOnNoMatch,
        };

        private bool MatchesBaseline()
        {
            var b = _baseline;
            return EnableAutoFill == b.EnableAutoFill
                && string.Equals(DomainWhitelist ?? string.Empty, b.DomainWhitelist ?? string.Empty, StringComparison.Ordinal)
                && InjectUsername == b.InjectUsername
                && InjectPassword == b.InjectPassword
                && AutoSubmit == b.AutoSubmit
                && ShowIcon == b.ShowIcon
                && DesktopApps == b.DesktopApps
                && AutoFillModeEnabled == b.AutoFillModeEnabled
                && AutoFillAutoInputTotp == b.AutoFillAutoInputTotp
                && AutoFillShowNewEntryOnNoMatch == b.AutoFillShowNewEntryOnNoMatch;
        }

        // Issue #27: setters route through here so the overlay-bottom Save/
        // Discard buttons drive AutoFill persistence too. Re-staging replaces
        // the prior pair so flipping multiple toggles still results in a
        // single commit/discard.
        private void StageAll()
        {
            if (_suppressStage) return;
            if (MatchesBaseline())
            {
                _draft.ClearKey("AutoFill.All");
                return;
            }
            var staged = SnapshotCurrent();
            var baseline = _baseline;
            _draft.Stage(
                key: "AutoFill.All",
                commit: () =>
                {
                    var s = SettingsService.Load();
                    s.EnableAutoFill = staged.EnableAutoFill;
                    s.AutoFillDomainWhitelist = staged.DomainWhitelist;
                    s.AutoFillInjectUsername = staged.InjectUsername;
                    s.AutoFillInjectPassword = staged.InjectPassword;
                    s.AutoFillAutoSubmit = staged.AutoSubmit;
                    s.AutoFillShowIcon = staged.ShowIcon;
                    s.AutoFillDesktopApps = staged.DesktopApps;
                    s.AutoFillModeEnabled = staged.AutoFillModeEnabled;
                    s.AutoFillAutoInputTotp = staged.AutoFillAutoInputTotp;
                    s.AutoFillShowNewEntryOnNoMatch = staged.AutoFillShowNewEntryOnNoMatch;
                    SettingsService.Save(s);
                    _baseline = staged;
                },
                discard: () =>
                {
                    _suppressStage = true;
                    try
                    {
                        EnableAutoFill = baseline.EnableAutoFill;
                        DomainWhitelist = baseline.DomainWhitelist;
                        InjectUsername = baseline.InjectUsername;
                        InjectPassword = baseline.InjectPassword;
                        AutoSubmit = baseline.AutoSubmit;
                        ShowIcon = baseline.ShowIcon;
                        DesktopApps = baseline.DesktopApps;
                        AutoFillModeEnabled = baseline.AutoFillModeEnabled;
                        AutoFillAutoInputTotp = baseline.AutoFillAutoInputTotp;
                        AutoFillShowNewEntryOnNoMatch = baseline.AutoFillShowNewEntryOnNoMatch;
                    }
                    finally
                    {
                        _suppressStage = false;
                    }
                });
        }
    }
}
