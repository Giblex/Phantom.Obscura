using System;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models.DomainStores;

namespace PhantomVault.Core.Services.DomainKeys
{
    /// <summary>
    /// Manages a recovery session with policy enforcement.
    ///
    /// Recovery session lifecycle:
    /// 1. Enter recovery mode (logs RecoveryModeEntered)
    /// 2. Validate recovery code (logs CodeValidationAttempt)
    /// 3. Wait for recovery delay (policy-configurable)
    /// 4. Select domain to recover (explicit selection required)
    /// 5. Unseal domain recovery key (logs DomainRecovered)
    /// 6. Trigger rekey (logs KeyRotationTriggered)
    /// 7. Exit recovery mode (logs RecoveryModeExited)
    ///
    /// Session auto-expires after MaxSessionSeconds.
    /// USB removal during recovery terminates session immediately.
    /// </summary>
    public sealed class RecoverySession : IDisposable
    {
        private readonly ScopedRecoveryService _scopedRecoveryService;
        private readonly RecoveryAuditService _auditService;
        private readonly RecoveryStore _recoveryStore;
        private readonly string? _deviceFingerprint;
        private readonly string? _appVersion;
        private readonly CancellationTokenSource _sessionCts;
        private readonly DateTimeOffset _sessionStartTime;

        private RecoveryCodeEntry? _validatedCode;
        private byte[]? _recoverySessionKey;
        private bool _disposed;
        private RecoverySessionState _state;

        public RecoverySession(
            ScopedRecoveryService scopedRecoveryService,
            RecoveryAuditService auditService,
            RecoveryStore recoveryStore,
            string? deviceFingerprint = null,
            string? appVersion = null)
        {
            _scopedRecoveryService = scopedRecoveryService ?? throw new ArgumentNullException(nameof(scopedRecoveryService));
            _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
            _recoveryStore = recoveryStore ?? throw new ArgumentNullException(nameof(recoveryStore));
            _deviceFingerprint = deviceFingerprint;
            _appVersion = appVersion;
            _sessionCts = new CancellationTokenSource();
            _sessionStartTime = DateTimeOffset.UtcNow;
            _state = RecoverySessionState.NotStarted;
        }

        /// <summary>
        /// Current session state.
        /// </summary>
        public RecoverySessionState State => _state;

        /// <summary>
        /// Whether USB presence is required (from policy).
        /// </summary>
        public bool RequiresUsbPresence => _recoveryStore.Policy.RequireUsbPresence;

        /// <summary>
        /// Recovery delay in seconds (from policy).
        /// </summary>
        public int RecoveryDelaySeconds => _recoveryStore.Policy.RecoveryDelaySeconds;

        /// <summary>
        /// Time remaining in session (seconds).
        /// </summary>
        public int SessionSecondsRemaining
        {
            get
            {
                var elapsed = (DateTimeOffset.UtcNow - _sessionStartTime).TotalSeconds;
                var remaining = _recoveryStore.Policy.MaxSessionSeconds - elapsed;
                return Math.Max(0, (int)remaining);
            }
        }

        /// <summary>
        /// Remaining unused recovery codes.
        /// </summary>
        public int RemainingCodes => _auditService.GetRemainingCodeCount(_recoveryStore);

        /// <summary>
        /// Enters recovery mode. Must be called first.
        /// </summary>
        /// <returns>Device mismatch warning if applicable</returns>
        public string? EnterRecoveryMode()
        {
            ThrowIfDisposed();

            if (_state != RecoverySessionState.NotStarted)
                throw new InvalidOperationException($"Cannot enter recovery mode from state {_state}");

            // Validate policy
            _auditService.ValidatePolicy(_recoveryStore, _deviceFingerprint, _appVersion);

            // Check device match (warning only in Warn mode)
            _auditService.CheckDeviceMatch(_recoveryStore, _deviceFingerprint, out var warning);

            // Log entry
            _auditService.LogAuditEntry(
                _recoveryStore,
                RecoveryEventType.RecoveryModeEntered,
                success: true,
                deviceFingerprint: _deviceFingerprint,
                appVersion: _appVersion);

            _state = RecoverySessionState.AwaitingCodeValidation;

            // Start session timeout
            _ = MonitorSessionTimeoutAsync();

            return warning;
        }

