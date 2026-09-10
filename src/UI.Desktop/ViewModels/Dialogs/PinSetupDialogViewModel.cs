using System;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using ReactiveUI;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.ViewModels.Dialogs
{
    public sealed class PinSetupDialogViewModel : ReactiveObject
    {
        private readonly Window _owner;
        private readonly Func<string, Task>? _setPin;
        private string _pin = string.Empty;
        private string _confirmPin = string.Empty;
        private string? _errorMessage;

        /// <param name="setPin">
        /// Stores the PIN in the unlocked vault's encrypted manifest. Null when the dialog was
        /// opened without an unlocked vault, in which case no PIN can be set.
        /// </param>
        public PinSetupDialogViewModel(Window owner, Func<string, Task>? setPin = null)
        {
            _owner = owner;
            _setPin = setPin;

            SetPinCommand = ReactiveCommand.CreateFromTask(OnSetPinAsync);
            CancelCommand = ReactiveCommand.Create(OnCancel);
        }

        public string Pin
        {
            get => _pin;
            set
            {
                this.RaiseAndSetIfChanged(ref _pin, value);
                ErrorMessage = null;
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

        private async Task OnSetPinAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Pin))
                {
                    ErrorMessage = "PIN cannot be empty.";
                    return;
                }

                if (Pin.Length < PinLockService.MinVaultPinLength)
                {
                    ErrorMessage = $"PIN must be {PinLockService.MinVaultPinLength}-{PinLockService.MaxVaultPinLength} digits.";
                    return;
                }

                if (Pin.Length > PinLockService.MaxVaultPinLength || !System.Linq.Enumerable.All(Pin, char.IsDigit))
                {
                    ErrorMessage = $"PIN must be {PinLockService.MinVaultPinLength}-{PinLockService.MaxVaultPinLength} digits.";
                    return;
                }

                if (!string.Equals(Pin, ConfirmPin, StringComparison.Ordinal))
                {
                    ErrorMessage = "PINs do not match.";
                    return;
                }

                if (_setPin == null)
                {
                    ErrorMessage = "Open Settings from an unlocked vault to set a PIN.";
                    return;
                }

                await _setPin(Pin);

                Success = true;
                _owner.Close();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[PinSetup] Failed to set the vault PIN.");
                ErrorMessage = "The PIN could not be set. Verify the vault is unlocked and try again.";
            }
        }

        private void OnCancel()
        {
            Success = false;
            _owner.Close();
        }
    }
}
