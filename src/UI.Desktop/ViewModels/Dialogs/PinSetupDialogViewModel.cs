using System;
using System.Reactive;
using Avalonia.Controls;
using ReactiveUI;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.ViewModels.Dialogs
{
    public sealed class PinSetupDialogViewModel : ReactiveObject
    {
        private readonly Window _owner;
        private readonly string? _manifestPath;
        private string _pin = string.Empty;
        private string _confirmPin = string.Empty;
        private string? _errorMessage;

        public PinSetupDialogViewModel(Window owner, string? manifestPath = null)
        {
            _owner = owner;
            _manifestPath = manifestPath;

            SetPinCommand = ReactiveCommand.Create(OnSetPin);
            CancelCommand = ReactiveCommand.Create(OnCancel);
        }

        public string Pin
        {
            get => _pin;
            set
            {
                this.RaiseAndSetIfChanged(ref _pin, value);
                ErrorMessage = null; // Clear error when user types
            }
        }

        public string ConfirmPin
        {
            get => _confirmPin;
            set
            {
                this.RaiseAndSetIfChanged(ref _confirmPin, value);
                ErrorMessage = null;
            }
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
        }

        public ReactiveCommand<Unit, Unit> SetPinCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

        public bool Success { get; private set; }

        private void OnSetPin()
        {
            try
            {
                // Validate PIN length
                if (string.IsNullOrWhiteSpace(Pin))
                {
                    ErrorMessage = "PIN cannot be empty.";
                    return;
                }

                if (Pin.Length < 4)
                {
                    ErrorMessage = "PIN must be at least 4 characters.";
                    return;
                }

                // Validate PIN match
                if (!string.Equals(Pin, ConfirmPin, StringComparison.Ordinal))
                {
                    ErrorMessage = "PINs do not match.";
                    return;
                }

                // Set PIN in PinLockService (which will handle both settings and manifest)
                if (!string.IsNullOrWhiteSpace(_manifestPath))
                {
                    PinLockService.SetPin(Pin, _manifestPath);
                }
                else
                {
                    PinLockService.SetPin(Pin);
                }

                Success = true;
                _owner.Close();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to set PIN: {ex.Message}";
            }
        }

        private void OnCancel()
        {
            Success = false;
            _owner.Close();
        }
    }
}
