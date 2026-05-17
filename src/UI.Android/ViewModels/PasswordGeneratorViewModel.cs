using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PhantomVault.Android.ViewModels
{
    /// <summary>
    /// ViewModel for the password generator page.
    /// Supports configurable length, character classes, and one-tap copy.
    /// </summary>
    public sealed partial class PasswordGeneratorViewModel : BaseViewModel
    {
        [ObservableProperty] private int _length = 20;
        [ObservableProperty] private bool _useUppercase = true;
        [ObservableProperty] private bool _useLowercase = true;
        [ObservableProperty] private bool _useDigits = true;
        [ObservableProperty] private bool _useSymbols = true;
        [ObservableProperty] private string _generatedPassword = string.Empty;
        [ObservableProperty] private bool _passwordCopied;

        public PasswordGeneratorViewModel() => GenerateCommand.Execute(null);

        [RelayCommand]
        private void Generate()
        {
            const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            const string lower = "abcdefghijklmnopqrstuvwxyz";
            const string digits = "0123456789";
            const string symbols = "!@#$%^&*()-_=+[]{}|;:,.<>?";

            var pool = string.Empty;
            if (UseUppercase) pool += upper;
            if (UseLowercase) pool += lower;
            if (UseDigits) pool += digits;
            if (UseSymbols) pool += symbols;

            if (string.IsNullOrEmpty(pool))
            {
                GeneratedPassword = string.Empty;
                return;
            }

            var bytes = RandomNumberGenerator.GetBytes(Length);
            GeneratedPassword = new string(bytes.Select(b => pool[b % pool.Length]).ToArray());
            PasswordCopied = false;
        }

        [RelayCommand]
        private async Task CopyAsync()
        {
            if (string.IsNullOrEmpty(GeneratedPassword)) return;
            await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.SetTextAsync(GeneratedPassword);
            PasswordCopied = true;
            await Task.Delay(2000);
            PasswordCopied = false;
        }

        partial void OnLengthChanged(int value) => Generate();
        partial void OnUseUppercaseChanged(bool value) => Generate();
        partial void OnUseLowercaseChanged(bool value) => Generate();
        partial void OnUseDigitsChanged(bool value) => Generate();
        partial void OnUseSymbolsChanged(bool value) => Generate();
    }
}
