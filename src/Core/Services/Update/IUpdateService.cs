using System;
using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services.Update
{
    /// <summary>
    /// Orchestrates the check → download → verify → stage pipeline. Does NOT
    /// apply the update — that is the Giblex Installer's job (see Phase C,
    /// invoked with <c>--apply-update --staged &lt;dir&gt; --target &lt;dir&gt; --pid &lt;n&gt;</c>).
    ///
    /// <para>
    /// Trust boundary: the manifest's Ed25519 signature is the sole authority
    /// for "this update is real". The asset hash, host pin, channel, and
    /// monotonic version are derived facts from that signature.
    /// </para>
    /// </summary>
    public interface IUpdateService
    {
        UpdateState State { get; }

        /// <summary>The latest verified manifest from a successful check. Null otherwise.</summary>
        UpdateInfo? LatestInfo { get; }

        /// <summary>Directory containing the staged update after <see cref="DownloadAndStageAsync"/> succeeds.</summary>
        string? StagedDirectory { get; }

        /// <summary>Reason for the most recent <see cref="UpdateState.Failed"/> transition.</summary>
        UpdateVerificationFailure? LastFailureReason { get; }

        /// <summary>Human-readable diagnostic for the most recent failure (safe to surface to the user).</summary>
        string? LastFailureMessage { get; }

        event EventHandler<UpdateStateChangedEventArgs>? StateChanged;

        /// <summary>
        /// Fetch and verify the manifest. Drives the state to
        /// <see cref="UpdateState.UpdateAvailable"/>, <see cref="UpdateState.UpToDate"/>,
        /// or <see cref="UpdateState.Failed"/>. Returns the verified info on success,
        /// or null when up-to-date / failed.
        /// </summary>
        Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Download the asset for <paramref name="info"/> and stage it (with a
        /// re-validated <c>info.json</c> + raw manifest + signature) for the
        /// Giblex Installer to apply on next launch. Drives state through
        /// <see cref="UpdateState.Downloading"/> → <see cref="UpdateState.Verifying"/>
        /// → <see cref="UpdateState.Staged"/> or <see cref="UpdateState.Failed"/>.
        /// </summary>
        /// <param name="info">An UpdateInfo previously returned by <see cref="CheckAsync"/>.</param>
        /// <param name="progress">Optional 0..1 download progress callback.</param>
        /// <param name="cancellationToken">Cancels the download mid-stream.</param>
        Task<string?> DownloadAndStageAsync(
            UpdateInfo info,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>Return to <see cref="UpdateState.Idle"/>, clearing failure info but keeping <see cref="LatestInfo"/>.</summary>
        void Reset();

        /// <summary>
        /// Hand the staged update off to the Giblex Installer, then shut down so
        /// the installer can swap our install directory. Caller is expected to
        /// exit the process shortly after this returns (the installer waits on
        /// the supplied PID before touching <paramref name="targetDir"/>).
        ///
        /// <para>
        /// Requires <see cref="State"/> == <see cref="UpdateState.Staged"/> and
        /// <see cref="StagedDirectory"/> non-null; otherwise throws.
        /// </para>
        /// </summary>
        /// <param name="installerExePath">Absolute path to the deployed Giblex.Installer.exe.</param>
        /// <param name="targetDir">Absolute path to the install directory the installer should replace.</param>
        /// <param name="relaunchExe">Optional absolute path to the executable the installer should relaunch after the swap.</param>
        void HandOffToInstaller(string installerExePath, string targetDir, string? relaunchExe = null);
    }

    public sealed class UpdateStateChangedEventArgs : EventArgs
    {
        public required UpdateState OldState { get; init; }
        public required UpdateState NewState { get; init; }
    }
}
