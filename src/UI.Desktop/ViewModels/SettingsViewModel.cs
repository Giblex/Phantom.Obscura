using System;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using ReactiveUI;
using PhantomVault.Core.Services.Security;
using PhantomVault.Core.Services.Update;
using PhantomVault.UI.Services;
using PhantomVault.UI.ViewModels.Settings;
using Serilog;

namespace PhantomVault.UI.ViewModels
{

    public sealed class SettingsViewModel : ReactiveObject
    {
        private readonly IDefenceSettingsService? _defenceSettings;
        private readonly IUpdateService? _updateService;
        private readonly UpdateChannelSettings? _updateSettings;
        private bool _isDarkTheme = false;
        private string _updateStatusText = "Idle";
        private string? _updateDetailText;

        public SettingsViewModel(
            IDefenceSettingsService? defenceSettingsService = null,
            RecoveryActivationSettingsViewModel? recoveryActivationSettingsViewModel = null,
            IUpdateService? updateService = null,
            UpdateChannelSettings? updateSettings = null)
        {
            _defenceSettings = defenceSettingsService;
            _updateService = updateService;
            _updateSettings = updateSettings;
            _isDarkTheme = SettingsService.Load().IsDarkTheme;

            PasswordSecurityViewModel = new PasswordSecurityViewModel();
            RecoveryActivationSettingsViewModel = recoveryActivationSettingsViewModel
                ?? new RecoveryActivationSettingsViewModel(new RecoveryActivationPolicyStore());

            ToggleThemeCommand = ReactiveCommand.Create(() =>
            {
                IsDarkTheme = !IsDarkTheme;

                try
                {
                    if (Avalonia.Application.Current is App app)
                    {
                        app.SetTheme(IsDarkTheme ? "dark" : "light");
                        var settings = SettingsService.Load();
                        settings.IsDarkTheme = IsDarkTheme;
                        SettingsService.Save(settings);
                    }
                }
                catch
                {
                    ApplyTheme();
                }
            });

            CheckForUpdatesCommand = ReactiveCommand.CreateFromTask(CheckForUpdatesAsync);
            DownloadUpdateCommand = ReactiveCommand.CreateFromTask(DownloadUpdateAsync);
            RestartAndInstallCommand = ReactiveCommand.Create(RestartAndInstall);

            if (_updateService is not null)
            {
                _updateService.StateChanged += OnUpdateStateChanged;
                RefreshUpdateState();
            }
            else
            {
                UpdateStatusText = "Updates unavailable in this build";
            }
        }

        public PasswordSecurityViewModel PasswordSecurityViewModel { get; }

        public RecoveryActivationSettingsViewModel RecoveryActivationSettingsViewModel { get; }

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set => this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
        }

        public ReactiveCommand<Unit, Unit> ToggleThemeCommand { get; }

        private void ApplyTheme()
        {
            if (Application.Current == null) return;
            Application.Current.RequestedThemeVariant = IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
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

        public bool IsRecoveryDeveloperModeEnabled
        {
            get => RecoveryDeveloperMode.IsEnabled;
            set
            {
                RecoveryDeveloperMode.IsEnabled = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(RecoveryDeveloperVaultPath));
                this.RaisePropertyChanged(nameof(RecoveryDeveloperUsbPath));
            }
        }

        public string RecoveryDeveloperVaultPath => RecoveryDeveloperMode.DeveloperVaultPath;

        public string RecoveryDeveloperUsbPath => RecoveryDeveloperMode.DeveloperUsbPath;

        public bool SkipUsbBindingCheck
        {
            get => RecoveryDeveloperMode.SkipUsbBindingCheck;
            set
            {
                RecoveryDeveloperMode.SkipUsbBindingCheck = value;
                this.RaisePropertyChanged();
            }
        }

        public bool RecoveryVerboseLogging
        {
            get => RecoveryDeveloperMode.VerboseLogging;
            set
            {
                RecoveryDeveloperMode.VerboseLogging = value;
                this.RaisePropertyChanged();
            }
        }

