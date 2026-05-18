using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using PhantomVault.Core.Models;
using PhantomVault.UI.Helpers;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels
{
    public class EditPasswordViewModel : ReactiveObject
    {
        private readonly Credential _credential;
        private readonly VaultViewModel _vaultViewModel;
        private readonly CredentialViewModel? _credentialVM;

        private string _currentPassword = string.Empty;
        public string CurrentPassword
        {
            get => _currentPassword;
            set
            {
                this.RaiseAndSetIfChanged(ref _currentPassword, value);
                UpdatePasswordTestResults();
            }
        }

        private string _suggestedPassword = string.Empty;
        public string SuggestedPassword
        {
            get => _suggestedPassword;
            set
            {
                this.RaiseAndSetIfChanged(ref _suggestedPassword, value);
                this.RaisePropertyChanged(nameof(HasSuggestedPassword));
            }
        }

        public bool HasSuggestedPassword => !string.IsNullOrEmpty(_suggestedPassword);

        private int _testPasswordScore = 0;
        public int TestPasswordScore
        {
            get => _testPasswordScore;
            private set => this.RaiseAndSetIfChanged(ref _testPasswordScore, value);
        }

        private string _testPasswordStrengthLabel = "Very Weak";
        public string TestPasswordStrengthLabel
        {
            get => _testPasswordStrengthLabel;
            private set => this.RaiseAndSetIfChanged(ref _testPasswordStrengthLabel, value);
        }

        private string _testPasswordStrengthColor = "#EF4444";
        public string TestPasswordStrengthColor
        {
            get => _testPasswordStrengthColor;
            private set => this.RaiseAndSetIfChanged(ref _testPasswordStrengthColor, value);
        }

        private double _similarityPercentage = 0;
        public double SimilarityPercentage
        {
            get => _similarityPercentage;
            private set => this.RaiseAndSetIfChanged(ref _similarityPercentage, value);
        }

        private string _credentialTitle = string.Empty;
        public string CredentialTitle
        {
            get => _credentialTitle;
            set => this.RaiseAndSetIfChanged(ref _credentialTitle, value);
        }

        private string _credentialUsername = string.Empty;
        public string CredentialUsername
        {
            get => _credentialUsername;
            set => this.RaiseAndSetIfChanged(ref _credentialUsername, value);
        }

        private bool _hasTotpSecret = false;
        public bool HasTotpSecret
        {
            get => _hasTotpSecret;
            set => this.RaiseAndSetIfChanged(ref _hasTotpSecret, value);
        }

        private string _totpSecret = string.Empty;
        public string TotpSecret
        {
            get => _totpSecret;
            set => this.RaiseAndSetIfChanged(ref _totpSecret, value);
        }

        private bool _hasPasskey = false;
        public bool HasPasskey
        {
            get => _hasPasskey;
            set => this.RaiseAndSetIfChanged(ref _hasPasskey, value);
        }

        private bool _hasPhantomKey = false;
        public bool HasPhantomKey
        {
            get => _hasPhantomKey;
            set => this.RaiseAndSetIfChanged(ref _hasPhantomKey, value);
        }

        public ReactiveCommand<Unit, Unit> GeneratePasswordCommand { get; }
        public ReactiveCommand<Unit, Unit> CopySuggestedPasswordCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteCommand { get; }

        public event Action<bool>? RequestClose;

        public EditPasswordViewModel(Credential credential, CredentialViewModel? credentialVM, VaultViewModel vaultViewModel)
        {
            _credential = credential ?? throw new ArgumentNullException(nameof(credential));
            _vaultViewModel = vaultViewModel ?? throw new ArgumentNullException(nameof(vaultViewModel));
            _credentialVM = credentialVM;

            CredentialTitle = credential.Title ?? string.Empty;
            CredentialUsername = credential.Username ?? string.Empty;
            CurrentPassword = credential.Password ?? string.Empty;
            _credentialTitle = CredentialTitle;
            _credentialUsername = CredentialUsername;
            HasTotpSecret = !string.IsNullOrEmpty(credential.TotpSecret);
            _totpSecret = credential.TotpSecret ?? string.Empty;

            GeneratePasswordCommand = ReactiveCommand.Create(GeneratePassword);
            CopySuggestedPasswordCommand = ReactiveCommand.Create(CopySuggestedPassword);
            SaveCommand = ReactiveCommand.Create(Save);
            CancelCommand = ReactiveCommand.Create(Cancel);
            DeleteCommand = ReactiveCommand.Create(Delete);

            // Initial calculation
            UpdatePasswordTestResults();
        }

        private void UpdatePasswordTestResults()
        {
            var assessment = PasswordStrengthEvaluator.Evaluate(CurrentPassword);
            TestPasswordScore = assessment.Score;
            TestPasswordStrengthLabel = assessment.Label;
            TestPasswordStrengthColor = assessment.ColorHex;
            SuggestedPassword = PasswordStrengthEvaluator.GenerateSuggestedPassword(CurrentPassword);
            SimilarityPercentage = string.IsNullOrEmpty(SuggestedPassword)
                ? 0
                : CalculateSimilarity(CurrentPassword, SuggestedPassword);
        }

        private int CalculatePasswordScore(string password)
        {
            return PasswordStrengthEvaluator.Evaluate(password).Score;
        }

        private (string label, string color) GetStrengthLabel(int score)
        {
            var assessment = PasswordStrengthEvaluator.Evaluate(CurrentPassword);
            return (assessment.Label, assessment.ColorHex);
        }

        private string GenerateSuggestedPasswordInternal(string original)
        {
            return PasswordStrengthEvaluator.GenerateSuggestedPassword(original);
        }

        private int CountCharacters(string text, Func<char, bool> predicate)
        {
            return text.Count(predicate);
        }

        private double CalculateSimilarity(string original, string suggested)
        {
            if (string.IsNullOrEmpty(original))
                return 0;

            // Simple similarity: count matching characters
            int matches = 0;
            for (int i = 0; i < Math.Min(original.Length, suggested.Length); i++)
            {
                if (original[i] == suggested[i])
                    matches++;
            }

            return Math.Round((double)matches / original.Length * 100, 1);
        }

        private void GeneratePassword()
        {
            CurrentPassword = GenerateRandomPassword(20);
        }

        private string GenerateRandomPassword(int length)
        {
            return PasswordStrengthEvaluator.GenerateRandomPassword(length);
        }

        private void CopySuggestedPassword()
        {
            if (!string.IsNullOrEmpty(SuggestedPassword))
            {
                try
                {
                    System.Windows.Forms.Clipboard.SetText(SuggestedPassword);
                }
                catch { /* Ignore clipboard errors */ }
            }
        }

        private void Save()
        {
            // Update credential with new values
            _credential.Password = CurrentPassword;
            _credential.Title = CredentialTitle;
            _credential.Username = CredentialUsername;

            if (HasTotpSecret)
            {
                _credential.TotpSecret = TotpSecret;
            }

            // Update vault - if we have the CredentialViewModel, we can update it directly
            if (_credentialVM != null)
            {
                // Refresh the credential from the core model
                _credentialVM.Refresh();
            }
            
            RequestClose?.Invoke(true);
        }

        private void Cancel()
        {
            RequestClose?.Invoke(false);
        }

        private void Delete()
        {
            if (_credentialVM != null)
            {
                _vaultViewModel.DeleteCredentialCommand?.Execute(_credentialVM).Subscribe();
            }
            RequestClose?.Invoke(false);
        }
    }
}
