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
        private string? _manifestPath; // Track current vault's manifest path

        // Decoy vault properties
        private bool _autoActivateDecoyOnTamper = true;
        private int _decoyCredentialCount = 25;
        private bool _logDecoyActivation = true;
        private bool _isDecoyCurrentlyActive;

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

            var settings = SettingsService.Load();
            _enablePinLock = settings.EnablePinLock;
            _usePinLockForAutoLock = settings.UsePinLockForAutoLock;
            _autoCopyTotpWithPassword = settings.AutoCopyTotpWithPassword;

            // Load decoy settings
            _autoActivateDecoyOnTamper = _securityOptions.AutoActivateDecoyOnTamper;
            _decoyCredentialCount = _securityOptions.DecoyCredentialCount;
            _logDecoyActivation = _securityOptions.LogDecoyActivation;
            _isDecoyCurrentlyActive = _tamperDetectionService?.IsDecoyActive ?? false;

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

                // Persist simple settings used by the UI.
                var settings = SettingsService.Load();
                settings.EnablePinLock = EnablePinLock;
                settings.UsePinLockForAutoLock = UsePinLockForAutoLock;
                SettingsService.Save(settings);
                
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
            set => this.RaiseAndSetIfChanged(ref _requireHardwareToken, value);
        }

        public bool RequireKeyfile
        {
            get => _requireKeyfile;
            set => this.RaiseAndSetIfChanged(ref _requireKeyfile, value);
        }

        public int IdleTimeoutMinutes
        {
            get => _idleTimeoutMinutes;
            set => this.RaiseAndSetIfChanged(ref _idleTimeoutMinutes, value);
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
                // Persist immediately so lockscreen behavior is consistent.
                var settings = SettingsService.Load();
                settings.EnablePinLock = value;
                if (!value)
                {
                    settings.UsePinLockForAutoLock = false;
                    this.RaiseAndSetIfChanged(ref _usePinLockForAutoLock, false);
                }
                SettingsService.Save(settings);
            }
        }

        public bool UsePinLockForAutoLock
        {
            get => _usePinLockForAutoLock;
            set
            {
                this.RaiseAndSetIfChanged(ref _usePinLockForAutoLock, value);
                var settings = SettingsService.Load();
                settings.UsePinLockForAutoLock = value;
                SettingsService.Save(settings);
            }
        }

        public bool AutoCopyTotpWithPassword
        {
            get => _autoCopyTotpWithPassword;
            set
            {
                this.RaiseAndSetIfChanged(ref _autoCopyTotpWithPassword, value);
                var settings = SettingsService.Load();
                settings.AutoCopyTotpWithPassword = value;
                SettingsService.Save(settings);
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
            set => this.RaiseAndSetIfChanged(ref _autoActivateDecoyOnTamper, value);
        }

        public int DecoyCredentialCount
        {
            get => _decoyCredentialCount;
            set => this.RaiseAndSetIfChanged(ref _decoyCredentialCount, value);
        }

        public bool LogDecoyActivation
        {
            get => _logDecoyActivation;
            set => this.RaiseAndSetIfChanged(ref _logDecoyActivation, value);
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

            // Persist to defence-rules.json or similar storage
            // For now, SecurityOptions are passed by DI and should be persisted by the application
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