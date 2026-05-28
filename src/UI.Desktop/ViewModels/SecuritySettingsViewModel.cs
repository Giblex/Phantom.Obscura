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
using PhantomVault.UI.Views.Dialogs;
using PhantomVault.UI.Views;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model for the security settings window. Allows the user to
    /// configure advanced security parameters such as hardware token
    /// requirements, keyfile usage and idle timeout. Saving the settings
    /// triggers an action that could persist them to disk or apply them
    /// immediately.
    /// </summary>
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
        private string? _manifestPath; // Track current vault's manifest path

        // Decoy vault properties
        private bool _autoActivateDecoyOnTamper = true;
        private int _decoyCredentialCount = 25;
        private bool _decoyReadOnlyMode = true;
        private bool _logDecoyActivation = true;
        private bool _isDecoyCurrentlyActive;

        // Issue #28: shared draft tracker so the overlay-bottom Save/Discard
        // buttons drive Security persistence too. Baseline snapshot captures
        // on-disk state at ctor for Discard rollback. _suppressStage guards
        // initial-load + discard-path property writes from triggering staging.
        private readonly SettingsDraftTracker _draft;
        private SecurityBaseline _baseline;
        private bool _suppressStage;

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
            SecurityOptions? securityOptions = null)
        {
            _defenceSettings = defenceSettingsService;
            _dialogService = new DialogService();
            _manifestPath = manifestPath;
            _decoyVaultService = decoyVaultService;
            _tamperDetectionService = tamperDetectionService;
            _securityOptions = securityOptions ?? new SecurityOptions();
            // Issue #28: resolve singleton tracker (with fallback for non-DI
            // construction paths so tests/manual instantiation keep working).
            _draft = ((Avalonia.Application.Current as App)?.Services?.GetService(typeof(SettingsDraftTracker)) as SettingsDraftTracker)
                ?? new SettingsDraftTracker();
            _suppressStage = true;

            var settings = SettingsService.LoadSecuritySnapshot();
            _enablePinLock = settings.EnablePinLock;
            _usePinLockForAutoLock = settings.UsePinLockForAutoLock;
            _autoCopyTotpWithPassword = settings.AutoCopyTotpWithPassword;
            _requireHardwareToken = settings.RequireHardwareToken;
            _requireKeyfile = settings.RequireKeyfile;
            _idleTimeoutMinutes = settings.IdleTimeoutMinutes;
            _clipboardClearTimeIndex = settings.ClipboardClearTime;
            _enableScreenshotProtection = settings.EnableScreenshotProtection;

            // Load decoy settings
            _autoActivateDecoyOnTamper = settings.EnableDecoyVault;
            _decoyCredentialCount = settings.DecoyCredentialCount;
            _decoyReadOnlyMode = settings.DecoyReadOnlyMode;
            _logDecoyActivation = settings.DecoyLogActivations;
            _isDecoyCurrentlyActive = _tamperDetectionService?.IsDecoyActive ?? false;

            // Snapshot the just-loaded values as the baseline for Discard.
            _baseline = SnapshotCurrent();
            _suppressStage = false;

            SaveCommand = ReactiveCommand.CreateFromTask(SaveSettingsAsync,
                this.WhenAnyValue(vm => vm.IsBusy).Select(b => !b));

            SetOrChangePinCommand = ReactiveCommand.CreateFromTask(SetOrChangePinAsync,
                this.WhenAnyValue(vm => vm.IsBusy).Select(b => !b));

            ClearPinCommand = ReactiveCommand.CreateFromTask(ClearPinAsync,
                this.WhenAnyValue(vm => vm.IsBusy).Select(b => !b));

            PreviewDecoyVaultCommand = ReactiveCommand.CreateFromTask(PreviewDecoyVaultAsync,
                this.WhenAnyValue(vm => vm.IsBusy, vm => vm.AutoActivateDecoyOnTamper).Select(x => !x.Item1 && x.Item2));

            DeactivateDecoyCommand = ReactiveCommand.CreateFromTask(DeactivateDecoyAsync,
                this.WhenAnyValue(vm => vm.IsBusy, vm => vm.IsDecoyCurrentlyActive).Select(x => !x.Item1 && x.Item2));

            // Auto-save decoy settings on change
            this.WhenAnyValue(
                x => x.AutoActivateDecoyOnTamper,
                x => x.DecoyCredentialCount,
                x => x.LogDecoyActivation)
                .Throttle(TimeSpan.FromMilliseconds(500))
                .Subscribe(_ => SaveDecoySettings());

            // Subscribe to tamper detection events
            if (_tamperDetectionService != null)
            {
                _tamperDetectionService.TamperDetected += OnTamperDetected;
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
                // Simulate save operation
                await Task.Delay(300);

                // Issue #28: clicking the tab's local Save commits the staged
                // 'Security.All' entry (and anything else staged across the
                // overlay — same semantics as the overlay-bottom Save). If
                // nothing is staged (settings already match baseline) this is
                // a no-op rather than a duplicate write.
                _draft.CommitAll();

                LastSaved = DateTimeOffset.UtcNow;
                SaveButtonText = "✓ Saved";

                // Reset button text after 2 seconds
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
                if (this.RaiseAndSetIfChanged(ref _requireHardwareToken, value))
                {
                    StageAll();
                }
            }
        }

        public bool RequireKeyfile
        {
            get => _requireKeyfile;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _requireKeyfile, value))
                {
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

        public bool EnablePinLock
        {
            get => _enablePinLock;
            set
            {
                this.RaiseAndSetIfChanged(ref _enablePinLock, value);
                // Issue #28: if PIN-lock is being disabled, cascade-clear the
                // related toggle in-memory so the user sees the dependent
                // switch flip immediately. Persistence waits for Save.
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
                if (this.RaiseAndSetIfChanged(ref _enableScreenshotProtection, value))
                {
                    // Persist immediately — this is a live-effect security
                    // toggle (the SettingsChanged event drives the window's
                    // SetWindowDisplayAffinity call in VaultWindow), so the
                    // user's expectation is that flipping the switch takes
                    // effect now, not after clicking a Save button buried in
                    // the overlay. We also rebase so the draft tracker
                    // doesn't keep the value marked as "unsaved" and won't
                    // re-stage it (which would clobber any other genuinely
                    // staged fields on this tab).
                    SettingsService.Update(s => s.EnableScreenshotProtection = value);
                    _baseline = _baseline with { EnableScreenshotProtection = value };
                    StageAll();
                }
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
                await _dialogService.ShowSuccessAsync("PIN Disabled", "PIN lock has been disabled.", GetOwnerWindow());
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("PIN Disable Failed", ex.Message, GetOwnerWindow());
            }
        }

        private Window? GetOwnerWindow()
        {
            // Settings panel can be hosted inside VaultWindow overlay; best-effort owner discovery.
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
                // Wait until the dialog closes.
                var tcs = new TaskCompletionSource();
                dialog.Closed += (_, _) => tcs.TrySetResult();
                await tcs.Task;
            }

            input.Text = string.Empty;
            return result;
        }

        // ===== Defence Engine Settings =====

        /// <summary>
        /// Require Phantom Key / extra checks on new devices.
        /// Maps to "new-device" rule.
        /// </summary>
        public bool IsNewDeviceProtectionEnabled
        {
            get => _defenceSettings?.GetRuleEnabled("new-device") ?? true;
            set
            {
                _defenceSettings?.SetRuleEnabled("new-device", value);
                this.RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Open in Safe (read-only) mode if tampering suspected.
        /// Maps to "integrity-critical" rule.
        /// </summary>
        public bool IsIntegritySafeModeEnabled
        {
            get => _defenceSettings?.GetRuleEnabled("integrity-critical") ?? true;
            set
            {
                _defenceSettings?.SetRuleEnabled("integrity-critical", value);
                this.RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Limit rapid copy-to-clipboard of secrets.
        /// This is a conceptual flag for clipboard guard behavior.
        /// </summary>
        public bool IsClipboardGuardEnabled
        {
            get => _defenceSettings?.GetRuleEnabled("clipboard-guard") ?? true;
            set
            {
                _defenceSettings?.SetRuleEnabled("clipboard-guard", value);
                this.RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Limit repeated vault exports in short time.
        /// Maps to "excessive-exports" rule.
        /// </summary>
        public bool IsExportGuardEnabled
        {
            get => _defenceSettings?.GetRuleEnabled("excessive-exports") ?? true;
            set
            {
                _defenceSettings?.SetRuleEnabled("excessive-exports", value);
                this.RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Detect and respond to abnormal session behaviour.
        /// Maps to "behavior-deviation" rule.
        /// </summary>
        public bool IsBehaviourDeviationProtectionEnabled
        {
            get => _defenceSettings?.GetRuleEnabled("behavior-deviation") ?? true;
            set
            {
                _defenceSettings?.SetRuleEnabled("behavior-deviation", value);
                this.RaisePropertyChanged();
            }
        }

        // ===== Decoy Vault Settings =====

        public bool AutoActivateDecoyOnTamper
        {
            get => _autoActivateDecoyOnTamper;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _autoActivateDecoyOnTamper, value))
                {
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
                if (this.RaiseAndSetIfChanged(ref _logDecoyActivation, value))
                {
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
                if (this.RaiseAndSetIfChanged(ref _decoyReadOnlyMode, value))
                {
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
            // Issue #28: route through the shared tracker so the overlay Save
            // button handles persistence. Live SecurityOptions update above
            // still fires so any in-process consumers see the new value.
            StageAll();
        }

        private static void PersistSettings(Action<UserSettings> update)
        {
            SettingsService.Update(update);
        }

        // Issue #28: snapshot current property values for baseline + commit
        // payload.
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

        // Issue #28: re-staging the same key replaces the prior pair so a
        // burst of property changes still results in a single commit/discard.
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

                // Generate preview decoy vault
                var decoyDb = await _decoyVaultService.ActivateDecoyVaultAsync();

                // Show preview window
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

                // Deactivate after preview
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

                // Deactivate decoy
                _decoyVaultService.DeactivateDecoyVault();
                IsDecoyCurrentlyActive = false;

                // Restart tamper detection monitoring
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
