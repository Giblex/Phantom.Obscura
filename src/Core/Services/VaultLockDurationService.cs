using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Services.ZeroKnowledge;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Service for managing vault auto-lock functionality.
    /// Provides configurable lock durations and automatic vault locking.
    /// </summary>
    public class VaultLockDurationService : IDisposable
    {
        private Timer? _lockTimer;
        private LockDuration _currentDuration = LockDuration.FiveMinutes;
        private DateTime _lastActivityTime;
        private bool _isLocked = true;
        private readonly object _lockObject = new object();
        private readonly SemaphoreSlim _lockSemaphore = new SemaphoreSlim(1, 1);
        private readonly VaultService _vaultService;
        private readonly IZkVaultService _zkVaultService;
        private VaultLockSessionContext? _activeSession;

        public event EventHandler<VaultLockEventArgs>? VaultLocking;
        public event EventHandler<VaultLockEventArgs>? VaultLocked;
        public event EventHandler? ActivityDetected;

        public VaultLockDurationService(VaultService vaultService, IZkVaultService zkVaultService)
        {
            _vaultService = vaultService ?? throw new ArgumentNullException(nameof(vaultService));
            _zkVaultService = zkVaultService ?? throw new ArgumentNullException(nameof(zkVaultService));
        }

        /// <summary>
        /// Gets the current lock duration setting.
        /// </summary>
        public LockDuration CurrentDuration
        {
            get { lock (_lockObject) return _currentDuration; }
        }

        /// <summary>
        /// Gets whether the vault is currently locked.
        /// </summary>
        public bool IsLocked
        {
            get { lock (_lockObject) return _isLocked; }
        }

        /// <summary>
        /// Gets the time remaining until auto-lock (null if disabled or locked).
        /// </summary>
        public TimeSpan? TimeUntilLock
        {
            get
            {
                lock (_lockObject)
                {
                    if (_isLocked || _currentDuration == LockDuration.Never)
                        return null;

                    var durationMs = GetDurationMilliseconds(_currentDuration);
                    if (durationMs == Timeout.Infinite)
                        return null;

                    var elapsed = DateTime.UtcNow - _lastActivityTime;
                    var remaining = TimeSpan.FromMilliseconds(durationMs) - elapsed;
                    return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
                }
            }
        }

        /// <summary>
        /// Set the auto-lock duration and start/restart the timer.
        /// </summary>
        public void SetLockDuration(LockDuration duration)
        {
            lock (_lockObject)
            {
                _currentDuration = duration;

                if (_isLocked)
                    return; // Don't start timer if vault is locked

                StopTimer();

                if (duration == LockDuration.Never)
                    return; // No auto-lock

                if (duration == LockDuration.Immediate)
                {
                    // Lock immediately
                    _ = LockVaultAsync(LockReason.AutoLock);
                    return;
                }

                // Start new timer
                int durationMs = GetDurationMilliseconds(duration);
                _lastActivityTime = DateTime.UtcNow;
                _lockTimer = new Timer(OnTimerElapsed, null, durationMs, Timeout.Infinite);
            }
        }

        /// <summary>
        /// Unlock the vault and start the auto-lock timer.
        /// </summary>
        public void UnlockVault()
        {
            lock (_lockObject)
            {
                if (!_isLocked)
                    return; // Already unlocked

                _isLocked = false;
                _lastActivityTime = DateTime.UtcNow;

                // Start timer if duration is not Never or Immediate
                if (_currentDuration != LockDuration.Never && _currentDuration != LockDuration.Immediate)
                {
                    StopTimer();
                    int durationMs = GetDurationMilliseconds(_currentDuration);
                    _lockTimer = new Timer(OnTimerElapsed, null, durationMs, Timeout.Infinite);
                }
                else if (_currentDuration == LockDuration.Immediate)
                {
                    // If set to immediate, lock right away
                    _ = LockVaultAsync(LockReason.AutoLock);
                }
            }
        }

        /// <summary>
        /// Lock the vault immediately and stop the timer.
        /// </summary>
        public void LockVault()
        {
            _ = LockVaultAsync(LockReason.AutoLock);
        }

        /// <summary>
        /// Performs the secure lock sequence asynchronously.
        /// </summary>
        public async Task<VaultLockResult> LockVaultAsync(LockReason reason = LockReason.AutoLock, CancellationToken cancellationToken = default)
        {
            await _lockSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                VaultLockSessionContext? session;
                lock (_lockObject)
                {
                    if (_isLocked)
                    {
                        return VaultLockResult.Successful;
                    }

                    _isLocked = true;
                    StopTimer();
                    session = _activeSession;
                }

                VaultLocking?.Invoke(this, new VaultLockEventArgs
                {
                    LockTime = DateTime.UtcNow,
                    Reason = reason,
                    Result = VaultLockResult.Successful
                });

                Exception? cleanupError = null;

                async Task executeStep(Func<Task>? step)
                {
                    if (step == null)
                        return;

                    try
                    {
                        await step().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        if (cleanupError == null)
                        {
                            cleanupError = ex;
                        }
                        session?.OnCleanupFailed?.Invoke(ex);
                    }
                }

                // Flush pending writes before locking
                await executeStep(session?.FlushPendingWritesAsync).ConfigureAwait(false);

                // Wipe keys from zero-knowledge service
                await executeStep(() => _zkVaultService.LockAndWipeKeysAsync()).ConfigureAwait(false);

                // Dismount vault container
                await executeStep(async () =>
                {
                    if (!string.IsNullOrWhiteSpace(session?.MountPath))
                    {
                        var mountName = VaultLockDurationServiceHelpers.ResolveMountIdentifier(session.MountPath!);
                        if (!string.IsNullOrEmpty(mountName))
                        {
                            await _vaultService.DismountVaultAsync(mountName!, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }).ConfigureAwait(false);

                // Release any additional handles
                await executeStep(session?.ReleaseHandlesAsync).ConfigureAwait(false);

                // Kill residual processes if provided
                await executeStep(session?.KillResidualProcessesAsync).ConfigureAwait(false);

                try
                {
                    session?.ZeroizeSensitiveData?.Invoke();
                }
                catch (Exception ex)
                {
                    if (cleanupError == null)
                    {
                        cleanupError = ex;
                    }
                    session?.OnCleanupFailed?.Invoke(ex);
                }
                finally
                {
                    session?.OnCleanupComplete?.Invoke();
                    lock (_lockObject)
                    {
                        _activeSession = null;
                    }
                }

                var result = cleanupError == null
                    ? VaultLockResult.Successful
                    : VaultLockResult.FromError(cleanupError);

                VaultLocked?.Invoke(this, new VaultLockEventArgs
                {
                    LockTime = DateTime.UtcNow,
                    Reason = reason,
                    Result = result
                });

                if (cleanupError != null)
                {
                    Debug.WriteLine($"[VaultLockDurationService] Lock sequence completed with error: {cleanupError}");
                }

                return result;
            }
            finally
            {
                _lockSemaphore.Release();
            }
        }

        /// <summary>
        /// Registers the active session so auto-lock can perform secure cleanup.
        /// </summary>
        public void SetActiveSession(VaultLockSessionContext? session)
        {
            lock (_lockObject)
            {
                _activeSession = session;
            }
        }

        /// <summary>
        /// Reset the auto-lock timer (call this on user activity).
        /// </summary>
        public void ResetTimer()
        {
            lock (_lockObject)
            {
                if (_isLocked || _currentDuration == LockDuration.Never)
                    return;

                _lastActivityTime = DateTime.UtcNow;

                // Restart timer
                StopTimer();
                int durationMs = GetDurationMilliseconds(_currentDuration);
                _lockTimer = new Timer(OnTimerElapsed, null, durationMs, Timeout.Infinite);

                ActivityDetected?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Get the duration description for UI display.
        /// </summary>
        public static string GetDurationDescription(LockDuration duration)
        {
            return duration switch
            {
                LockDuration.Immediate => "Immediately",
                LockDuration.ThirtySeconds => "After 30 seconds",
                LockDuration.OneMinute => "After 1 minute",
                LockDuration.TwoMinutes => "After 2 minutes",
                LockDuration.FiveMinutes => "After 5 minutes",
                LockDuration.TenMinutes => "After 10 minutes",
                LockDuration.FifteenMinutes => "After 15 minutes",
                LockDuration.ThirtyMinutes => "After 30 minutes",
                LockDuration.OneHour => "After 1 hour",
                LockDuration.Never => "Never (manual lock only)",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Get all available lock durations for UI binding.
        /// </summary>
        public static LockDuration[] GetAllDurations()
        {
            return new[]
            {
                LockDuration.Immediate,
                LockDuration.ThirtySeconds,
                LockDuration.OneMinute,
                LockDuration.TwoMinutes,
                LockDuration.FiveMinutes,
                LockDuration.TenMinutes,
                LockDuration.FifteenMinutes,
                LockDuration.ThirtyMinutes,
                LockDuration.OneHour,
                LockDuration.Never
            };
        }

        private void OnTimerElapsed(object? state)
        {
            LockVault();
        }

        private void StopTimer()
        {
            _lockTimer?.Dispose();
            _lockTimer = null;
        }

        private static int GetDurationMilliseconds(LockDuration duration)
        {
            return duration switch
            {
                LockDuration.Immediate => 0,
                LockDuration.ThirtySeconds => 30 * 1000,
                LockDuration.OneMinute => 60 * 1000,
                LockDuration.TwoMinutes => 2 * 60 * 1000,
                LockDuration.FiveMinutes => 5 * 60 * 1000,
                LockDuration.TenMinutes => 10 * 60 * 1000,
                LockDuration.FifteenMinutes => 15 * 60 * 1000,
                LockDuration.ThirtyMinutes => 30 * 60 * 1000,
                LockDuration.OneHour => 60 * 60 * 1000,
                LockDuration.Never => Timeout.Infinite,
                _ => 5 * 60 * 1000 // Default to 5 minutes
            };
        }

        public void Dispose()
        {
            StopTimer();
            _lockSemaphore.Dispose();
        }
    }

    /// <summary>
    /// Auto-lock duration options.
    /// </summary>
    public enum LockDuration
    {
        Immediate,
        ThirtySeconds,
        OneMinute,
        TwoMinutes,
        FiveMinutes,
        TenMinutes,
        FifteenMinutes,
        ThirtyMinutes,
        OneHour,
        Never
    }

    /// <summary>
    /// Event args for vault lock events.
    /// </summary>
    public class VaultLockEventArgs : EventArgs
    {
        public DateTime LockTime { get; set; }
        public LockReason Reason { get; set; }
        public VaultLockResult Result { get; set; } = VaultLockResult.Successful;
    }

    /// <summary>
    /// Reason for vault locking.
    /// </summary>
    public enum LockReason
    {
        AutoLock,
        ManualLock,
        IdleTimeout,
        SystemSuspend,
        UsbRemoved,
        SecurityThreat
    }

    /// <summary>
    /// Represents the outcome of a vault lock operation, including any cleanup errors.
    /// </summary>
    public sealed record VaultLockResult(bool Success, Exception? Error)
    {
        public static VaultLockResult Successful { get; } = new VaultLockResult(true, null);

        public static VaultLockResult FromError(Exception error)
        {
            if (error == null) throw new ArgumentNullException(nameof(error));
            return new VaultLockResult(false, error);
        }
    }

    /// <summary>
    /// Provides context for secure cleanup when the vault is auto-locked.
    /// </summary>
    public sealed class VaultLockSessionContext
    {
        public string? MountPath { get; init; }
        public Func<Task>? FlushPendingWritesAsync { get; init; }
        public Func<Task>? ReleaseHandlesAsync { get; init; }
        public Func<Task>? KillResidualProcessesAsync { get; init; }
        public Action? ZeroizeSensitiveData { get; init; }
        public Action<Exception>? OnCleanupFailed { get; init; }
        public Action? OnCleanupComplete { get; init; }
    }

    internal static class VaultLockDurationServiceHelpers
    {
        public static string? ResolveMountIdentifier(string mountPath)
        {
            if (string.IsNullOrWhiteSpace(mountPath))
            {
                return null;
            }

            if (OperatingSystem.IsWindows())
            {
                return mountPath.TrimEnd('\\', '/');
            }

            try
            {
                return new DirectoryInfo(mountPath).Name;
            }
            catch
            {
                return null;
            }
        }
    }
}