        /// <summary>
        /// Validates a recovery code.
        /// </summary>
        /// <param name="recoveryCode">Recovery code entered by user</param>
        /// <param name="verifyHash">Function to verify Argon2id hash</param>
        /// <param name="salt">Salt for session key derivation</param>
        /// <returns>True if valid</returns>
        public bool ValidateCode(
            string recoveryCode,
            Func<string, string, bool> verifyHash,
            byte[] salt)
        {
            ThrowIfDisposed();

            if (_state != RecoverySessionState.AwaitingCodeValidation)
                throw new InvalidOperationException($"Cannot validate code from state {_state}");

            var codeEntry = _auditService.ValidateRecoveryCode(_recoveryStore, recoveryCode, verifyHash);

            if (codeEntry == null)
            {
                // Log failed attempt
                _auditService.LogAuditEntry(
                    _recoveryStore,
                    RecoveryEventType.CodeValidationAttempt,
                    success: false,
                    failureReason: "Invalid recovery code",
                    deviceFingerprint: _deviceFingerprint,
                    appVersion: _appVersion);

                return false;
            }

            // Code is valid
            _validatedCode = codeEntry;

            // Derive session key
            _recoverySessionKey = _scopedRecoveryService.DeriveRecoverySessionKey(recoveryCode, salt);

            // Log success
            _auditService.LogAuditEntry(
                _recoveryStore,
                RecoveryEventType.CodeValidationAttempt,
                success: true,
                codeNumber: codeEntry.CodeNumber,
                deviceFingerprint: _deviceFingerprint,
                appVersion: _appVersion);

            _state = RecoverySessionState.AwaitingDelay;
            return true;
        }

        /// <summary>
        /// Waits for the policy-required delay before recovery can proceed.
        /// This gives users time to abort if they're being coerced.
        /// </summary>
        /// <param name="progressCallback">Called with remaining seconds</param>
        /// <param name="ct">Cancellation token</param>
        public async Task WaitForDelayAsync(
            Action<int>? progressCallback = null,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (_state != RecoverySessionState.AwaitingDelay)
                throw new InvalidOperationException($"Cannot wait for delay from state {_state}");

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _sessionCts.Token);

            var delaySeconds = _recoveryStore.Policy.RecoveryDelaySeconds;
            for (int i = delaySeconds; i > 0; i--)
            {
                linkedCts.Token.ThrowIfCancellationRequested();
                progressCallback?.Invoke(i);
                await Task.Delay(1000, linkedCts.Token);
            }

            progressCallback?.Invoke(0);
            _state = RecoverySessionState.AwaitingDomainSelection;
        }

        /// <summary>
        /// Recovers the specified domain.
        /// </summary>
        /// <param name="domain">Domain to recover (must be explicitly selected)</param>
        /// <returns>Domain recovery key (caller must zero after use)</returns>
        public byte[] RecoverDomain(CryptoDomain domain)
        {
            ThrowIfDisposed();

            if (_state != RecoverySessionState.AwaitingDomainSelection)
                throw new InvalidOperationException($"Cannot recover domain from state {_state}");

            if (_recoverySessionKey == null)
                throw new InvalidOperationException("Session key not available");

            // Get the sealed key for the requested domain
            SealedRecoveryKey? sealedKey = domain switch
            {
                CryptoDomain.Obscura => _recoveryStore.ObscuraSealed,
                CryptoDomain.Attestor => _recoveryStore.AttestorSealed,
                _ => throw new ArgumentException($"Cannot recover domain {domain}", nameof(domain))
            };

            if (sealedKey == null)
                throw new InvalidOperationException($"No sealed recovery key exists for domain {domain}");

            byte[] domainRecoveryKey;
            try
            {
                domainRecoveryKey = _scopedRecoveryService.UnsealDomainRecoveryKey(
                    sealedKey,
                    _recoverySessionKey,
                    domain);
            }
            catch (Exception ex)
            {
                // Log failure
                _auditService.LogAuditEntry(
                    _recoveryStore,
                    RecoveryEventType.DomainRecovered,
                    success: false,
                    targetDomain: domain,
                    codeNumber: _validatedCode?.CodeNumber,
                    failureReason: ex.Message,
                    deviceFingerprint: _deviceFingerprint,
                    appVersion: _appVersion);

                throw;
            }

            // Mark code and sealed key as used
            if (_validatedCode != null)
            {
                _auditService.MarkCodeUsed(_recoveryStore, _validatedCode, domain);
            }
            _auditService.MarkSealedKeyUsed(sealedKey);

            // Log success (this triggers RekeyRequired event)
            _auditService.LogAuditEntry(
                _recoveryStore,
                RecoveryEventType.DomainRecovered,
                success: true,
                targetDomain: domain,
                codeNumber: _validatedCode?.CodeNumber,
                deviceFingerprint: _deviceFingerprint,
                appVersion: _appVersion);

            _state = RecoverySessionState.DomainRecovered;
            return domainRecoveryKey;
        }

