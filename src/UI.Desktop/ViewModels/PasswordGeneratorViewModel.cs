using System;
using System.Linq;
using System.Reactive;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ReactiveUI;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model for standalone password generator window.
    /// </summary>
    public sealed class PasswordGeneratorViewModel : ReactiveObject
    {
        private Window? _ownerWindow;
        private bool _accepted;
        private string _generatedPassword = string.Empty;
        private int _passwordLength = 16;
        private bool _includeUppercase = true;
        private bool _includeLowercase = true;
        private bool _includeNumbers = true;
        private bool _includeSymbols = true;
        private bool _avoidAmbiguous;
        private bool _easyToRead;
        private int _passwordStrength;
        private string _passwordStrengthText = "Generate a password";
        private ISolidColorBrush _passwordStrengthColor;
        private string _statusMessage = "Configure options and click Generate";

        public PasswordGeneratorViewModel()
        {
            // Default color
            _passwordStrengthColor = new SolidColorBrush(Color.Parse("#6B8CAE"));

            // Initialize commands
            GenerateCommand = ReactiveCommand.Create(GeneratePassword);
            CopyToClipboardCommand = ReactiveCommand.CreateFromTask(CopyToClipboardAsync);
            CloseCommand = ReactiveCommand.Create(Close);

            // Add command - when used by a caller it signals the generator result should be used
            AddCommand = ReactiveCommand.Create(AddAndClose,
                this.WhenAnyValue(x => x.GeneratedPassword, gp => !string.IsNullOrEmpty(gp) && !gp.StartsWith("Please select")));

            // Preset commands
            ApplyWeakPresetCommand = ReactiveCommand.Create(ApplyWeakPreset);
            ApplyStrongPresetCommand = ReactiveCommand.Create(ApplyStrongPreset);
            ApplyMaxPresetCommand = ReactiveCommand.Create(ApplyMaxPreset);

            // Subscribe to property changes to auto-generate
            this.WhenAnyValue(
                    x => x.PasswordLength,
                    x => x.IncludeUppercase,
                    x => x.IncludeLowercase,
                    x => x.IncludeNumbers,
                    x => x.IncludeSymbols,
                    x => x.AvoidAmbiguous,
                    x => x.EasyToRead)
                .Subscribe(_ => GeneratePassword());

            // Generate initial password
            GeneratePassword();
        }

        // Properties
        public string GeneratedPassword
        {
            get => _generatedPassword;
            set => this.RaiseAndSetIfChanged(ref _generatedPassword, value);
        }

        public int PasswordLength
        {
            get => _passwordLength;
            set => this.RaiseAndSetIfChanged(ref _passwordLength, value);
        }

        public bool IncludeUppercase
        {
            get => _includeUppercase;
            set => this.RaiseAndSetIfChanged(ref _includeUppercase, value);
        }

        public bool IncludeLowercase
        {
            get => _includeLowercase;
            set => this.RaiseAndSetIfChanged(ref _includeLowercase, value);
        }

        public bool IncludeNumbers
        {
            get => _includeNumbers;
            set => this.RaiseAndSetIfChanged(ref _includeNumbers, value);
        }

        public bool IncludeSymbols
        {
            get => _includeSymbols;
            set => this.RaiseAndSetIfChanged(ref _includeSymbols, value);
        }

        public bool AvoidAmbiguous
        {
            get => _avoidAmbiguous;
            set => this.RaiseAndSetIfChanged(ref _avoidAmbiguous, value);
        }

        public bool EasyToRead
        {
            get => _easyToRead;
            set => this.RaiseAndSetIfChanged(ref _easyToRead, value);
        }

        public int PasswordStrength
        {
            get => _passwordStrength;
            set => this.RaiseAndSetIfChanged(ref _passwordStrength, value);
        }

        public string PasswordStrengthText
        {
            get => _passwordStrengthText;
            set => this.RaiseAndSetIfChanged(ref _passwordStrengthText, value);
        }

        public ISolidColorBrush PasswordStrengthColor
        {
            get => _passwordStrengthColor;
            set => this.RaiseAndSetIfChanged(ref _passwordStrengthColor, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        /// <summary>
        /// Indicates that the user accepted the generated password (clicked Add).
        /// Callers can inspect this after the dialog closes and, if true, read GeneratedPassword.
        /// </summary>
        public bool Accepted
        {
            get => _accepted;
            private set => this.RaiseAndSetIfChanged(ref _accepted, value);
        }

        // Commands
        public ReactiveCommand<Unit, Unit> GenerateCommand { get; }
        public ReactiveCommand<Unit, Unit> CopyToClipboardCommand { get; }
        public ReactiveCommand<Unit, Unit> CloseCommand { get; }
        public ReactiveCommand<Unit, Unit> AddCommand { get; }
        public ReactiveCommand<Unit, Unit> ApplyWeakPresetCommand { get; }
        public ReactiveCommand<Unit, Unit> ApplyStrongPresetCommand { get; }
        public ReactiveCommand<Unit, Unit> ApplyMaxPresetCommand { get; }

        // Methods
        private void GeneratePassword()
        {
            // Validate at least one character type is selected
            if (!IncludeUppercase && !IncludeLowercase && !IncludeNumbers && !IncludeSymbols)
            {
                GeneratedPassword = "Please select at least one character type";
                PasswordStrength = 0;
                PasswordStrengthText = "No Options";
                PasswordStrengthColor = new SolidColorBrush(Color.Parse("#F48771"));
                StatusMessage = "Select at least one character type";
                return;
            }

            // Build character set
            string upperChars = AvoidAmbiguous ? "ABCDEFGHJKLMNPQRSTUVWXYZ" : "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            string lowerChars = AvoidAmbiguous ? "abcdefghjkmnpqrstuvwxyz" : "abcdefghijklmnopqrstuvwxyz";
            string digitChars = AvoidAmbiguous ? "23456789" : "0123456789";
            string symbolChars = EasyToRead ? "!@#$%^&*" : "!@#$%^&*()_+-=[]{}|;:,.<>?";

            var charSet = new StringBuilder();
            var requiredChars = new StringBuilder();

            // Add required character types using cryptographic RNG
            if (IncludeUppercase)
            {
                charSet.Append(upperChars);
                requiredChars.Append(upperChars[SecureRandomIndex(upperChars.Length)]);
            }
            if (IncludeLowercase)
            {
                charSet.Append(lowerChars);
                requiredChars.Append(lowerChars[SecureRandomIndex(lowerChars.Length)]);
            }
            if (IncludeNumbers)
            {
                charSet.Append(digitChars);
                requiredChars.Append(digitChars[SecureRandomIndex(digitChars.Length)]);
            }
            if (IncludeSymbols)
            {
                charSet.Append(symbolChars);
                requiredChars.Append(symbolChars[SecureRandomIndex(symbolChars.Length)]);
            }

            // Fill remaining length with random characters
            var password = new StringBuilder(requiredChars.ToString());
            string allChars = charSet.ToString();
            for (int i = requiredChars.Length; i < PasswordLength; i++)
            {
                password.Append(allChars[SecureRandomIndex(allChars.Length)]);
            }

            // Shuffle the password using Fisher-Yates with cryptographic RNG
            var chars = password.ToString().ToCharArray();
            for (int i = chars.Length - 1; i > 0; i--)
            {
                int j = SecureRandomIndex(i + 1);
                (chars[i], chars[j]) = (chars[j], chars[i]);
            }

            GeneratedPassword = new string(chars);
            UpdatePasswordStrength();
            StatusMessage = $"Generated {PasswordLength}-character password";
        }

        private void UpdatePasswordStrength()
        {
            if (string.IsNullOrEmpty(GeneratedPassword) ||
                GeneratedPassword.StartsWith("Please select"))
            {
                PasswordStrength = 0;
                PasswordStrengthText = "Generate a password";
                PasswordStrengthColor = new SolidColorBrush(Color.Parse("#6B8CAE"));
                return;
            }

            var info = PasswordStrengthHelper.Evaluate(GeneratedPassword);
            PasswordStrength = info.Progress;

            if (!info.HasValue)
            {
                PasswordStrengthText = "Generate a password";
                PasswordStrengthColor = new SolidColorBrush(Color.Parse("#6B8CAE"));
                return;
            }

            PasswordStrengthText = info.Label;
            PasswordStrengthColor = info.CreateBrush();
        }

        private async System.Threading.Tasks.Task CopyToClipboardAsync()
        {
            if (string.IsNullOrEmpty(GeneratedPassword) ||
                GeneratedPassword.StartsWith("Please select"))
            {
                StatusMessage = "No password to copy";
                return;
            }

            try
            {
                var clipboard = TopLevel.GetTopLevel(_ownerWindow)?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(GeneratedPassword);
                    StatusMessage = "Password copied to clipboard!";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to copy: {ex.Message}";
            }
        }

        private void ApplyWeakPreset()
        {
            PasswordLength = 8;
            IncludeUppercase = true;
            IncludeLowercase = true;
            IncludeNumbers = true;
            IncludeSymbols = false;
            AvoidAmbiguous = false;
            EasyToRead = false;
            StatusMessage = "Applied Weak preset (8 characters)";
        }

        private void ApplyStrongPreset()
        {
            PasswordLength = 16;
            IncludeUppercase = true;
            IncludeLowercase = true;
            IncludeNumbers = true;
            IncludeSymbols = true;
            AvoidAmbiguous = false;
            EasyToRead = false;
            StatusMessage = "Applied Strong preset (16 characters)";
        }

        private void ApplyMaxPreset()
        {
            PasswordLength = 64;
            IncludeUppercase = true;
            IncludeLowercase = true;
            IncludeNumbers = true;
            IncludeSymbols = true;
            AvoidAmbiguous = false;
            EasyToRead = false;
            StatusMessage = "Applied Maximum Security preset (64 characters)";
        }

        private void Close()
        {
            _ownerWindow?.Close();
        }

        public void SetOwnerWindow(Window window)
        {
            _ownerWindow = window;
        }

        private void AddAndClose()
        {
            // Mark accepted so caller can read the GeneratedPassword
            Accepted = true;
            _ownerWindow?.Close();
        }

        /// <summary>
        /// Returns a cryptographically secure random integer in [0, exclusiveMax).
        /// Uses RandomNumberGenerator for unpredictable password generation.
        /// </summary>
        private static int SecureRandomIndex(int exclusiveMax)
        {
            if (exclusiveMax <= 0)
                throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            if (exclusiveMax == 1)
                return 0;

            // Use rejection sampling to avoid modulo bias
            Span<byte> buffer = stackalloc byte[4];
            uint range = (uint)exclusiveMax;
            uint limit = uint.MaxValue - (uint.MaxValue % range);

            uint result;
            do
            {
                RandomNumberGenerator.Fill(buffer);
                result = BitConverter.ToUInt32(buffer);
            }
            while (result >= limit);

            return (int)(result % range);
        }
    }
}
