using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using ReactiveUI;
using PhantomVault.Core.Services.Security;
using PhantomVault.Core.Options;
using PhantomVault.UI.Services;
using PhantomVault.UI.Desktop.Services;
using PhantomVault.UI.Views.Dialogs;
using PhantomVault.UI.Views;

namespace PhantomVault.UI.ViewModels
{

    public sealed class SecuritySettingsViewModel : ReactiveObject
    {
        private readonly IDefenceSettingsService? _defenceSettings;
        private readonly DialogService _dialogService;
        private readonly DecoyVaultService? _decoyVaultService;
        private readonly TamperDetectionService? _tamperDetectionService;
        private readonly SecurityOptions _securityOptions;
        private bool _requireHardwareToken;
        private bool _requireKeyfile;
        private int _idleTimeoutMinutes = 5;
        private bool _isBusy;
        private string _saveButtonText = "Save";
        private bool _enablePinLock;
        private bool _usePinLockForAutoLock;
        private bool _autoCopyTotpWithPassword;
        private int _clipboardClearTimeIndex;
        private bool _enableScreenshotProtection;
        private string? _manifestPath;

        private bool _autoActivateDecoyOnTamper = true;
        private int _decoyCredentialCount = 25;
        private bool _decoyReadOnlyMode = true;
        private bool _logDecoyActivation = true;
        private bool _isDecoyCurrentlyActive;

        private readonly SettingsDraftTracker _draft;
        private SecurityBaseline _baseline;
        private bool _suppressStage;

        private readonly VaultViewModel? _hostViewModel;

        private readonly struct SecurityBaseline
        {
            public bool EnablePinLock { get; init; }
            public bool UsePinLockForAutoLock { get; init; }
            public bool RequireHardwareToken { get; init; }
            public bool RequireKeyfile { get; init; }
            public int IdleTimeoutMinutes { get; init; }
            public bool AutoCopyTotpWithPassword { get; init; }
            public int ClipboardClearTimeIndex { get; init; }
            public bool EnableScreenshotProtection { get; init; }
            public bool AutoActivateDecoyOnTamper { get; init; }
            public int DecoyCredentialCount { get; init; }
            public bool DecoyReadOnlyMode { get; init; }
            public bool LogDecoyActivation { get; init; }
        }

