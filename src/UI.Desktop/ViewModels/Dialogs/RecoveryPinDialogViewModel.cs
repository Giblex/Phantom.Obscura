using System;
using System.Reactive;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using PhantomVault.Core.Services;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels.Dialogs
{
    public sealed class RecoveryPinDialogViewModel : ReactiveObject
    {
        private const string MaskedDisplay = "•••• •••• •••• ••••";

        private readonly Window _owner;
        private string? _generatedPin;
        private bool _isRevealed;
        private bool _hasAcknowledged;
        private string? _copyStatus;
        private string? _errorMessage;

        public RecoveryPinDialogViewModel(Window owner)
        {
            _owner = owner;
            _generatedPin = GenerateRecoveryPin();

            CopyCommand = ReactiveCommand.CreateFromTask(OnCopyAsync);
            ConfirmCommand = ReactiveCommand.Create(OnConfirm);
            CancelCommand = ReactiveCommand.Create(OnCancel);

            _owner.Closed += (_, _) => Zeroize();
            this.RaisePropertyChanged(nameof(DisplayPin));
        }

        public string DisplayPin =>
            _generatedPin is null
                ? string.Empty
                : (IsRevealed ? _generatedPin : MaskedDisplay);

        public bool IsRevealed
        {
            get => _isRevealed;
            set
            {
                this.RaiseAndSetIfChanged(ref _isRevealed, value);
                this.RaisePropertyChanged(nameof(DisplayPin));
            }
        }

        public bool HasAcknowledged
        {
            get => _hasAcknowledged;
            set => this.RaiseAndSetIfChanged(ref _hasAcknowledged, value);
        }

        public string? CopyStatus
        {
            get => _copyStatus;
            private set => this.RaiseAndSetIfChanged(ref _copyStatus, value);
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
        }

        public ReactiveCommand<Unit, Unit> CopyCommand { get; }
        public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

        public bool Success { get; private set; }

        public string? GeneratedPin => _generatedPin;

        private async System.Threading.Tasks.Task OnCopyAsync()
        {
            try
            {
                var clipboard = TopLevel.GetTopLevel(_owner)?.Clipboard;
                if (clipboard == null || _generatedPin == null)
                {
                    CopyStatus = "Clipboard unavailable.";
                    return;
                }
                await clipboard.SetTextAsync(_generatedPin);
                CopyStatus = "Copied. Paste into your offline storage now.";
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Copy failed: {ex.Message}";
            }
        }

        private void OnConfirm()
        {
            if (!HasAcknowledged)
            {
                ErrorMessage = "Please confirm you have stored the PIN safely.";
                return;
            }
            Success = true;
            _owner.Close();
        }

        private void OnCancel()
        {
            Success = false;
            _owner.Close();
        }

        private void Zeroize()
        {
            if (_generatedPin == null) return;
            var bytes = Encoding.UTF8.GetBytes(_generatedPin);
            CryptographicOperations.ZeroMemory(bytes);
            _generatedPin = null;
        }

        private static string GenerateRecoveryPin()
        {
            try
            {
                var svc = new RecoveryCodeService();
                var codes = svc.GenerateRecoveryCodes(1);
                return codes[0];
            }
            catch
            {
                var buf = new byte[8];
                RandomNumberGenerator.Fill(buf);
                var hex = Convert.ToHexString(buf);
                return $"{hex.Substring(0, 4)}-{hex.Substring(4, 4)}-{hex.Substring(8, 4)}-{hex.Substring(12, 4)}";
            }
        }
    }
}
