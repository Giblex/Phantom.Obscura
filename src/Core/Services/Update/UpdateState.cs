namespace PhantomVault.Core.Services.Update
{
    /// <summary>
    /// Lifecycle of a single update attempt. The service starts in
    /// <see cref="Idle"/>, transitions forward through the pipeline, and lands
    /// in one of three terminal-ish states: <see cref="UpToDate"/> (no work
    /// needed), <see cref="Staged"/> (ready for the Giblex Installer to apply
    /// on next launch), or <see cref="Failed"/> (with a reason on the service).
    /// Calling <see cref="IUpdateService.Reset"/> returns to <see cref="Idle"/>.
    /// </summary>
    public enum UpdateState
    {
        Idle = 0,
        Checking = 1,
        UpToDate = 2,
        UpdateAvailable = 3,
        Downloading = 4,
        Verifying = 5,
        Staged = 6,
        Failed = 7,
    }
}
