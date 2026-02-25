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

        private bool _enableAutoFill;
        private string _domainWhitelist = string.Empty;
        private bool _injectUsername = true;
        private bool _injectPassword = true;
        private bool _isBusy;

        // AutoFill Mode (USB-triggered)
        private bool _autoFillModeEnabled;
        private bool _autoFillAutoInputTotp = true;
        private bool _autoFillShowNewEntryOnNoMatch = true;

        public AutoFillSettingsViewModel() : this(null) { }

        public AutoFillSettingsViewModel(ITrayBackgroundService? trayService)
        {
            _trayService = trayService;

            // Load saved preferences
            LoadPreferences();

            SaveCommand = ReactiveCommand.Create(() =>
            {
                SavePreferences();
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
            set => this.RaiseAndSetIfChanged(ref _enableAutoFill, value);
        }

        /// <summary>
        /// A comma separated list of domain names for which auto‑fill is
        /// allowed. If empty, all domains are eligible. Domains should be
        /// specified without protocol (e.g. "example.com").
        /// </summary>
        public string DomainWhitelist
        {
            get => _domainWhitelist;
            set => this.RaiseAndSetIfChanged(ref _domainWhitelist, value);
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
            set => this.RaiseAndSetIfChanged(ref _injectUsername, value);
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
            set => this.RaiseAndSetIfChanged(ref _injectPassword, value);
        }

        // ===== AutoFill Mode (USB-triggered) =====

        /// <summary>
        /// Enables USB-triggered AutoFill Mode. When active the app runs in the
        /// system tray and auto-fills login portals when the USB key is inserted.
        /// </summary>
        public bool AutoFillModeEnabled
        {
            get => _autoFillModeEnabled;
            set => this.RaiseAndSetIfChanged(ref _autoFillModeEnabled, value);
        }

        /// <summary>
        /// When true, TOTP codes are automatically detected and input after password fill.
        /// </summary>
        public bool AutoFillAutoInputTotp
        {
            get => _autoFillAutoInputTotp;
            set => this.RaiseAndSetIfChanged(ref _autoFillAutoInputTotp, value);
        }

        /// <summary>
        /// When true, shows a "New Entry / Create Passkey" dialog when no stored
        /// credential matches the detected login portal.
        /// </summary>
        public bool AutoFillShowNewEntryOnNoMatch
        {
            get => _autoFillShowNewEntryOnNoMatch;
            set => this.RaiseAndSetIfChanged(ref _autoFillShowNewEntryOnNoMatch, value);
        }

        public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    }
}