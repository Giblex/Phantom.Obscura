using System;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models.DomainStores;

namespace PhantomVault.Core.Services.DomainKeys
{

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
        private bool _isEnvelopeSession;
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

        public RecoverySessionState State => _state;

        /// <summary>
        /// Open a recovery store envelope with a single recovery code and build a session ready to
        /// recover, WITHOUT an unlocked vault. This is the canonical break of the historical recovery
        /// Catch-22: validation is performed by <see cref="RecoveryStoreEnvelope.OpenWithCode"/> (the
        /// code unwraps the RecoveryStoreKey), and the session key becomes the RSK-derived domain seal
        /// KEK so that EVERY recovery code — not just the seed-time one — can unseal domain keys and
        /// read the backed-up keyfile.
        ///
        /// Extends, and does not replace, the legacy <see cref="ValidateCode"/> + per-code
        /// <see cref="ScopedRecoveryService.DeriveRecoverySessionKey"/> path.
        /// </summary>
        public static RecoverySession OpenFromEnvelope(
            byte[] envelopeBytes,
            string recoveryCode,
            ScopedRecoveryService scopedRecoveryService,
            RecoveryAuditService auditService,
            string? deviceFingerprint = null,
            string? appVersion = null)
        {
            if (envelopeBytes == null) throw new ArgumentNullException(nameof(envelopeBytes));
            if (string.IsNullOrWhiteSpace(recoveryCode)) throw new ArgumentException("A recovery code is required.", nameof(recoveryCode));
            if (scopedRecoveryService == null) throw new ArgumentNullException(nameof(scopedRecoveryService));
            if (auditService == null) throw new ArgumentNullException(nameof(auditService));

            using var opened = RecoveryStoreEnvelope.OpenWithCode(envelopeBytes, recoveryCode);

            var session = new RecoverySession(
                scopedRecoveryService,
                auditService,
                opened.Store,
                deviceFingerprint,
                appVersion);

            // Derive the same KEK the factory used to seal the domain keys (and that protects the
            // keyfile body), from the RSK every code unwraps. Computed before the OpenedStore is
            // disposed (which zeroizes the RSK); the derived key survives for the session lifetime.
            session._recoverySessionKey = RecoveryStoreEnvelope.DeriveDomainSealKey(
                opened.RecoveryStoreKey,
                opened.Store.Salt);
            session._isEnvelopeSession = true;
            session._state = RecoverySessionState.AwaitingDomainSelection;

            auditService.LogAuditEntry(
                opened.Store,
                RecoveryEventType.CodeValidationAttempt,
                success: true,
                deviceFingerprint: deviceFingerprint,
                appVersion: appVersion);

            _ = session.MonitorSessionTimeoutAsync();
            return session;
        }

        /// <summary>
        /// Return the backed-up keyfile material from an envelope-opened session. The caller owns
        /// zeroization of the returned bytes after rewriting the keyfile onto a new USB.
        /// </summary>
        public byte[] RecoverKeyfile()
        {
            ThrowIfDisposed();

            if (!_isEnvelopeSession)
                throw new InvalidOperationException("Keyfile recovery is only available for envelope-opened sessions.");
            if (_state != RecoverySessionState.AwaitingDomainSelection && _state != RecoverySessionState.DomainRecovered)
                throw new InvalidOperationException($"Cannot recover keyfile from state {_state}");

            var keyfile = _recoveryStore.ProtectedKeyfile;
            if (keyfile == null || keyfile.Length == 0)
                throw new InvalidOperationException("This recovery store does not contain a keyfile backup.");

            _auditService.LogAuditEntry(
                _recoveryStore,
                RecoveryEventType.DomainRecovered,
                success: true,
                targetDomain: CryptoDomain.Obscura,
                deviceFingerprint: _deviceFingerprint,
                appVersion: _appVersion);

            _state = RecoverySessionState.DomainRecovered;
            return (byte[])keyfile.Clone();
        }


        public bool RequiresUsbPresence => _recoveryStore.Policy.RequireUsbPresence;

        public int RecoveryDelaySeconds => _recoveryStore.Policy.RecoveryDelaySeconds;

        public int SessionSecondsRemaining
        {
            get
            {
                var elapsed = (DateTimeOffset.UtcNow - _sessionStartTime).TotalSeconds;
                var remaining = _recoveryStore.Policy.MaxSessionSeconds - elapsed;
                return Math.Max(0, (int)remaining);
            }
        }

        public int RemainingCodes => _auditService.GetRemainingCodeCount(_recoveryStore);

        public string? EnterRecoveryMode()
        {
            ThrowIfDisposed();

            if (_state != RecoverySessionState.NotStarted)
                throw new InvalidOperationException($"Cannot enter recovery mode from state {_state}");

            _auditService.ValidatePolicy(_recoveryStore, _deviceFingerprint, _appVersion);

            _auditService.CheckDeviceMatch(_recoveryStore, _deviceFingerprint, out var warning);

            _auditService.LogAuditEntry(
                _recoveryStore,
                RecoveryEventType.RecoveryModeEntered,
                success: true,
                deviceFingerprint: _deviceFingerprint,
                appVersion: _appVersion);

            _state = RecoverySessionState.AwaitingCodeValidation;

            _ = MonitorSessionTimeoutAsync();

            return warning;
        }

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

                _auditService.LogAuditEntry(
                    _recoveryStore,
                    RecoveryEventType.CodeValidationAttempt,
                    success: false,
                    failureReason: "Invalid recovery code",
                    deviceFingerprint: _deviceFingerprint,
                    appVersion: _appVersion);

                return false;
            }

            _validatedCode = codeEntry;

            _recoverySessionKey = _scopedRecoveryService.DeriveRecoverySessionKey(recoveryCode, salt);

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

        public byte[] RecoverDomain(CryptoDomain domain)
        {
            ThrowIfDisposed();

            if (_state != RecoverySessionState.AwaitingDomainSelection)
                throw new InvalidOperationException($"Cannot recover domain from state {_state}");

            if (_recoverySessionKey == null)
                throw new InvalidOperationException("Session key not available");

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

            if (_validatedCode != null)
            {
                _auditService.MarkCodeUsed(_recoveryStore, _validatedCode, domain);
            }
            _auditService.MarkSealedKeyUsed(sealedKey);

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

    public enum RecoverySessionState
    {

        NotStarted,

        AwaitingCodeValidation,

        AwaitingDelay,

        AwaitingDomainSelection,

        DomainRecovered,

        Completed,

        Aborted
    }
}

