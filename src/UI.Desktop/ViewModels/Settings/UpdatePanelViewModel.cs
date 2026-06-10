using System;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using PhantomVault.Core.Services.Update;
using PhantomVault.UI.Services;
using Serilog;

namespace PhantomVault.UI.ViewModels.Settings
{
    /// <summary>
    /// Self-contained "Software updates" panel. Drives the verified
    /// check → download → stage → hand-off pipeline through <see cref="IUpdateService"/>.
    ///
    /// <para>
    /// Fails closed: while the embedded Ed25519 update-signing key is still the
    /// all-zero placeholder (<see cref="UpdatePublicKey.IsProvisioned"/> == false),
    /// the entire panel is disabled and clearly labelled as not-yet-enabled. This
    /// honours the rule that update entry points stay dark until the signing key
    /// is actually provisioned, so an unprovisioned build can never be coaxed into
    /// accepting an attacker-signed manifest.
    /// </para>
    /// </summary>
    public sealed class UpdatePanelViewModel : ReactiveObject, IDisposable
    {
        private readonly IUpdateService? _updateService;
        private bool _disposed;
        private string _statusText = "Idle";
        private string? _detailText;

        public UpdatePanelViewModel()
        {
            _updateService = (Avalonia.Application.Current as App)?.Services
                ?.GetService(typeof(IUpdateService)) as IUpdateService;

            CheckForUpdatesCommand = ReactiveCommand.CreateFromTask(CheckForUpdatesAsync);
            DownloadUpdateCommand = ReactiveCommand.CreateFromTask(DownloadUpdateAsync);
            RestartAndInstallCommand = ReactiveCommand.Create(RestartAndInstall);

            try
            {
                _autoCheckEnabled = SettingsService.Load().UpdateAutoCheckEnabled;
            }
            catch
            {
                _autoCheckEnabled = false;
            }

            if (!IsProvisioned)
            {
                _statusText = "Secure updates are not enabled in this build";
                _detailText = "The update-signing key has not been provisioned yet. Updates stay disabled until it is, so no unsigned or attacker-signed package can ever be applied.";
            }
            else if (_updateService is null)
            {
                _statusText = "Updates unavailable in this build";
            }
            else
            {
                _updateService.StateChanged += OnUpdateStateChanged;
                RefreshState();
            }
        }

        /// <summary>True only when the embedded Ed25519 key is real (not the placeholder).</summary>
        public bool IsProvisioned => UpdatePublicKey.IsProvisioned;

        /// <summary>The panel is fully usable only when provisioned AND the service resolved.</summary>
        public bool IsUpdateAvailable => IsProvisioned && _updateService is not null;

        public string VerificationSummary =>
            "Every update is verified before it is applied: an Ed25519 signature proves the release is authentic, a SHA-256 hash proves the download is intact, and (on Windows) the Authenticode signature and pinned certificate prove the binary is ours.";

        public string StatusText
        {
            get => _statusText;
            private set => this.RaiseAndSetIfChanged(ref _statusText, value);
        }

        public string? DetailText
        {
            get => _detailText;
            private set => this.RaiseAndSetIfChanged(ref _detailText, value);
        }

        public bool CanCheckForUpdates => IsUpdateAvailable
            && _updateService!.State is UpdateState.Idle or UpdateState.UpToDate
                or UpdateState.UpdateAvailable or UpdateState.Failed;

        public bool CanDownloadUpdate => IsUpdateAvailable
            && _updateService!.State == UpdateState.UpdateAvailable;

        public bool CanRestartAndInstall => IsUpdateAvailable
            && _updateService!.State == UpdateState.Staged;

        private bool _autoCheckEnabled;
        public bool AutoCheckEnabled
        {
            get => _autoCheckEnabled;
            set
            {
                if (_autoCheckEnabled == value) return;
                this.RaiseAndSetIfChanged(ref _autoCheckEnabled, value);
                try
                {
                    SettingsService.Update(s => s.UpdateAutoCheckEnabled = value);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to persist UpdateAutoCheckEnabled toggle");
                }
            }
        }

        public ReactiveCommand<Unit, Unit> CheckForUpdatesCommand { get; }
        public ReactiveCommand<Unit, Unit> DownloadUpdateCommand { get; }
        public ReactiveCommand<Unit, Unit> RestartAndInstallCommand { get; }

        private void OnUpdateStateChanged(object? sender, UpdateStateChangedEventArgs e) => RefreshState();

        private void RefreshState()
        {
            if (_updateService is null) return;
            StatusText = _updateService.State switch
            {
                UpdateState.Idle => "Up to date — last checked just now",
                UpdateState.Checking => "Checking for updates…",
                UpdateState.UpToDate => "Up to date",
                UpdateState.UpdateAvailable => $"Update available: {_updateService.LatestInfo?.Version}",
                UpdateState.Downloading => "Downloading…",
                UpdateState.Verifying => "Verifying signature and hash…",
                UpdateState.Staged => $"Ready to install: {_updateService.LatestInfo?.Version}",
                UpdateState.Failed => "Update failed",
                _ => _updateService.State.ToString(),
            };
            DetailText = _updateService.State == UpdateState.Failed
                ? _updateService.LastFailureMessage
                : null;
            this.RaisePropertyChanged(nameof(CanCheckForUpdates));
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
            if (_updateService is null || _updateService.State != UpdateState.Staged) return;

            var processPath = Environment.ProcessPath;
            var targetDir = string.IsNullOrEmpty(processPath) ? null : Path.GetDirectoryName(processPath);
            if (string.IsNullOrEmpty(targetDir))
            {
                DetailText = "Cannot determine install directory.";
                return;
            }

            var installerExe = Path.Combine(targetDir, "Giblex.Installer.exe");
            if (!File.Exists(installerExe))
            {
                DetailText = "Giblex Installer not found beside this app — cannot apply update.";
                return;
            }

            try
            {
                _updateService.HandOffToInstaller(installerExe, targetDir, processPath);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Hand-off to installer threw");
                DetailText = "Could not start the installer.";
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_updateService is not null)
            {
                _updateService.StateChanged -= OnUpdateStateChanged;
            }
        }
    }
}