        public SecuritySettingsViewModel(
            IDefenceSettingsService? defenceSettingsService = null,
            string? manifestPath = null,
            DecoyVaultService? decoyVaultService = null,
            TamperDetectionService? tamperDetectionService = null,
            SecurityOptions? securityOptions = null,
            VaultViewModel? hostViewModel = null)
        {
            _defenceSettings = defenceSettingsService;
            _dialogService = new DialogService();
            _manifestPath = manifestPath;
            _decoyVaultService = decoyVaultService;
            _tamperDetectionService = tamperDetectionService;
            _securityOptions = securityOptions ?? new SecurityOptions();
            _hostViewModel = hostViewModel;

            _draft = ((Avalonia.Application.Current as App)?.Services?.GetService(typeof(SettingsDraftTracker)) as SettingsDraftTracker)
                ?? new SettingsDraftTracker();
            _suppressStage = true;

            // Clears stale PIN flags before snapshotting, so the toggles can never
            // read as "on" when no PIN has actually been set.
            _hasPinConfigured = PinLockService.SyncPinFlags(_manifestPath);

            var settings = SettingsService.LoadSecuritySnapshot();
            _enablePinLock = settings.EnablePinLock && _hasPinConfigured;
            _usePinLockForAutoLock = settings.UsePinLockForAutoLock && _hasPinConfigured;
            _autoCopyTotpWithPassword = settings.AutoCopyTotpWithPassword;
            _requireHardwareToken = settings.RequireHardwareToken;
            _requireKeyfile = settings.RequireKeyfile;
            _idleTimeoutMinutes = settings.IdleTimeoutMinutes;
            _clipboardClearTimeIndex = settings.ClipboardClearTime;
            _enableScreenshotProtection = settings.EnableScreenshotProtection;

            _autoActivateDecoyOnTamper = settings.EnableDecoyVault;
            _decoyCredentialCount = settings.DecoyCredentialCount;
            _decoyReadOnlyMode = settings.DecoyReadOnlyMode;
            _logDecoyActivation = settings.DecoyLogActivations;
            _isDecoyCurrentlyActive = _tamperDetectionService?.IsDecoyActive ?? false;

            _baseline = SnapshotCurrent();
            _suppressStage = false;

            SaveCommand = ReactiveCommand.CreateFromTask(SaveSettingsAsync,
                this.WhenAnyValue(vm => vm.IsBusy).Select(b => !b));

            SetOrChangePinCommand = ReactiveCommand.CreateFromTask(SetOrChangePinAsync,
                this.WhenAnyValue(vm => vm.IsBusy).Select(b => !b));

            ClearPinCommand = ReactiveCommand.CreateFromTask(ClearPinAsync,
                this.WhenAnyValue(vm => vm.IsBusy).Select(b => !b));

            // Fast Unlock state: load persisted user preference; the "needs re-key" flag is
            // derived against the host VM's current manifest KDF tier.
            try { _useFastUnlock = SettingsService.Load().UseFastUnlock; } catch { _useFastUnlock = false; }
            ApplyFastUnlockReKeyCommand = ReactiveCommand.CreateFromTask(ApplyFastUnlockReKeyAsync,
                this.WhenAnyValue(vm => vm.IsBusy).Select(b => !b));

            PreviewDecoyVaultCommand = ReactiveCommand.CreateFromTask(PreviewDecoyVaultAsync,
                this.WhenAnyValue(vm => vm.IsBusy, vm => vm.AutoActivateDecoyOnTamper).Select(x => !x.Item1 && x.Item2));

            DeactivateDecoyCommand = ReactiveCommand.CreateFromTask(DeactivateDecoyAsync,
                this.WhenAnyValue(vm => vm.IsBusy, vm => vm.IsDecoyCurrentlyActive).Select(x => !x.Item1 && x.Item2));

            this.WhenAnyValue(
                x => x.AutoActivateDecoyOnTamper,
                x => x.DecoyCredentialCount,
                x => x.LogDecoyActivation)
                .Throttle(TimeSpan.FromMilliseconds(500))
                .Subscribe(_ => SaveDecoySettings());

            if (_tamperDetectionService != null)
            {
                _tamperDetectionService.TamperDetected += OnTamperDetected;
            }

            if (_hostViewModel != null)
            {
                _hostViewModel.PropertyChanged += OnHostPropertyChanged;
            }
        }

