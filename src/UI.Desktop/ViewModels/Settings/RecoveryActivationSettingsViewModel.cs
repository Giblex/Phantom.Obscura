using System;
using System.Reactive;
using ReactiveUI;
using PhantomVault.Core.Models;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.ViewModels.Settings
{
    public sealed class RecoveryActivationSettingsViewModel : ReactiveObject
    {
        private readonly RecoveryActivationPolicyStore _store;
        private readonly RecoveryActivationSessionState? _sessionState;

        private int _requiredPossession;
        private int _requiredIdentity;

        private bool _usbBindingEnabled;
        private bool _keyfileEnabled;
        private bool _recoveryCodeEnabled;

        private bool _phantomKeyEnabled;
        private bool _passkeyEnabled;
        private bool _passwordEnabled;
        private bool _pinEnabled;

        private string _statusMessage = string.Empty;

        public RecoveryActivationSettingsViewModel(
            RecoveryActivationPolicyStore store,
            RecoveryActivationSessionState? sessionState = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _sessionState = sessionState;

            LoadFromStore();

            SaveCommand = ReactiveCommand.Create(Save);
            ResetCommand = ReactiveCommand.Create(ResetToDefaults);
        }

        public int RequiredPossession
        {
            get => _requiredPossession;
            set => this.RaiseAndSetIfChanged(ref _requiredPossession, ClampPossession(value));
        }

        public int RequiredIdentity
        {
            get => _requiredIdentity;
            set => this.RaiseAndSetIfChanged(ref _requiredIdentity, ClampIdentity(value));
        }

        public bool UsbBindingEnabled { get => _usbBindingEnabled; set => this.RaiseAndSetIfChanged(ref _usbBindingEnabled, value); }
        public bool KeyfileEnabled { get => _keyfileEnabled; set => this.RaiseAndSetIfChanged(ref _keyfileEnabled, value); }
        public bool RecoveryCodeEnabled { get => _recoveryCodeEnabled; set => this.RaiseAndSetIfChanged(ref _recoveryCodeEnabled, value); }

        public bool PhantomKeyEnabled { get => _phantomKeyEnabled; set => this.RaiseAndSetIfChanged(ref _phantomKeyEnabled, value); }
        public bool PasskeyEnabled { get => _passkeyEnabled; set => this.RaiseAndSetIfChanged(ref _passkeyEnabled, value); }
        public bool PasswordEnabled { get => _passwordEnabled; set => this.RaiseAndSetIfChanged(ref _passwordEnabled, value); }
        public bool PinEnabled { get => _pinEnabled; set => this.RaiseAndSetIfChanged(ref _pinEnabled, value); }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public ReactiveCommand<Unit, Unit> SaveCommand { get; }
        public ReactiveCommand<Unit, Unit> ResetCommand { get; }

        private void LoadFromStore()
        {
            var policy = _store.LoadOrCreateDefault();
            ApplyPolicy(policy);
        }

        private void ApplyPolicy(RecoveryActivationPolicy policy)
        {
            _requiredPossession = policy.RequiredPossessionFactors;
            _requiredIdentity = policy.RequiredIdentityFactors;

            _usbBindingEnabled = policy.EnabledPossessionFactors.HasFlag(PossessionFactor.UsbBinding);
            _keyfileEnabled = policy.EnabledPossessionFactors.HasFlag(PossessionFactor.Keyfile);
            _recoveryCodeEnabled = policy.EnabledPossessionFactors.HasFlag(PossessionFactor.RecoveryCode);

            _phantomKeyEnabled = policy.EnabledIdentityFactors.HasFlag(IdentityFactor.PhantomKey);
            _passkeyEnabled = policy.EnabledIdentityFactors.HasFlag(IdentityFactor.Passkey);
            _passwordEnabled = policy.EnabledIdentityFactors.HasFlag(IdentityFactor.Password);
            _pinEnabled = policy.EnabledIdentityFactors.HasFlag(IdentityFactor.Pin);

            this.RaisePropertyChanged(nameof(RequiredPossession));
            this.RaisePropertyChanged(nameof(RequiredIdentity));
            this.RaisePropertyChanged(nameof(UsbBindingEnabled));
            this.RaisePropertyChanged(nameof(KeyfileEnabled));
            this.RaisePropertyChanged(nameof(RecoveryCodeEnabled));
            this.RaisePropertyChanged(nameof(PhantomKeyEnabled));
            this.RaisePropertyChanged(nameof(PasskeyEnabled));
            this.RaisePropertyChanged(nameof(PasswordEnabled));
            this.RaisePropertyChanged(nameof(PinEnabled));
        }

        private void Save()
        {
            var possession = PossessionFactor.None;
            if (UsbBindingEnabled) possession |= PossessionFactor.UsbBinding;
            if (KeyfileEnabled) possession |= PossessionFactor.Keyfile;
            if (RecoveryCodeEnabled) possession |= PossessionFactor.RecoveryCode;

            var identity = IdentityFactor.None;
            if (PhantomKeyEnabled) identity |= IdentityFactor.PhantomKey;
            if (PasskeyEnabled) identity |= IdentityFactor.Passkey;
            if (PasswordEnabled) identity |= IdentityFactor.Password;
            if (PinEnabled) identity |= IdentityFactor.Pin;

            int enabledPossessionCount = RecoveryActivationPolicyEvaluatorBridge.CountEnabled(possession);
            int enabledIdentityCount = RecoveryActivationPolicyEvaluatorBridge.CountEnabled(identity);

            if (RequiredPossession > enabledPossessionCount || RequiredIdentity > enabledIdentityCount)
            {
                StatusMessage = "Cannot save: required factor count exceeds the number of enabled factors.";
                return;
            }

            var existing = _store.LoadOrCreateDefault();
            existing.RequiredPossessionFactors = RequiredPossession;
            existing.RequiredIdentityFactors = RequiredIdentity;
            existing.EnabledPossessionFactors = possession;
            existing.EnabledIdentityFactors = identity;

            _store.Save(existing);
            StatusMessage = $"Saved at {DateTime.Now:HH:mm:ss}. Recovery launch now requires ≥{RequiredPossession} possession + ≥{RequiredIdentity} identity factors.";
        }

        private void ResetToDefaults()
        {
            ApplyPolicy(new RecoveryActivationPolicy());
            StatusMessage = "Defaults restored (not saved until you press Save).";
        }

        private static int ClampPossession(int value) => Math.Clamp(value, 1, 3);
        private static int ClampIdentity(int value) => Math.Clamp(value, 1, 4);
    }

    // Local bridge to expose the Core evaluator's CountEnabled without forcing the VM file
    // to take a direct dependency on the static helper namespace at the top.
    internal static class RecoveryActivationPolicyEvaluatorBridge
    {
        public static int CountEnabled(PossessionFactor f)
            => PhantomVault.Core.Services.RecoveryActivationPolicyEvaluator.CountEnabled(f);
        public static int CountEnabled(IdentityFactor f)
            => PhantomVault.Core.Services.RecoveryActivationPolicyEvaluator.CountEnabled(f);
    }
}
