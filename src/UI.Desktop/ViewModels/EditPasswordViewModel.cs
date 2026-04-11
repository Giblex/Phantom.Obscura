using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using PhantomVault.Core.Models;
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
            TestPasswordScore = CalculatePasswordScore(CurrentPassword);
            (TestPasswordStrengthLabel, TestPasswordStrengthColor) = GetStrengthLabel(TestPasswordScore);
            SuggestedPassword = GenerateSuggestedPasswordInternal(CurrentPassword);
            SimilarityPercentage = CalculateSimilarity(CurrentPassword, SuggestedPassword);
        }

        private int CalculatePasswordScore(string password)
        {
            if (string.IsNullOrEmpty(password))
                return 0;

            int score = 0;

            // Length scoring
            score += Math.Min(password.Length * 4, 40);

            // Character variety scoring
            bool hasLower = password.Any(c => char.IsLower(c));
            bool hasUpper = password.Any(c => char.IsUpper(c));
            bool hasDigit = password.Any(c => char.IsDigit(c));
            bool hasSpecial = password.Any(c => !char.IsLetterOrDigit(c));

            if (hasLower) score += 10;
            if (hasUpper) score += 10;
            if (hasDigit) score += 10;
            if (hasSpecial) score += 20;

            // Combination bonus
            int varietyCount = (hasLower ? 1 : 0) + (hasUpper ? 1 : 0) + (hasDigit ? 1 : 0) + (hasSpecial ? 1 : 0);
            if (varietyCount >= 4) score += 10;

            return Math.Min(score, 100);
        }

        private (string label, string color) GetStrengthLabel(int score)
        {
            if (score < 20) return ("Very Weak", "#EF4444");
            if (score < 40) return ("Weak", "#F97316");
            if (score < 60) return ("Fair", "#EAB308");
            if (score < 80) return ("Good", "#84CC16");
            return ("Strong", "#22C55E");
        }

        private string GenerateSuggestedPasswordInternal(string original)
        {
            if (string.IsNullOrEmpty(original))
                return GenerateRandomPassword(16);

            // Keep similarity high (75%+) by preserving structure
            var result = new System.Text.StringBuilder(original);
            var random = new Random();

            // Replace or add special characters
            int specialCount = CountCharacters(original, c => !char.IsLetterOrDigit(c));
            if (specialCount == 0)
            {
                // Add 1-2 special characters
                var specials = "!@#$%^&*+=";
                int pos = random.Next(Math.Max(1, result.Length - 3), result.Length);
                result.Insert(pos, specials[random.Next(specials.Length)]);
            }

            // Ensure mix of cases
            if (!original.Any(char.IsUpper))
            {
                int pos = random.Next(result.Length);
                result[pos] = char.ToUpper(result[pos]);
            }

            // Add digits if missing
            if (!original.Any(char.IsDigit))
            {
                result.Append(random.Next(10));
            }

            return result.ToString().Substring(0, Math.Min(result.Length, 32));
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
            CurrentPassword = GenerateRandomPassword(16);
        }

        private string GenerateRandomPassword(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*+=";
            var random = new Random();
            var password = new System.Text.StringBuilder();

            for (int i = 0; i < length; i++)
            {
                password.Append(chars[random.Next(chars.Length)]);
            }

            return password.ToString();
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
