using System;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model for the auto‑fill settings window. Allows the user to
    /// enable browser auto‑fill and configure domain restrictions. These
    /// settings are placeholders and do not integrate with a real
    /// auto‑fill engine in this example.
    /// </summary>
    public sealed class AutoFillSettingsViewModel : ReactiveObject
    {
        private bool _enableAutoFill;
        private string _domainWhitelist = string.Empty;
        private bool _injectUsername = true;
        private bool _injectPassword = true;
        private bool _isBusy;

        public AutoFillSettingsViewModel()
        {
            // Load saved preferences
            LoadPreferences();

            SaveCommand = ReactiveCommand.Create(() =>
            {
                SavePreferences();
                LastSaved = DateTimeOffset.UtcNow;
            }, this.WhenAnyValue(vm => vm.IsBusy).Select(b => !b));
        }

        private void LoadPreferences()
        {
            var settings = SettingsService.Load();
            _enableAutoFill = settings.EnableAutoFill;
            _domainWhitelist = settings.AutoFillDomainWhitelist;
            _injectUsername = settings.AutoFillInjectUsername;
            _injectPassword = settings.AutoFillInjectPassword;
        }

        private void SavePreferences()
        {
            var settings = SettingsService.Load();
            settings.EnableAutoFill = EnableAutoFill;
            settings.AutoFillDomainWhitelist = DomainWhitelist;
            settings.AutoFillInjectUsername = InjectUsername;
            settings.AutoFillInjectPassword = InjectPassword;
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

        public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    }
}