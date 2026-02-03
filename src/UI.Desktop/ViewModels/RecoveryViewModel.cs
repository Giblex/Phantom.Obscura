using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;
using PhantomVault.Core.Models.DomainStores;
using PhantomVault.Core.Services.DomainKeys;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the recovery wizard UI.
    /// Manages the multi-step recovery flow with proper state transitions.
    /// </summary>
    public sealed class RecoveryViewModel : ReactiveObject, IDisposable
    {
        private readonly ScopedRecoveryService _scopedRecoveryService;
        private readonly RecoveryAuditService _auditService;
        private readonly Func<string, string, bool> _verifyHash;

        private RecoveryStore? _recoveryStore;
        private RecoverySession? _session;
        private CancellationTokenSource? _delayCts;

        // Step state
        private RecoveryStep _currentStep = RecoveryStep.Welcome;
        private bool _isBusy;
        private string _statusMessage = string.Empty;
        private string _errorMessage = string.Empty;
        private bool _hasError;

        // Welcome step
        private string _deviceWarning = string.Empty;
        private bool _hasDeviceWarning;

        // Code entry step
        private string _recoveryCode = string.Empty;
        private int _remainingCodes;
        private int _failedAttempts;

        // Delay step
        private int _delaySecondsRemaining;
        private int _totalDelaySeconds;
        private bool _isDelayActive;

        // Domain selection step
        private CryptoDomain? _selectedDomain;
        private bool _canSelectObscura = true;
        private bool _canSelectAttestor = true;
        private string _obscuraStatus = string.Empty;
        private string _attestorStatus = string.Empty;

        // Completion step
        private bool _rekeyRequired;
        private string _completionMessage = string.Empty;

        // Audit history
        private ObservableCollection<RecoveryAuditEntryViewModel> _auditHistory = new();

        public RecoveryViewModel(
            ScopedRecoveryService scopedRecoveryService,
            RecoveryAuditService auditService,
            Func<string, string, bool> verifyHash)
        {
            _scopedRecoveryService = scopedRecoveryService ?? throw new ArgumentNullException(nameof(scopedRecoveryService));
            _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
            _verifyHash = verifyHash ?? throw new ArgumentNullException(nameof(verifyHash));

            // Subscribe to rekey events
            _auditService.RekeyRequired += OnRekeyRequired;

            // Commands
            StartRecoveryCommand = ReactiveCommand.CreateFromTask(StartRecoveryAsync);
            ValidateCodeCommand = ReactiveCommand.CreateFromTask(ValidateCodeAsync);
            AbortRecoveryCommand = ReactiveCommand.Create(AbortRecovery);
            SelectDomainCommand = ReactiveCommand.CreateFromTask<CryptoDomain>(SelectDomainAsync);
            CompleteRecoveryCommand = ReactiveCommand.Create(CompleteRecovery);
            ViewAuditHistoryCommand = ReactiveCommand.Create(ViewAuditHistory);
        }

        #region Properties

        public RecoveryStep CurrentStep
        {
            get => _currentStep;
            private set => this.RaiseAndSetIfChanged(ref _currentStep, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
        }

        public bool HasError
        {
            get => _hasError;
            private set => this.RaiseAndSetIfChanged(ref _hasError, value);
        }

        public string DeviceWarning
        {
            get => _deviceWarning;
            private set => this.RaiseAndSetIfChanged(ref _deviceWarning, value);
        }

        public bool HasDeviceWarning
        {
            get => _hasDeviceWarning;
            private set => this.RaiseAndSetIfChanged(ref _hasDeviceWarning, value);
        }

        public string RecoveryCode
        {
            get => _recoveryCode;
            set => this.RaiseAndSetIfChanged(ref _recoveryCode, value);
        }

        public int RemainingCodes
        {
            get => _remainingCodes;
            private set => this.RaiseAndSetIfChanged(ref _remainingCodes, value);
        }

        public int FailedAttempts
        {
            get => _failedAttempts;
            private set => this.RaiseAndSetIfChanged(ref _failedAttempts, value);
        }

        public int DelaySecondsRemaining
        {
            get => _delaySecondsRemaining;
            private set => this.RaiseAndSetIfChanged(ref _delaySecondsRemaining, value);
        }

        public int TotalDelaySeconds
        {
            get => _totalDelaySeconds;
            private set => this.RaiseAndSetIfChanged(ref _totalDelaySeconds, value);
        }

        public bool IsDelayActive
        {
            get => _isDelayActive;
            private set => this.RaiseAndSetIfChanged(ref _isDelayActive, value);
        }

        public CryptoDomain? SelectedDomain
        {
            get => _selectedDomain;
            set => this.RaiseAndSetIfChanged(ref _selectedDomain, value);
        }

        public bool CanSelectObscura
        {
            get => _canSelectObscura;
            private set => this.RaiseAndSetIfChanged(ref _canSelectObscura, value);
        }

        public bool CanSelectAttestor
        {
            get => _canSelectAttestor;
            private set => this.RaiseAndSetIfChanged(ref _canSelectAttestor, value);
        }

        public string ObscuraStatus
        {
            get => _obscuraStatus;
            private set => this.RaiseAndSetIfChanged(ref _obscuraStatus, value);
        }

        public string AttestorStatus
        {
            get => _attestorStatus;
            private set => this.RaiseAndSetIfChanged(ref _attestorStatus, value);
        }

        public bool RekeyRequired
        {
            get => _rekeyRequired;
            private set => this.RaiseAndSetIfChanged(ref _rekeyRequired, value);
        }

        public string CompletionMessage
        {
            get => _completionMessage;
            private set => this.RaiseAndSetIfChanged(ref _completionMessage, value);
        }

        public ObservableCollection<RecoveryAuditEntryViewModel> AuditHistory
        {
            get => _auditHistory;
            private set => this.RaiseAndSetIfChanged(ref _auditHistory, value);
        }

        #endregion

        #region Commands

        public ReactiveCommand<Unit, Unit> StartRecoveryCommand { get; }
        public ReactiveCommand<Unit, Unit> ValidateCodeCommand { get; }
        public ReactiveCommand<Unit, Unit> AbortRecoveryCommand { get; }
        public ReactiveCommand<CryptoDomain, Unit> SelectDomainCommand { get; }
        public ReactiveCommand<Unit, Unit> CompleteRecoveryCommand { get; }
        public ReactiveCommand<Unit, Unit> ViewAuditHistoryCommand { get; }

        #endregion

        #region Public Methods

        /// <summary>
        /// Initializes the recovery view model with a recovery store.
        /// </summary>
        public void Initialize(RecoveryStore store, string? deviceFingerprint = null, string? appVersion = null)
        {
            _recoveryStore = store ?? throw new ArgumentNullException(nameof(store));

            // Update remaining codes
            RemainingCodes = _auditService.GetRemainingCodeCount(store);

            // Check sealed key availability
            CanSelectObscura = store.ObscuraSealed != null && !store.ObscuraSealed.HasBeenUsed;
            CanSelectAttestor = store.AttestorSealed != null && !store.AttestorSealed.HasBeenUsed;

            ObscuraStatus = store.ObscuraSealed == null
                ? "No recovery key configured"
                : store.ObscuraSealed.HasBeenUsed
                    ? "Recovery key already used"
                    : "Available";

            AttestorStatus = store.AttestorSealed == null
                ? "No recovery key configured"
                : store.AttestorSealed.HasBeenUsed
                    ? "Recovery key already used"
                    : "Available";

            // Load audit history
            LoadAuditHistory();

            // Create session
            _session = new RecoverySession(
                _scopedRecoveryService,
                _auditService,
                _recoveryStore,
                deviceFingerprint,
                appVersion);

            CurrentStep = RecoveryStep.Welcome;
        }

        /// <summary>
        /// Called when USB is removed during recovery.
        /// </summary>
        public void OnUsbRemoved()
        {
            _session?.OnUsbRemoved();
            _delayCts?.Cancel();

            ErrorMessage = "USB drive was removed during recovery. Session terminated.";
            HasError = true;
            CurrentStep = RecoveryStep.Error;
        }

        #endregion

        #region Private Methods

        private async Task StartRecoveryAsync()
        {
            if (_session == null || _recoveryStore == null)
            {
                ErrorMessage = "Recovery not initialized";
                HasError = true;
                return;
            }

            IsBusy = true;
            HasError = false;
            StatusMessage = "Entering recovery mode...";

            try
            {
                var warning = _session.EnterRecoveryMode();

                if (!string.IsNullOrEmpty(warning))
                {
                    DeviceWarning = warning;
                    HasDeviceWarning = true;
                }

                CurrentStep = RecoveryStep.EnterCode;
                StatusMessage = $"Enter one of your {RemainingCodes} recovery codes";
            }
            catch (UnauthorizedAccessException ex)
            {
                ErrorMessage = ex.Message;
                HasError = true;
                CurrentStep = RecoveryStep.Error;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to enter recovery mode: {ex.Message}";
                HasError = true;
                CurrentStep = RecoveryStep.Error;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ValidateCodeAsync()
        {
            if (_session == null || _recoveryStore == null)
                return;

            if (string.IsNullOrWhiteSpace(RecoveryCode))
            {
                ErrorMessage = "Please enter a recovery code";
                HasError = true;
                return;
            }

            IsBusy = true;
            HasError = false;
            StatusMessage = "Validating recovery code...";

            try
            {
                var isValid = _session.ValidateCode(
                    RecoveryCode,
                    _verifyHash,
                    _recoveryStore.Salt);

                if (!isValid)
                {
                    FailedAttempts++;
                    ErrorMessage = $"Invalid recovery code. Attempts: {FailedAttempts}";
                    HasError = true;
                    RecoveryCode = string.Empty;
                    return;
                }

                // Code is valid - proceed to delay
                TotalDelaySeconds = _session.RecoveryDelaySeconds;
                DelaySecondsRemaining = TotalDelaySeconds;

                if (TotalDelaySeconds > 0)
                {
                    CurrentStep = RecoveryStep.WaitingDelay;
                    await RunDelayCountdownAsync();
                }
                else
                {
                    CurrentStep = RecoveryStep.SelectDomain;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Validation failed: {ex.Message}";
                HasError = true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RunDelayCountdownAsync()
        {
            IsDelayActive = true;
            _delayCts = new CancellationTokenSource();

            try
            {
                await _session!.WaitForDelayAsync(
                    remaining => DelaySecondsRemaining = remaining,
                    _delayCts.Token);

                CurrentStep = RecoveryStep.SelectDomain;
            }
            catch (OperationCanceledException)
            {
                // User aborted
                CurrentStep = RecoveryStep.Aborted;
            }
            finally
            {
                IsDelayActive = false;
                _delayCts?.Dispose();
                _delayCts = null;
            }
        }

        private async Task SelectDomainAsync(CryptoDomain domain)
        {
            if (_session == null)
                return;

            IsBusy = true;
            HasError = false;
            StatusMessage = $"Recovering {domain} domain...";

            try
            {
                var recoveredKey = _session.RecoverDomain(domain);

                // Key recovered successfully
                SelectedDomain = domain;
                CompletionMessage = $"Successfully recovered access to {domain} domain.\n\n" +
                                   "Your domain key has been restored.";

                // Zero the key - in a real implementation, this would be passed
                // to the appropriate service for re-keying
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(recoveredKey);

                CurrentStep = RecoveryStep.Complete;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Domain recovery failed: {ex.Message}";
                HasError = true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void AbortRecovery()
        {
            _delayCts?.Cancel();
            _session?.Abort();
            CurrentStep = RecoveryStep.Aborted;
        }

        private void CompleteRecovery()
        {
            _session?.Complete();
            // Raise event for window to close
        }

        private void ViewAuditHistory()
        {
            LoadAuditHistory();
            CurrentStep = RecoveryStep.AuditHistory;
        }

        private void LoadAuditHistory()
        {
            if (_recoveryStore == null)
                return;

            AuditHistory.Clear();

            // Load in reverse order (newest first)
            for (int i = _recoveryStore.AuditLog.Count - 1; i >= 0; i--)
            {
                var entry = _recoveryStore.AuditLog[i];
                AuditHistory.Add(new RecoveryAuditEntryViewModel(entry));
            }
        }

        private void OnRekeyRequired(object? sender, RekeyRequiredEventArgs e)
        {
            RekeyRequired = true;
            CompletionMessage += $"\n\nIMPORTANT: You must change your password for the {e.Domain} domain. " +
                                 "This is required after recovery for security.";
        }

        #endregion

        public void Dispose()
        {
            _auditService.RekeyRequired -= OnRekeyRequired;
            _delayCts?.Cancel();
            _delayCts?.Dispose();
            _session?.Dispose();
        }
    }

    /// <summary>
    /// Recovery wizard steps.
    /// </summary>
    public enum RecoveryStep
    {
        Welcome,
        EnterCode,
        WaitingDelay,
        SelectDomain,
        Complete,
        Aborted,
        Error,
        AuditHistory
    }

    /// <summary>
    /// View model for audit history entries.
    /// </summary>
    public sealed class RecoveryAuditEntryViewModel : ReactiveObject
    {
        public string Id { get; }
        public DateTimeOffset Timestamp { get; }
        public string EventType { get; }
        public bool Success { get; }
        public string? TargetDomain { get; }
        public string? FailureReason { get; }
        public string DisplayText { get; }
        public string StatusIcon { get; }

        public RecoveryAuditEntryViewModel(RecoveryAuditEntry entry)
        {
            Id = entry.Id;
            Timestamp = entry.Timestamp;
            EventType = entry.EventType.ToString();
            Success = entry.Success;
            TargetDomain = entry.TargetDomain;
            FailureReason = entry.FailureReason;

            StatusIcon = Success ? "\u2714" : "\u2718"; // Check or X mark

            var domainPart = string.IsNullOrEmpty(TargetDomain) ? "" : $" [{TargetDomain}]";
            var failurePart = string.IsNullOrEmpty(FailureReason) ? "" : $" - {FailureReason}";
            DisplayText = $"{EventType}{domainPart}{failurePart}";
        }
    }
}