        /// <summary>
        /// Aborts the recovery session.
        /// </summary>
        public void Abort()
        {
            ThrowIfDisposed();

            _auditService.LogAuditEntry(
                _recoveryStore,
                RecoveryEventType.RecoveryAborted,
                success: true,
                deviceFingerprint: _deviceFingerprint,
                appVersion: _appVersion);

            _sessionCts.Cancel();
            _state = RecoverySessionState.Aborted;
        }

        /// <summary>
        /// Called when USB is removed during recovery.
        /// </summary>
        public void OnUsbRemoved()
        {
            if (_disposed || _state == RecoverySessionState.Completed || _state == RecoverySessionState.Aborted)
                return;

            _auditService.LogAuditEntry(
                _recoveryStore,
                RecoveryEventType.UsbRemovedDuringRecovery,
                success: false,
                failureReason: "USB removed during active recovery session",
                deviceFingerprint: _deviceFingerprint,
                appVersion: _appVersion);

            _sessionCts.Cancel();
            _state = RecoverySessionState.Aborted;
        }

        /// <summary>
        /// Ends the recovery session normally.
        /// </summary>
        public void Complete()
        {
            ThrowIfDisposed();

            _auditService.LogAuditEntry(
                _recoveryStore,
                RecoveryEventType.RecoveryModeExited,
                success: true,
                deviceFingerprint: _deviceFingerprint,
                appVersion: _appVersion);

            _state = RecoverySessionState.Completed;
        }

        private async Task MonitorSessionTimeoutAsync()
        {
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_recoveryStore.Policy.MaxSessionSeconds),
                    _sessionCts.Token);

                // Session timed out
                if (!_disposed && _state != RecoverySessionState.Completed && _state != RecoverySessionState.Aborted)
                {
                    _auditService.LogAuditEntry(
                        _recoveryStore,
                        RecoveryEventType.RecoveryModeExited,
                        success: false,
                        failureReason: "Session timeout",
                        deviceFingerprint: _deviceFingerprint,
                        appVersion: _appVersion);

                    _state = RecoverySessionState.Aborted;
                }
            }
            catch (OperationCanceledException)
            {
                // Session was cancelled normally
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RecoverySession));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _sessionCts.Cancel();
            _sessionCts.Dispose();

            if (_recoverySessionKey != null)
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(_recoverySessionKey);
                _recoverySessionKey = null;
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// States of a recovery session.
    /// </summary>
    public enum RecoverySessionState
    {
        /// <summary>
        /// Session not yet started.
        /// </summary>
        NotStarted,

        /// <summary>
        /// Waiting for user to enter recovery code.
        /// </summary>
        AwaitingCodeValidation,

        /// <summary>
        /// Waiting for policy-required delay.
        /// </summary>
        AwaitingDelay,

        /// <summary>
        /// Waiting for user to select domain.
        /// </summary>
        AwaitingDomainSelection,

        /// <summary>
        /// Domain has been recovered, awaiting completion.
        /// </summary>
        DomainRecovered,

        /// <summary>
        /// Session completed normally.
        /// </summary>
        Completed,

        /// <summary>
        /// Session was aborted.
        /// </summary>
        Aborted
    }
}
