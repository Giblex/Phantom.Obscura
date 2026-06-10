using System;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using PhantomVault.UI.Services;
using PhantomVault.UI.Services.TrayBackground;

namespace PhantomVault.UI.ViewModels
{

    public sealed class AutoFillSettingsViewModel : ReactiveObject
    {
        private readonly ITrayBackgroundService? _trayService;

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

            SaveCommand = ReactiveCommand.Create(() =>
            {
                SavePreferences();
                _baseline = SnapshotCurrent();
                _draft.ClearKey("AutoFill.All");
                LastSaved = DateTimeOffset.UtcNow;
            }, this.WhenAnyValue(vm => vm.IsBusy).Select(b => !b));

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

        public bool EnableAutoFill
        {
            get => _enableAutoFill;
            set { this.RaiseAndSetIfChanged(ref _enableAutoFill, value); StageAll(); }
        }

        public string DomainWhitelist
        {
            get => _domainWhitelist;
            set { this.RaiseAndSetIfChanged(ref _domainWhitelist, value); StageAll(); }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        public DateTimeOffset? LastSaved { get; private set; }

        public bool InjectUsername
        {
            get => _injectUsername;
            set { this.RaiseAndSetIfChanged(ref _injectUsername, value); StageAll(); }
        }

        public bool InjectPassword
        {
            get => _injectPassword;
            set { this.RaiseAndSetIfChanged(ref _injectPassword, value); StageAll(); }
        }

        public bool AutoSubmit
        {
            get => _autoSubmit;
            set { this.RaiseAndSetIfChanged(ref _autoSubmit, value); StageAll(); }
        }

        public bool ShowIcon
        {
            get => _showIcon;
            set { this.RaiseAndSetIfChanged(ref _showIcon, value); StageAll(); }
        }

        public bool DesktopApps
        {
            get => _desktopApps;
            set { this.RaiseAndSetIfChanged(ref _desktopApps, value); StageAll(); }
        }

        public bool AutoFillModeEnabled
        {
            get => _autoFillModeEnabled;
            set { this.RaiseAndSetIfChanged(ref _autoFillModeEnabled, value); StageAll(); }
        }

        public bool AutoFillAutoInputTotp
        {
            get => _autoFillAutoInputTotp;
            set { this.RaiseAndSetIfChanged(ref _autoFillAutoInputTotp, value); StageAll(); }
        }

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