        private void OnHostPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VaultViewModel.CurrentKeyfileDisplay))
            {
                this.RaisePropertyChanged(nameof(CurrentKeyfileDisplay));
            }
            else if (e.PropertyName == nameof(VaultViewModel.CurrentManifestKdfDisplay)
                  || e.PropertyName == nameof(VaultViewModel.IsManifestKdfFast))
            {
                this.RaisePropertyChanged(nameof(CurrentManifestKdfDisplay));
                this.RaisePropertyChanged(nameof(FastUnlockNeedsReKey));
            }
            else if (e.PropertyName == nameof(VaultViewModel.PrivacyModeEnabled))
            {
                this.RaisePropertyChanged(nameof(PrivacyModeEnabled));
            }
        }

        private void OnTamperDetected(object? sender, TamperDetectedEventArgs e)
        {
            if (e.DecoyActivated)
            {
                IsDecoyCurrentlyActive = true;
            }
        }

        private async Task SaveSettingsAsync()
        {
            IsBusy = true;
            SaveButtonText = "Saving...";

            try
            {

                await Task.Delay(300);

                _draft.CommitAll();

                LastSaved = DateTimeOffset.UtcNow;
                SaveButtonText = "✓ Saved";

                await Task.Delay(2000);
                SaveButtonText = "Save";
            }
            catch (Exception ex)
            {
                SaveButtonText = "Save";
                await _dialogService.ShowErrorAsync(
                    "Save Failed",
                    $"Failed to save security settings: {ex.Message}",
                    null);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public bool RequireHardwareToken
        {
            get => _requireHardwareToken;
            set
            {
                if (_requireHardwareToken != value)
                {
                    this.RaiseAndSetIfChanged(ref _requireHardwareToken, value);
                    StageAll();
                }
            }
        }

        public bool RequireKeyfile
        {
            get => _requireKeyfile;
            set
            {
                if (_requireKeyfile != value)
                {
                    this.RaiseAndSetIfChanged(ref _requireKeyfile, value);
                    StageAll();
                }
            }
        }

        public int IdleTimeoutMinutes
        {
            get => _idleTimeoutMinutes;
            set
            {
                if (_idleTimeoutMinutes != value)
                {
                    this.RaiseAndSetIfChanged(ref _idleTimeoutMinutes, value);
                    StageAll();
                }
            }
        }

        public bool PrivacyModeEnabled
        {
            get => _hostViewModel?.PrivacyModeEnabled ?? PrivacyShield.PrivacyModeEnabled;
            set
            {
                if (PrivacyModeEnabled == value)
                {
                    return;
                }

                if (_hostViewModel != null)
                {
                    _hostViewModel.PrivacyModeEnabled = value;
                }
                else if (PrivacyShield.PrivacyModeEnabled != value)
                {
                    PrivacyShield.PrivacyModeEnabled = value;
                    try { SettingsService.Update(s => s.PrivacyModeEnabled = value); } catch { }
                }

                this.RaisePropertyChanged();
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        public string SaveButtonText
        {
            get => _saveButtonText;
            private set => this.RaiseAndSetIfChanged(ref _saveButtonText, value);
        }

        public DateTimeOffset? LastSaved { get; private set; }

        public ReactiveCommand<Unit, Unit> SaveCommand { get; }

        public ReactiveCommand<Unit, Unit> SetOrChangePinCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearPinCommand { get; }

        public ReactiveCommand<Unit, Unit>? BackupKeyfileCommand => _hostViewModel?.BackupKeyfileCommand;
        public ReactiveCommand<Unit, Unit>? RegenerateKeyfileCommand => _hostViewModel?.RegenerateKeyfileCommand;
        public ReactiveCommand<Unit, Unit>? ChangeKeyfileCommand => _hostViewModel?.ChangeKeyfileCommand;
        public ReactiveCommand<Unit, Unit>? ChangeMasterPasswordCommand => _hostViewModel?.ChangeMasterPasswordCommand;
        public string CurrentKeyfileDisplay => _hostViewModel?.CurrentKeyfileDisplay ?? "No keyfile on record";

        // === Fast Unlock ===
        // Setting flips the user's preference. The actual KDF change requires a re-key,
        // which is triggered explicitly via ApplyFastUnlockReKeyCommand. We split the two
        // so the user knows whether their preference matches the manifest's current state.
        private bool _useFastUnlock;
        public bool UseFastUnlock
        {
            get => _useFastUnlock;
            set
            {
                if (_useFastUnlock != value)
                {
                    this.RaiseAndSetIfChanged(ref _useFastUnlock, value);
                    try { SettingsService.Update(s => s.UseFastUnlock = value); }
                    catch (Exception ex) { Serilog.Log.Warning(ex, "[SecuritySettings] persist UseFastUnlock failed"); }
                    this.RaisePropertyChanged(nameof(FastUnlockNeedsReKey));
                }
            }
        }

        public string CurrentManifestKdfDisplay => _hostViewModel?.CurrentManifestKdfDisplay ?? "Unknown";

        /// <summary>
        /// True when the user's preference differs from the manifest's current KDF tier.
        /// Used to highlight the "Apply Fast Unlock" button when an action is required.
        /// </summary>
        public bool FastUnlockNeedsReKey
        {
            get
            {
                if (_hostViewModel == null) return false;
                bool manifestIsFast = _hostViewModel.IsManifestKdfFast;
                return _useFastUnlock != manifestIsFast;
            }
        }

        public ReactiveCommand<Unit, Unit> ApplyFastUnlockReKeyCommand { get; }

        private async Task ApplyFastUnlockReKeyAsync()
        {
            if (_hostViewModel == null) return;
            bool ok = await _hostViewModel.RekeyManifestForFastUnlockAsync(_useFastUnlock).ConfigureAwait(false);
            if (ok)
            {
                this.RaisePropertyChanged(nameof(CurrentManifestKdfDisplay));
                this.RaisePropertyChanged(nameof(FastUnlockNeedsReKey));
            }
        }

        private bool _hasPinConfigured;

        /// <summary>
        /// True only when a PIN has actually been set (settings or manifest).
        /// The PIN toggles bind their IsEnabled to this — there is nothing to
        /// enable until "Set PIN" has been used.
        /// </summary>
        public bool HasPinConfigured
        {
            get => _hasPinConfigured;
            private set => this.RaiseAndSetIfChanged(ref _hasPinConfigured, value);
        }

        public bool EnablePinLock
        {
            get => _enablePinLock;
            set
            {
                // Refuse to arm PIN lock when no PIN exists — otherwise auto-lock
                // ends up gated behind a PIN the user never chose.
                if (value && !HasPinConfigured)
                {
                    this.RaisePropertyChanged(nameof(EnablePinLock));
                    return;
                }

                this.RaiseAndSetIfChanged(ref _enablePinLock, value);

                if (!value && _usePinLockForAutoLock)
                {
                    this.RaiseAndSetIfChanged(ref _usePinLockForAutoLock, false);
                }
                StageAll();
            }
        }

        public bool UsePinLockForAutoLock
        {
            get => _usePinLockForAutoLock;
            set
            {
                if (value && (!HasPinConfigured || !_enablePinLock))
                {
                    this.RaisePropertyChanged(nameof(UsePinLockForAutoLock));
                    return;
                }

                this.RaiseAndSetIfChanged(ref _usePinLockForAutoLock, value);
                StageAll();
            }
        }

        public bool AutoCopyTotpWithPassword
        {
            get => _autoCopyTotpWithPassword;
            set
            {
                this.RaiseAndSetIfChanged(ref _autoCopyTotpWithPassword, value);
                StageAll();
            }
        }

        public int ClipboardClearTimeIndex
        {
            get => _clipboardClearTimeIndex;
            set
            {
                var sanitized = SettingsService.NormalizeClipboardClearTimeIndex(value);
                if (_clipboardClearTimeIndex != sanitized)
                {
                    this.RaiseAndSetIfChanged(ref _clipboardClearTimeIndex, sanitized);
                    this.RaisePropertyChanged(nameof(ClearClipboardAfterCopy));
                    StageAll();
                }
            }
        }

        public bool ClearClipboardAfterCopy
        {
            get => SettingsService.IsClipboardAutoClearEnabled(ClipboardClearTimeIndex);
            set
            {
                if (value)
                {
                    if (!SettingsService.IsClipboardAutoClearEnabled(ClipboardClearTimeIndex))
                    {
                        ClipboardClearTimeIndex = 1;
                    }
                }
                else
                {
                    ClipboardClearTimeIndex = 4;
                }
            }
        }

        public bool EnableScreenshotProtection
        {
            get => _enableScreenshotProtection;
            set
            {
                if (_enableScreenshotProtection != value)
                {
                    this.RaiseAndSetIfChanged(ref _enableScreenshotProtection, value);
                    SettingsService.Update(s => s.EnableScreenshotProtection = value);
                    _baseline = _baseline with { EnableScreenshotProtection = value };
                    StageAll();

                    ApplyScreenshotProtectionToWindows(value);
                }
            }
        }

        private static void ApplyScreenshotProtectionToWindows(bool enable)
        {
            try
            {
                if (!WindowProtectionService.IsSupported()) return;
                if (Avalonia.Application.Current?.ApplicationLifetime
                        is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    foreach (var window in desktop.Windows.ToList())
                    {
                        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                        if (handle == IntPtr.Zero) continue;
                        if (enable)
                            WindowProtectionService.EnableScreenshotProtection(handle);
                        else
                            WindowProtectionService.DisableScreenshotProtection(handle);
                    }
                }
            }
            catch (Exception ex)
            {
                // A failure here means the user believes screenshot protection is on when
                // it is not. Surfacing it beats the previous silent swallow.
                Serilog.Log.Error(ex, "Failed to apply screenshot protection (enable={Enable})", enable);
                RecentIssuesLog.Instance.Record(
                    IssueSeverity.Warning,
                    "Screenshot protection not applied",
                    "Windows rejected the screen-capture protection request. Vault windows may still be capturable.");
            }
        }

        private async Task SetOrChangePinAsync()
        {
            try
            {
                var owner = GetOwnerWindow();
                var dialog = new PinSetupDialog(_manifestPath);

                if (owner != null)
                {
                    await dialog.ShowDialog(owner);
                }
                else
                {
                    dialog.Show();
                }

                var viewModel = dialog.DataContext as PhantomVault.UI.ViewModels.Dialogs.PinSetupDialogViewModel;
                if (viewModel?.Success == true)
                {
                    // A PIN now exists — unblock the toggles before arming them.
                    HasPinConfigured = true;
                    EnablePinLock = true;
                    await _dialogService.ShowSuccessAsync("PIN Set", "Your PIN lock has been enabled.", owner);
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("PIN Setup Failed", ex.Message, GetOwnerWindow());
            }
        }

        private async Task ClearPinAsync()
        {
            try
            {
                PinLockService.ClearPin(_manifestPath);
                EnablePinLock = false;
                HasPinConfigured = false;
                await _dialogService.ShowSuccessAsync("PIN Disabled", "PIN lock has been disabled.", GetOwnerWindow());
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("PIN Disable Failed", ex.Message, GetOwnerWindow());
            }
        }

        private Window? GetOwnerWindow()
        {

            return Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.Windows.FirstOrDefault(w => w.IsActive)
                : null;
        }

        private static async Task<string?> PromptForSecretAsync(Window? owner, string title, string message)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 420,
                Height = 190,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            var panel = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = message, FontWeight = Avalonia.Media.FontWeight.SemiBold });

            var input = new TextBox { PasswordChar = '●', Watermark = "PIN", Width = 360 };
            panel.Children.Add(input);

            var buttons = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Spacing = 10
            };
            var ok = new Button { Content = "OK", Width = 80, IsDefault = true };
            var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            panel.Children.Add(buttons);

            dialog.Content = panel;

            string? result = null;
            ok.Click += (_, _) => { result = input.Text; dialog.Close(); };
            cancel.Click += (_, _) => { dialog.Close(); };

            if (owner != null)
            {
                await dialog.ShowDialog(owner);
            }
            else
            {
                dialog.Show();

                var tcs = new TaskCompletionSource();
                dialog.Closed += (_, _) => tcs.TrySetResult();
                await tcs.Task;
            }

            input.Text = string.Empty;
            return result;
        }

        public bool IsNewDeviceProtectionEnabled
        {
            get => _defenceSettings?.GetRuleEnabled("new-device") ?? true;
            set
            {
                _defenceSettings?.SetRuleEnabled("new-device", value);
                this.RaisePropertyChanged();
            }
        }

        public bool IsIntegritySafeModeEnabled
        {
            get => _defenceSettings?.GetRuleEnabled("integrity-critical") ?? true;
            set
            {
                _defenceSettings?.SetRuleEnabled("integrity-critical", value);
                this.RaisePropertyChanged();
            }
        }

        public bool IsClipboardGuardEnabled
        {
            get => _defenceSettings?.GetRuleEnabled("clipboard-guard") ?? true;
            set
            {
                _defenceSettings?.SetRuleEnabled("clipboard-guard", value);
                this.RaisePropertyChanged();
            }
        }

        public bool IsExportGuardEnabled
        {
            get => _defenceSettings?.GetRuleEnabled("excessive-exports") ?? true;
            set
            {
                _defenceSettings?.SetRuleEnabled("excessive-exports", value);
                this.RaisePropertyChanged();
            }
        }

        public bool IsBehaviourDeviationProtectionEnabled
        {
            get => _defenceSettings?.GetRuleEnabled("behavior-deviation") ?? true;
            set
            {
                _defenceSettings?.SetRuleEnabled("behavior-deviation", value);
                this.RaisePropertyChanged();
            }
        }

        public bool AutoActivateDecoyOnTamper
        {
            get => _autoActivateDecoyOnTamper;
            set
            {
                if (_autoActivateDecoyOnTamper != value)
                {
                    this.RaiseAndSetIfChanged(ref _autoActivateDecoyOnTamper, value);
                    this.RaisePropertyChanged(nameof(EnableDecoyVault));
                    SaveDecoySettings();
                }
            }
        }

        public int DecoyCredentialCount
        {
            get => _decoyCredentialCount;
            set
            {
                if (_decoyCredentialCount != value)
                {
                    this.RaiseAndSetIfChanged(ref _decoyCredentialCount, value);
                    SaveDecoySettings();
                }
            }
        }

        public bool LogDecoyActivation
        {
            get => _logDecoyActivation;
            set
            {
                if (_logDecoyActivation != value)
                {
                    this.RaiseAndSetIfChanged(ref _logDecoyActivation, value);
                    this.RaisePropertyChanged(nameof(DecoyLogActivations));
                    SaveDecoySettings();
                }
            }
        }

        public bool EnableDecoyVault
        {
            get => AutoActivateDecoyOnTamper;
            set => AutoActivateDecoyOnTamper = value;
        }

        public bool DecoyReadOnlyMode
        {
            get => _decoyReadOnlyMode;
            set
            {
                if (_decoyReadOnlyMode != value)
                {
                    this.RaiseAndSetIfChanged(ref _decoyReadOnlyMode, value);
                    SaveDecoySettings();
                }
            }
        }

        public bool DecoyLogActivations
        {
            get => LogDecoyActivation;
            set => LogDecoyActivation = value;
        }

        public bool IsDecoyCurrentlyActive
        {
            get => _isDecoyCurrentlyActive;
            private set => this.RaiseAndSetIfChanged(ref _isDecoyCurrentlyActive, value);
        }

        public ReactiveCommand<Unit, Unit> PreviewDecoyVaultCommand { get; }
        public ReactiveCommand<Unit, Unit> DeactivateDecoyCommand { get; }

        private void SaveDecoySettings()
        {
            _securityOptions.AutoActivateDecoyOnTamper = AutoActivateDecoyOnTamper;
            _securityOptions.DecoyCredentialCount = DecoyCredentialCount;
            _securityOptions.LogDecoyActivation = LogDecoyActivation;

            StageAll();
        }

        private static void PersistSettings(Action<UserSettings> update)
        {
            SettingsService.Update(update);
        }

        private SecurityBaseline SnapshotCurrent() => new SecurityBaseline
        {
            EnablePinLock = EnablePinLock,
            UsePinLockForAutoLock = UsePinLockForAutoLock,
            RequireHardwareToken = RequireHardwareToken,
            RequireKeyfile = RequireKeyfile,
            IdleTimeoutMinutes = IdleTimeoutMinutes,
            AutoCopyTotpWithPassword = AutoCopyTotpWithPassword,
            ClipboardClearTimeIndex = ClipboardClearTimeIndex,
            EnableScreenshotProtection = EnableScreenshotProtection,
            AutoActivateDecoyOnTamper = AutoActivateDecoyOnTamper,
            DecoyCredentialCount = DecoyCredentialCount,
            DecoyReadOnlyMode = DecoyReadOnlyMode,
            LogDecoyActivation = LogDecoyActivation,
        };

        private bool MatchesBaseline()
        {
            var b = _baseline;
            return EnablePinLock == b.EnablePinLock
                && UsePinLockForAutoLock == b.UsePinLockForAutoLock
                && RequireHardwareToken == b.RequireHardwareToken
                && RequireKeyfile == b.RequireKeyfile
                && IdleTimeoutMinutes == b.IdleTimeoutMinutes
                && AutoCopyTotpWithPassword == b.AutoCopyTotpWithPassword
                && ClipboardClearTimeIndex == b.ClipboardClearTimeIndex
                && EnableScreenshotProtection == b.EnableScreenshotProtection
                && AutoActivateDecoyOnTamper == b.AutoActivateDecoyOnTamper
                && DecoyCredentialCount == b.DecoyCredentialCount
                && DecoyReadOnlyMode == b.DecoyReadOnlyMode
                && LogDecoyActivation == b.LogDecoyActivation;
        }

        private void StageAll()
        {
            if (_suppressStage) return;
            if (MatchesBaseline())
            {
                _draft.ClearKey("Security.All");
                return;
            }
            var staged = SnapshotCurrent();
            var baseline = _baseline;
            _draft.Stage(
                key: "Security.All",
                commit: () =>
                {
                    SettingsService.Update(s =>
                    {
                        s.EnablePinLock = staged.EnablePinLock;
                        s.UsePinLockForAutoLock = staged.UsePinLockForAutoLock;
                        s.RequireHardwareToken = staged.RequireHardwareToken;
                        s.RequireKeyfile = staged.RequireKeyfile;
                        s.IdleTimeoutMinutes = staged.IdleTimeoutMinutes;
                        s.AutoCopyTotpWithPassword = staged.AutoCopyTotpWithPassword;
                        s.ClipboardClearTime = staged.ClipboardClearTimeIndex;
                        s.EnableScreenshotProtection = staged.EnableScreenshotProtection;
                        s.EnableDecoyVault = staged.AutoActivateDecoyOnTamper;
                        s.DecoyCredentialCount = staged.DecoyCredentialCount;
                        s.DecoyReadOnlyMode = staged.DecoyReadOnlyMode;
                        s.DecoyLogActivations = staged.LogDecoyActivation;
                    });
                    _baseline = staged;
                },
                discard: () =>
                {
                    _suppressStage = true;
                    try
                    {
                        EnablePinLock = baseline.EnablePinLock;
                        UsePinLockForAutoLock = baseline.UsePinLockForAutoLock;
                        RequireHardwareToken = baseline.RequireHardwareToken;
                        RequireKeyfile = baseline.RequireKeyfile;
                        IdleTimeoutMinutes = baseline.IdleTimeoutMinutes;
                        AutoCopyTotpWithPassword = baseline.AutoCopyTotpWithPassword;
                        ClipboardClearTimeIndex = baseline.ClipboardClearTimeIndex;
                        EnableScreenshotProtection = baseline.EnableScreenshotProtection;
                        AutoActivateDecoyOnTamper = baseline.AutoActivateDecoyOnTamper;
                        DecoyCredentialCount = baseline.DecoyCredentialCount;
                        DecoyReadOnlyMode = baseline.DecoyReadOnlyMode;
                        LogDecoyActivation = baseline.LogDecoyActivation;
                    }
                    finally
                    {
                        _suppressStage = false;
                    }
                });
        }

        private async Task PreviewDecoyVaultAsync()
        {
            if (_decoyVaultService == null)
            {
                await _dialogService.ShowErrorAsync(
                    "Preview Unavailable",
                    "Decoy vault service is not available.",
                    GetOwnerWindow());
                return;
            }

            try
            {
                IsBusy = true;

                var decoyDb = await _decoyVaultService.ActivateDecoyVaultAsync();

                var previewWindow = new DecoyPreviewWindow
                {
                    DataContext = new DecoyPreviewViewModel(decoyDb, _decoyVaultService)
                };

                var owner = GetOwnerWindow();
                if (owner != null)
                {
                    await previewWindow.ShowDialog(owner);
                }
                else
                {
                    previewWindow.Show();
                }

                _decoyVaultService.DeactivateDecoyVault();
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    "Preview Failed",
                    $"Failed to generate decoy vault preview: {ex.Message}",
                    GetOwnerWindow());
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task DeactivateDecoyAsync()
        {
            if (_decoyVaultService == null || _tamperDetectionService == null)
                return;

            var confirmed = await _dialogService.ShowConfirmationAsync(
                "Deactivate Decoy Vault",
                "Are you sure you want to deactivate the decoy vault and return to the real vault?\n\n" +
                "This will require re-authentication and should only be done after resolving the security issue.",
                GetOwnerWindow());

            if (!confirmed)
                return;

            try
            {
                IsBusy = true;

                _decoyVaultService.DeactivateDecoyVault();
                IsDecoyCurrentlyActive = false;

                _tamperDetectionService.StopMonitoring();
                _tamperDetectionService.StartMonitoring();

                await _dialogService.ShowSuccessAsync(
                    "Decoy Deactivated",
                    "Decoy vault has been deactivated. Real vault restored.\n\n" +
                    "Please verify that the security issue has been resolved.",
                    GetOwnerWindow());
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    "Deactivation Failed",
                    $"Failed to deactivate decoy vault: {ex.Message}",
                    GetOwnerWindow());
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}

