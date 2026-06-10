using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Services.ZeroKnowledge;

namespace PhantomVault.Core.Services
{

    public class VaultLockDurationService : IDisposable
    {
        private Timer? _lockTimer;
        private LockDuration _currentDuration = LockDuration.FiveMinutes;
        private DateTime _lastActivityTime;
        private bool _isLocked = true;

        private bool _autoLockEnabled = true;
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

        public LockDuration CurrentDuration
        {
            get { lock (_lockObject) return _currentDuration; }
        }

        public bool AutoLockEnabled
        {
            get { lock (_lockObject) return _autoLockEnabled; }
            set
            {
                lock (_lockObject)
                {
                    if (_autoLockEnabled == value) return;
                    _autoLockEnabled = value;
                    if (!value)
                    {

                        StopTimer();
                    }
                }
            }
        }

        public bool IsLocked
        {
            get { lock (_lockObject) return _isLocked; }
        }

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

        public void SetLockDuration(LockDuration duration)
        {
            lock (_lockObject)
            {
                _currentDuration = duration;

                if (_isLocked)
                    return;

                StopTimer();

                if (duration == LockDuration.Never)
                    return;

                if (!_autoLockEnabled)
                    return;

                if (duration == LockDuration.Immediate)
                {

                    _ = LockVaultAsync(LockReason.AutoLock);
                    return;
                }

                int durationMs = GetDurationMilliseconds(duration);
                _lastActivityTime = DateTime.UtcNow;
                _lockTimer = new Timer(OnTimerElapsed, null, durationMs, Timeout.Infinite);
            }
        }

        public void UnlockVault()
        {
            lock (_lockObject)
            {
                if (!_isLocked)
                    return;

                _isLocked = false;
                _lastActivityTime = DateTime.UtcNow;

                if (!_autoLockEnabled)
                    return;

                if (_currentDuration != LockDuration.Never && _currentDuration != LockDuration.Immediate)
                {
                    StopTimer();
                    int durationMs = GetDurationMilliseconds(_currentDuration);
                    _lockTimer = new Timer(OnTimerElapsed, null, durationMs, Timeout.Infinite);
                }
                else if (_currentDuration == LockDuration.Immediate)
                {

                    _ = LockVaultAsync(LockReason.AutoLock);
                }
            }
        }

        public void LockVault()
        {
            _ = LockVaultAsync(LockReason.AutoLock);
        }

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

                await executeStep(session?.FlushPendingWritesAsync).ConfigureAwait(false);

                await executeStep(() => _zkVaultService.LockAndWipeKeysAsync()).ConfigureAwait(false);

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

                await executeStep(session?.ReleaseHandlesAsync).ConfigureAwait(false);

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

        public void SetActiveSession(VaultLockSessionContext? session)
        {
            lock (_lockObject)
            {
                _activeSession = session;
            }
        }

        public void ResetTimer()
        {
            lock (_lockObject)
            {
                if (_isLocked || _currentDuration == LockDuration.Never)
                    return;

                if (!_autoLockEnabled)
                    return;

                _lastActivityTime = DateTime.UtcNow;

                StopTimer();
                int durationMs = GetDurationMilliseconds(_currentDuration);
                _lockTimer = new Timer(OnTimerElapsed, null, durationMs, Timeout.Infinite);

                ActivityDetected?.Invoke(this, EventArgs.Empty);
            }
        }

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

            lock (_lockObject)
            {
                if (!_autoLockEnabled) return;
            }
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
                _ => 5 * 60 * 1000
            };
        }

        public void Dispose()
        {
            StopTimer();
            _lockSemaphore.Dispose();
        }
    }

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

    public class VaultLockEventArgs : EventArgs
    {
        public DateTime LockTime { get; set; }
        public LockReason Reason { get; set; }
        public VaultLockResult Result { get; set; } = VaultLockResult.Successful;
    }

    public enum LockReason
    {
        AutoLock,
        ManualLock,
        IdleTimeout,
        SystemSuspend,
        UsbRemoved,
        SecurityThreat
    }

    public sealed record VaultLockResult(bool Success, Exception? Error)
    {
        public static VaultLockResult Successful { get; } = new VaultLockResult(true, null);

        public static VaultLockResult FromError(Exception error)
        {
            if (error == null) throw new ArgumentNullException(nameof(error));
            return new VaultLockResult(false, error);
        }
    }

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