        /// <summary>
        /// User toggle for "Check for updates automatically". Off by default so we
        /// never reach out unprompted. Persisted to user settings AND mirrored into
        /// the running <see cref="UpdateChannelSettings"/> singleton so the change
        /// takes effect without an app restart.
        /// </summary>
        public bool UpdateAutoCheckEnabled
        {
            get => _updateSettings?.AutoCheckEnabled ?? false;
            set
            {
                if (_updateSettings is null) return;
                if (_updateSettings.AutoCheckEnabled == value) return;
                _updateSettings.AutoCheckEnabled = value;
                try
                {
                    var us = SettingsService.Load();
                    us.UpdateAutoCheckEnabled = value;
                    SettingsService.Save(us);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to persist UpdateAutoCheckEnabled toggle");
                }
                this.RaisePropertyChanged();
            }
        }

        public bool IsUpdateServiceAvailable => _updateService is not null;

        public string UpdateStatusText
        {
            get => _updateStatusText;
            private set => this.RaiseAndSetIfChanged(ref _updateStatusText, value);
        }

        public string? UpdateDetailText
        {
            get => _updateDetailText;
            private set => this.RaiseAndSetIfChanged(ref _updateDetailText, value);
        }

        public bool CanDownloadUpdate => _updateService?.State == UpdateState.UpdateAvailable;
        public bool CanRestartAndInstall => _updateService?.State == UpdateState.Staged;

        public ReactiveCommand<Unit, Unit> CheckForUpdatesCommand { get; }
        public ReactiveCommand<Unit, Unit> DownloadUpdateCommand { get; }
        public ReactiveCommand<Unit, Unit> RestartAndInstallCommand { get; }

        private void OnUpdateStateChanged(object? sender, UpdateStateChangedEventArgs e)
        {
            // The service raises this on a worker thread; Reactive properties
            // notify via the same thread which is fine for Avalonia bindings
            // since they marshal to the UI thread automatically.
            RefreshUpdateState();
        }

        private void RefreshUpdateState()
        {
            if (_updateService is null) return;
            UpdateStatusText = _updateService.State switch
            {
                UpdateState.Idle => "Idle",
                UpdateState.Checking => "Checking…",
                UpdateState.UpToDate => "Up to date",
                UpdateState.UpdateAvailable => $"Update available: {_updateService.LatestInfo?.Version}",
                UpdateState.Downloading => "Downloading…",
                UpdateState.Verifying => "Verifying…",
                UpdateState.Staged => $"Ready to install: {_updateService.LatestInfo?.Version}",
                UpdateState.Failed => "Update failed",
                _ => _updateService.State.ToString(),
            };
            UpdateDetailText = _updateService.State == UpdateState.Failed
                ? _updateService.LastFailureMessage
                : null;
            this.RaisePropertyChanged(nameof(CanDownloadUpdate));
            this.RaisePropertyChanged(nameof(CanRestartAndInstall));
        }

        private async Task CheckForUpdatesAsync()
        {
            if (_updateService is null) return;
            try
            {
                await _updateService.CheckAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Manual update check threw");
            }
        }

        private async Task DownloadUpdateAsync()
        {
            if (_updateService is null) return;
            var info = _updateService.LatestInfo;
            if (info is null) return;
            try
            {
                await _updateService.DownloadAndStageAsync(info);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Download/stage threw");
            }
        }

        private void RestartAndInstall()
        {
            if (_updateService is null) return;
            if (_updateService.State != UpdateState.Staged) return;

            // Install layout: Giblex.Installer.exe lives next to PhantomVault.UI.exe.
            // In dev (run-dev.ps1) the installer isn't present — the button is hidden
            // in that case via CanRestartAndInstall, but we still guard.
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(processPath))
            {
                UpdateDetailText = "Cannot determine install directory.";
                return;
            }
            var targetDir = Path.GetDirectoryName(processPath);
            if (string.IsNullOrEmpty(targetDir))
            {
                UpdateDetailText = "Cannot determine install directory.";
                return;
            }
            var installerExe = Path.Combine(targetDir, "Giblex.Installer.exe");
            if (!File.Exists(installerExe))
            {
                UpdateDetailText = "Giblex Installer not found beside this app — cannot apply update.";
                return;
            }

            try
            {
                _updateService.HandOffToInstaller(installerExe, targetDir, processPath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HandOffToInstaller failed");
                UpdateDetailText = "Failed to launch the installer: " + ex.Message;
                return;
            }

            // Yield so the installer can attach to our PID before we exit.
            Environment.Exit(0);
        }
    }
}

