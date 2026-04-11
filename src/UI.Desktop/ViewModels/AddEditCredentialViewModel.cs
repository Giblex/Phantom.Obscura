using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using ReactiveUI;
using PhantomVault.Core.Models;
using PhantomVault.UI.Services;
using PhantomVault.UI.Views;
using PhantomVault.UI.Helpers;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model for adding or editing a credential.
    /// </summary>
    public sealed class AddEditCredentialViewModel : ReactiveObject
    {
        private Window? _ownerWindow;
        private readonly Credential? _existingCredential;
        private readonly Action<Credential>? _onSave;
        private readonly PhantomVault.Core.Services.IconManager? _iconManager;

        private string _title = string.Empty;
        private string _username = string.Empty;
        private string _password = string.Empty;
        private string _url = string.Empty;
        private string _notes = string.Empty;
        private string _icon = string.Empty;
        private string _tagsText = string.Empty;
        private string _totpSecretInput = string.Empty;
        private bool _isFavorite;
        private bool _hasExpiryDate;
        private DateTimeOffset? _expiryDate;
        private CategoryViewModel? _selectedCategory;
        private ObservableCollection<CategoryViewModel> _categories = new();
        private readonly IReadOnlyList<string> _identityTypeOptions = new List<string>
        {
            "Passport",
            "Driver Licence",
            "Medicare Card",
            "Birth Certificate",
            "Proof of Age Card",
            "Concession Card",
            "Citizenship Certificate"
        };
        private readonly IReadOnlyList<string> _apiKeyTypeOptions = new List<string>
        {
            "API Key",
            "SDK Key",
            "Secret",
            "Token",
            "Private Key",
            "Public Key"
        };

        // Icon auto-detection properties
        private string? _autoDetectedIconPath;
        private bool _hasAutoDetectedIcon;
        private Bitmap? _autoDetectedIconBitmap;
        private Color _selectedIconColor = Color.Parse("#FFB5E5FF"); // Default pastel blue
        private string _iconInitials = "?";

        // Pastel color options for icons without images
        public Color[] AvailableColors { get; } = new[]
        {
            Color.Parse("#FFB5E5FF"), // Pastel Blue
            Color.Parse("#FFFFC1E3"), // Pastel Pink
            Color.Parse("#FFFFDFBB"), // Pastel Peach
            Color.Parse("#FFC7E5C7"), // Pastel Green
            Color.Parse("#FFFFE5B4"), // Pastel Yellow
            Color.Parse("#FFE5D4FF"), // Pastel Purple
            Color.Parse("#FFFFC9C9"), // Pastel Red
            Color.Parse("#FFD4F4FF"), // Pastel Cyan
            Color.Parse("#FFFFE4F0"), // Pastel Rose
            Color.Parse("#FFE8F5E9")  // Pastel Mint
        };

        private string _titleError = string.Empty;
        private string _usernameError = string.Empty;
        private string _passwordError = string.Empty;

        private bool _isPasswordVisible;
        private char _passwordChar = '●';
        private string _passwordVisibilityIcon = "👁";
        private string _passwordVisibilitySvgIcon = "Assets/SVG/Current/Visible eye.svg";

        private int _passwordStrength;
        private string _passwordStrengthText = string.Empty;
        private IBrush _passwordStrengthColor = Brushes.Gray;
        private string _passwordFlagText = string.Empty;
        private IBrush _passwordFlagBackground = Brushes.Transparent;
        private bool _showPasswordFlag;
        private string _passwordFlagValue = string.Empty;
        private bool _showQuickPicks = true;

        // Predefined icon collections
        public static readonly List<string> PopularIcons = new()
        {
            // Social Media & Communication
            "📱", "💬", "📧", "📮", "📬",
            // Web & Tech
            "🌐", "💻", "🖥️", "⌨️", "🖱️", "🔌", "💾", "📀",
            // Finance & Shopping
            "💳", "💰", "🏦", "🛒", "🛍️", "💵", "💴", "💶", "💷",
            // Entertainment
            "🎮", "🎵", "🎬", "📺", "📻", "🎭", "🎪", "🎨",
            // Social & Brands
            "📘", "📷", "📹", "🎥", "📸",
            // Security & Privacy
            "🔐", "🔒", "🔓", "🔑", "🛡️", "⚠️",
            // Work & Productivity
            "📁", "📂", "📄", "📊", "📈", "📉", "🗂️", "📋", "📌",
            // Cloud & Storage
            "☁️", "🌥️", "💿", "📦",
            // Communication Apps
            "✉️", "📩", "📨", "📤", "📥",
            // Miscellaneous
            "⚙️", "🔧", "🔨", "🏠", "🏢", "🏪", "🏥", "✈️", "🚗", "🎓", "📚"
        };

        public AddEditCredentialViewModel(Credential? credential = null, Action<Credential>? onSave = null)
        {
            _existingCredential = credential;
            _onSave = onSave;

            // Initialize IconManager (root Assets/Icons so subfolders are searched)
            try
            {
                var iconsDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Icons");
                _iconManager = new PhantomVault.Core.Services.IconManager(iconsDir);
            }
            catch
            {
                // IconManager initialization failed, auto-detection won't work
            }

            InitializeCategories();

            if (credential != null)
            {
                // Editing mode
                _title = credential.Title;
                _username = credential.Username;
                _password = credential.Password;
                _url = credential.Url;
                _notes = credential.Notes;
                _icon = credential.Icon;
                // Load IconColor if present
                try
                {
                    if (!string.IsNullOrEmpty(credential.IconColor))
                    {
                        _selectedIconColor = Color.Parse(credential.IconColor);
                    }
                }
                catch
                {
                    // ignore parse errors
                }
                _tagsText = string.Join(", ", credential.Tags);
                _hasExpiryDate = credential.ExpiryUtc.HasValue;
                _expiryDate = credential.ExpiryUtc;

                // Find matching category
                _selectedCategory = _categories.FirstOrDefault(c => c.Name == credential.Group);
            }
            else
            {
                // New credential - default to first category
                _selectedCategory = _categories.FirstOrDefault();
            }

            // Notify UI about entry type for conditional visibility
            // Set backing fields based on entry type
            var entryType = _existingCredential?.EntryType ?? EntryType.Password;
            _isPasswordEntry = entryType == EntryType.Password;
            _isCreditCardEntry = entryType == EntryType.CreditCard;
            _isBankAccountEntry = entryType == EntryType.BankAccount;
            _isIdentityEntry = entryType == EntryType.Identity;
            _isWiFiEntry = entryType == EntryType.WiFi;
            _isApiKeyEntry = entryType == EntryType.ApiKey;
            _isContactEntry = entryType == EntryType.Contact;
            _isTotpEntry = entryType == EntryType.TotpGenerator;
            _isPinCodeEntry = entryType == EntryType.PinCode;

            // Initialize TOTP secret input from existing credential
            _totpSecretInput = _existingCredential?.TotpSecret ?? string.Empty;

            if (_isIdentityEntry && string.IsNullOrWhiteSpace(IdDocumentType))
            {
                IdDocumentType = _identityTypeOptions.FirstOrDefault() ?? string.Empty;
            }

            if (_isApiKeyEntry && string.IsNullOrWhiteSpace(ApiKeyType))
            {
                ApiKeyType = _apiKeyTypeOptions.FirstOrDefault() ?? string.Empty;
            }

            Debug.WriteLine($"[ENTRY-TYPE-INIT] EntryType={entryType}, IsCreditCard={_isCreditCardEntry}, IsBankAccount={_isBankAccountEntry}, IsIdentity={_isIdentityEntry}");
            Console.WriteLine($"[ADD/EDIT VM] Constructor: _existingCredential={_existingCredential?.Title ?? "NULL"}, EntryType={entryType}, IsCreditCardEntry={_isCreditCardEntry}");
            System.Diagnostics.Trace.WriteLine($"[ADD/EDIT VM] Constructor: _existingCredential={_existingCredential?.Title ?? "NULL"}, EntryType={entryType}, IsCreditCardEntry={_isCreditCardEntry}");

            // Raise property changes immediately so bindings see the correct values
            this.RaisePropertyChanged(nameof(EntryType));
            this.RaisePropertyChanged(nameof(IsPasswordEntry));
            this.RaisePropertyChanged(nameof(IsCreditCardEntry));
            this.RaisePropertyChanged(nameof(IsBankAccountEntry));
            this.RaisePropertyChanged(nameof(IsIdentityEntry));
            this.RaisePropertyChanged(nameof(IsWiFiEntry));
            this.RaisePropertyChanged(nameof(IsApiKeyEntry));
            this.RaisePropertyChanged(nameof(IsContactEntry));
            this.RaisePropertyChanged(nameof(IsTotpEntry));
            this.RaisePropertyChanged(nameof(IsPinCodeEntry));
            this.RaisePropertyChanged(nameof(ShowPasswordField));
            this.RaisePropertyChanged(nameof(ShowPasswordGenerator));
            this.RaisePropertyChanged(nameof(ShowPasswordStrength));
            this.RaisePropertyChanged(nameof(ShowPasswordVisibilityToggle));
            this.RaisePropertyChanged(nameof(PasswordLabelText));
            this.RaisePropertyChanged(nameof(IsSecureNoteEntry));
            this.RaisePropertyChanged(nameof(ShowPasswordFields));
            this.RaisePropertyChanged(nameof(ShowCategorySelector));
            this.RaisePropertyChanged(nameof(ShowIconSelector));

            Console.WriteLine($"[ADD/EDIT VM] After RaisePropertyChanged: IsCreditCardEntry={IsCreditCardEntry}, IsPasswordEntry={IsPasswordEntry}");

            // Commands
            SaveCommand = ReactiveCommand.Create(Save);
            CancelCommand = ReactiveCommand.Create(Cancel);
            TogglePasswordVisibilityCommand = ReactiveCommand.Create(TogglePasswordVisibility);
            GeneratePasswordCommand = ReactiveCommand.Create(GeneratePassword);
            OpenPasswordGeneratorCommand = ReactiveCommand.CreateFromTask(OpenPasswordGeneratorAsync);
            Debug.WriteLine("[INIT] Creating OpenIconPickerCommand...");
            OpenIconPickerCommand = ReactiveCommand.CreateFromTask(OpenIconPickerAsync);
            OpenIconPickerCommand.ThrownExceptions.Subscribe(ex =>
            {
                Debug.WriteLine($"[ERROR] OpenIconPickerCommand exception: {ex.Message}");
                Debug.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
            });
            Debug.WriteLine($"[INIT] OpenIconPickerCommand created. OwnerWindow = {(_ownerWindow == null ? "NULL" : "SET")}");

            OpenIconLibraryCommand = ReactiveCommand.CreateFromTask(OpenIconLibraryAsync);
            OpenIconLibraryCommand.ThrownExceptions.Subscribe(ex =>
            {
                Debug.WriteLine($"[ERROR] OpenIconLibraryCommand exception: {ex.Message}");
                Debug.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
            });

            SetIconCommand = ReactiveCommand.Create<string>(SetIcon);
            SelectColorCommand = ReactiveCommand.Create<Color>(SelectColor);
            GenerateTotpSecretCommand = ReactiveCommand.Create(GenerateTotpSecret);
            RemoveTotpSecretCommand = ReactiveCommand.Create(RemoveTotpSecretInput);
            ImportFromOtpAuthCommand = ReactiveCommand.CreateFromTask(ImportFromOtpAuthAsync);
            OpenTotpSettingsCommand = ReactiveCommand.CreateFromTask(OpenTotpSettingsAsync);

            // Subscribe to password changes for strength calculation
            this.WhenAnyValue(x => x.Password)
                .Subscribe(_ => UpdatePasswordStrength());

            // Auto-detect icon when Title or Url changes
            this.WhenAnyValue(vm => vm.Title, vm => vm.Url)
                .Throttle(TimeSpan.FromMilliseconds(300))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => UpdateAutoDetectedIcon());
        }

        // Properties
        public string WindowTitle => _existingCredential != null ? "Edit Credential" : "Add Credential";
        public string SaveButtonText => _existingCredential != null ? "Update" : "Save";

        public string Title
        {
            get => _title;
            set
            {
                this.RaiseAndSetIfChanged(ref _title, value);
                TitleError = string.Empty;
            }
        }

        public string Username
        {
            get => _username;
            set
            {
                this.RaiseAndSetIfChanged(ref _username, value);
                UsernameError = string.Empty;
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                this.RaiseAndSetIfChanged(ref _password, value);
                PasswordError = string.Empty;
                this.RaisePropertyChanged(nameof(HasPassword));
            }
        }

        public string Url
        {
            get => _url;
            set => this.RaiseAndSetIfChanged(ref _url, value);
        }

        public string Notes
        {
            get => _notes;
            set => this.RaiseAndSetIfChanged(ref _notes, value);
        }

        public string Icon
        {
            get => _icon;
            set
            {
                this.RaiseAndSetIfChanged(ref _icon, value);
                this.RaisePropertyChanged(nameof(HasCustomIcon));
                this.RaisePropertyChanged(nameof(DisplayIcon));
            }
        }

        public bool HasCustomIcon => !string.IsNullOrEmpty(Icon);
        public string DisplayIcon => HasCustomIcon ? Icon : (Title.Length > 0 ? Title.Substring(0, Math.Min(2, Title.Length)).ToUpper() : "??");

        private bool _isPasswordEntry;
        private bool _isCreditCardEntry;
        private bool _isBankAccountEntry;
        private bool _isIdentityEntry;
        private bool _isWiFiEntry;
        private bool _isTotpEntry;
        private bool _isApiKeyEntry;
        private bool _isContactEntry;
        private bool _isPinCodeEntry;

        public EntryType EntryType => _existingCredential?.EntryType ?? EntryType.Password;

        public bool IsPasswordEntry
        {
            get => _isPasswordEntry;
            private set => this.RaiseAndSetIfChanged(ref _isPasswordEntry, value);
        }

        public bool IsCreditCardEntry
        {
            get => _isCreditCardEntry;
            private set => this.RaiseAndSetIfChanged(ref _isCreditCardEntry, value);
        }

        public bool IsBankAccountEntry
        {
            get => _isBankAccountEntry;
            private set => this.RaiseAndSetIfChanged(ref _isBankAccountEntry, value);
        }

        public bool IsIdentityEntry
        {
            get => _isIdentityEntry;
            private set => this.RaiseAndSetIfChanged(ref _isIdentityEntry, value);
        }

        public bool IsWiFiEntry
        {
            get => _isWiFiEntry;
            private set => this.RaiseAndSetIfChanged(ref _isWiFiEntry, value);
        }

        public bool IsApiKeyEntry
        {
            get => _isApiKeyEntry;
            private set => this.RaiseAndSetIfChanged(ref _isApiKeyEntry, value);
        }

        public bool IsContactEntry
        {
            get => _isContactEntry;
            private set => this.RaiseAndSetIfChanged(ref _isContactEntry, value);
        }

        public bool IsTotpEntry
        {
            get => _isTotpEntry;
            private set => this.RaiseAndSetIfChanged(ref _isTotpEntry, value);
        }

        public bool IsPinCodeEntry
        {
            get => _isPinCodeEntry;
            private set => this.RaiseAndSetIfChanged(ref _isPinCodeEntry, value);
        }

        public bool ShowPasswordField => (IsPasswordEntry && !IsSecureNoteEntry) || IsWiFiEntry;
        public bool ShowPasswordGenerator => IsPasswordEntry && !IsSecureNoteEntry;
        public bool ShowPasswordStrength => IsPasswordEntry && !IsSecureNoteEntry;
        public bool ShowPasswordVisibilityToggle => (IsPasswordEntry && !IsSecureNoteEntry) || IsWiFiEntry;
        public string PasswordLabelText => IsWiFiEntry ? "Network Password *" : "Password *";

        /// <summary>
        /// Path to auto-detected icon image file
        /// </summary>
        public string? AutoDetectedIconPath
        {
            get => _autoDetectedIconPath;
            private set => this.RaiseAndSetIfChanged(ref _autoDetectedIconPath, value);
        }

        /// <summary>
        /// Bitmap to bind to an Image control when an icon file is detected.
        /// This is created on the UI thread to ensure Avalonia can render it.
        /// </summary>
        public Bitmap? AutoDetectedIconBitmap
        {
            get => _autoDetectedIconBitmap;
            private set => this.RaiseAndSetIfChanged(ref _autoDetectedIconBitmap, value);
        }

        /// <summary>
        /// Whether an icon was auto-detected
        /// </summary>
        public bool HasAutoDetectedIcon
        {
            get => _hasAutoDetectedIcon;
            private set => this.RaiseAndSetIfChanged(ref _hasAutoDetectedIcon, value);
        }

        /// <summary>
        /// Selected background color for text-based icon
        /// </summary>
        public Color SelectedIconColor
        {
            get => _selectedIconColor;
            set => this.RaiseAndSetIfChanged(ref _selectedIconColor, value);
        }

        /// <summary>
        /// Initials to display when no icon image is available
        /// </summary>
        public string IconInitials
        {
            get => _iconInitials;
            private set => this.RaiseAndSetIfChanged(ref _iconInitials, value);
        }

        public bool ShowQuickPicks
        {
            get => _showQuickPicks;
            set => this.RaiseAndSetIfChanged(ref _showQuickPicks, value);
        }

        public string TagsText
        {
            get => _tagsText;
            set => this.RaiseAndSetIfChanged(ref _tagsText, value);
        }

        public bool IsFavorite
        {
            get => _isFavorite;
            set => this.RaiseAndSetIfChanged(ref _isFavorite, value);
        }

        public bool HasExpiryDate
        {
            get => _hasExpiryDate;
            set => this.RaiseAndSetIfChanged(ref _hasExpiryDate, value);
        }

        public DateTimeOffset? ExpiryDate
        {
            get => _expiryDate;
            set => this.RaiseAndSetIfChanged(ref _expiryDate, value);
        }

        public CategoryViewModel? SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedCategory, value);
                this.RaisePropertyChanged(nameof(IsSecureNoteEntry));
                this.RaisePropertyChanged(nameof(ShowPasswordFields));
                this.RaisePropertyChanged(nameof(ShowCategorySelector));
                this.RaisePropertyChanged(nameof(ShowIconSelector));
                this.RaisePropertyChanged(nameof(ShowPasswordField));
                this.RaisePropertyChanged(nameof(ShowPasswordGenerator));
                this.RaisePropertyChanged(nameof(ShowPasswordStrength));
                this.RaisePropertyChanged(nameof(ShowPasswordVisibilityToggle));
            }
        }

        public ObservableCollection<CategoryViewModel> Categories
        {
            get => _categories;
            set => this.RaiseAndSetIfChanged(ref _categories, value);
        }

        public IReadOnlyList<string> IdentityTypeOptions => _identityTypeOptions;
        public IReadOnlyList<string> ApiKeyTypeOptions => _apiKeyTypeOptions;

        public bool IsSecureNoteEntry => string.Equals(SelectedCategory?.Name ?? _existingCredential?.Group,
            "Secure Notes", StringComparison.OrdinalIgnoreCase);

        public bool ShowPasswordFields => IsPasswordEntry && !IsSecureNoteEntry;
        public bool ShowCategorySelector => !IsSecureNoteEntry;
        public bool ShowIconSelector => !IsSecureNoteEntry;

        // Validation errors
        public string TitleError
        {
            get => _titleError;
            set
            {
                this.RaiseAndSetIfChanged(ref _titleError, value);
                this.RaisePropertyChanged(nameof(HasTitleError));
            }
        }

        public string UsernameError
        {
            get => _usernameError;
            set
            {
                this.RaiseAndSetIfChanged(ref _usernameError, value);
                this.RaisePropertyChanged(nameof(HasUsernameError));
            }
        }

        public string PasswordError
        {
            get => _passwordError;
            set
            {
                this.RaiseAndSetIfChanged(ref _passwordError, value);
                this.RaisePropertyChanged(nameof(HasPasswordError));
            }
        }

        public bool HasTitleError => !string.IsNullOrEmpty(TitleError);
        public bool HasUsernameError => !string.IsNullOrEmpty(UsernameError);
        public bool HasPasswordError => !string.IsNullOrEmpty(PasswordError);

        // Password visibility
        public bool IsPasswordVisible
        {
            get => _isPasswordVisible;
            set => this.RaiseAndSetIfChanged(ref _isPasswordVisible, value);
        }

        public char PasswordChar
        {
            get => _passwordChar;
            set => this.RaiseAndSetIfChanged(ref _passwordChar, value);
        }

        public string PasswordVisibilityIcon
        {
            get => _passwordVisibilityIcon;
            set => this.RaiseAndSetIfChanged(ref _passwordVisibilityIcon, value);
        }

        public string PasswordVisibilitySvgIcon
        {
            get => _passwordVisibilitySvgIcon;
            set => this.RaiseAndSetIfChanged(ref _passwordVisibilitySvgIcon, value);
        }

        // Password strength
        public bool HasPassword => !string.IsNullOrEmpty(Password);

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

        public IBrush PasswordStrengthColor
        {
            get => _passwordStrengthColor;
            set => this.RaiseAndSetIfChanged(ref _passwordStrengthColor, value);
        }

        public string PasswordFlagText
        {
            get => _passwordFlagText;
            private set => this.RaiseAndSetIfChanged(ref _passwordFlagText, value);
        }

        public IBrush PasswordFlagBackground
        {
            get => _passwordFlagBackground;
            private set => this.RaiseAndSetIfChanged(ref _passwordFlagBackground, value);
        }

        public bool ShowPasswordFlag
        {
            get => _showPasswordFlag;
            private set => this.RaiseAndSetIfChanged(ref _showPasswordFlag, value);
        }

        // Credit Card properties
        public string CardNumber
        {
            get => _existingCredential?.CardNumber ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.CardNumber = value; this.RaisePropertyChanged(); } }
        }
        public string CardholderName
        {
            get => _existingCredential?.CardholderName ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.CardholderName = value; this.RaisePropertyChanged(); } }
        }
        public string CardType
        {
            get => _existingCredential?.CardType ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.CardType = value; this.RaisePropertyChanged(); } }
        }
        public string CardCVV
        {
            get => _existingCredential?.CardCVV ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.CardCVV = value; this.RaisePropertyChanged(); } }
        }
        public string CardCVVSecure
        {
            get => _existingCredential?.CardCVVSecure ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.CardCVVSecure = value; this.RaisePropertyChanged(); } }
        }
        public string CardExpiryMonth
        {
            get => _existingCredential?.CardExpiryMonth ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.CardExpiryMonth = value; this.RaisePropertyChanged(); } }
        }
        public string CardExpiryYear
        {
            get => _existingCredential?.CardExpiryYear ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.CardExpiryYear = value; this.RaisePropertyChanged(); } }
        }
        public string CardPIN
        {
            get => _existingCredential?.CardPIN ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.CardPIN = value; this.RaisePropertyChanged(); } }
        }
        public string CardPINSecure
        {
            get => _existingCredential?.CardPINSecure ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.CardPINSecure = value; this.RaisePropertyChanged(); } }
        }
        public string CardBillingAddress
        {
            get => _existingCredential?.CardBillingAddress ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.CardBillingAddress = value; this.RaisePropertyChanged(); } }
        }

        // Bank Account properties
        public string BankName
        {
            get => _existingCredential?.BankName ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.BankName = value; this.RaisePropertyChanged(); } }
        }
        public string BankAccountNumber
        {
            get => _existingCredential?.BankAccountNumber ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.BankAccountNumber = value; this.RaisePropertyChanged(); } }
        }
        public string BankRoutingNumber
        {
            get => _existingCredential?.BankRoutingNumber ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.BankRoutingNumber = value; this.RaisePropertyChanged(); } }
        }
        public string BankIBAN
        {
            get => _existingCredential?.BankIBAN ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.BankIBAN = value; this.RaisePropertyChanged(); } }
        }
        public string BankSWIFT
        {
            get => _existingCredential?.BankSWIFT ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.BankSWIFT = value; this.RaisePropertyChanged(); } }
        }
        public string BankAccountType
        {
            get => _existingCredential?.BankAccountType ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.BankAccountType = value; this.RaisePropertyChanged(); } }
        }
        public string BankBranchCode
        {
            get => _existingCredential?.BankBranchCode ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.BankBranchCode = value; this.RaisePropertyChanged(); } }
        }
        public string BankBranchAddress
        {
            get => _existingCredential?.BankBranchAddress ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.BankBranchAddress = value; this.RaisePropertyChanged(); } }
        }

        // PIN Code properties
        public string PinLabel
        {
            get => _existingCredential?.PinLabel ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.PinLabel = value; this.RaisePropertyChanged(); } }
        }
        public string PinValue
        {
            get => _existingCredential?.PinValue ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.PinValue = value; this.RaisePropertyChanged(); } }
        }
        public string PinCategory
        {
            get => _existingCredential?.PinCategory ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.PinCategory = value; this.RaisePropertyChanged(); } }
        }
        public string PinIssuer
        {
            get => _existingCredential?.PinIssuer ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.PinIssuer = value; this.RaisePropertyChanged(); } }
        }

        // Identity Document properties
        public string IdDocumentType
        {
            get => _existingCredential?.IdDocumentType ?? string.Empty;
            set
            {
                if (_existingCredential != null)
                {
                    _existingCredential.IdDocumentType = value;
                    this.RaisePropertyChanged();
                    this.RaisePropertyChanged(nameof(IdNumberLabel));
                    this.RaisePropertyChanged(nameof(IdNumberWatermark));
                    this.RaisePropertyChanged(nameof(IdIssuingCountryLabel));
                    this.RaisePropertyChanged(nameof(IdIssuingStateLabel));
                    this.RaisePropertyChanged(nameof(IdIssuingCountryWatermark));
                    this.RaisePropertyChanged(nameof(IdIssuingStateWatermark));
                    this.RaisePropertyChanged(nameof(ShowIdIssuingCountry));
                    this.RaisePropertyChanged(nameof(ShowIdIssuingState));
                    this.RaisePropertyChanged(nameof(ShowIdIssueDate));
                    this.RaisePropertyChanged(nameof(ShowIdExpiryDate));
                }
            }
        }
        public string IdNumber
        {
            get => _existingCredential?.IdNumber ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.IdNumber = value; this.RaisePropertyChanged(); } }
        }
        public string IdCardNumber
        {
            get => _existingCredential?.IdCardNumber ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.IdCardNumber = value; this.RaisePropertyChanged(); } }
        }
        public string IdIssuingCountry
        {
            get => _existingCredential?.IdIssuingCountry ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.IdIssuingCountry = value; this.RaisePropertyChanged(); } }
        }
        public string IdIssuingState
        {
            get => _existingCredential?.IdIssuingState ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.IdIssuingState = value; this.RaisePropertyChanged(); } }
        }
        public DateTimeOffset? IdIssueDate
        {
            get => _existingCredential?.IdIssueDate;
            set { if (_existingCredential != null) { _existingCredential.IdIssueDate = value; this.RaisePropertyChanged(); } }
        }
        public DateTimeOffset? IdExpiryDate
        {
            get => _existingCredential?.IdExpiryDate;
            set { if (_existingCredential != null) { _existingCredential.IdExpiryDate = value; this.RaisePropertyChanged(); } }
        }

        public string IdNumberLabel => GetIdentityTypeKey() switch
        {
            "passport" => "Passport Number",
            "driver licence" => "Licence Number",
            "medicare card" => "Medicare Number",
            "birth certificate" => "Registration Number",
            "proof of age card" => "Card Number",
            "concession card" => "Card Number",
            "citizenship certificate" => "Certificate Number",
            _ => "ID Number"
        };

        public string IdNumberWatermark => GetIdentityTypeKey() switch
        {
            "passport" => "N1234567",
            "driver licence" => "12345678",
            "medicare card" => "1234 56789 0",
            "birth certificate" => "2024/123456",
            "proof of age card" => "PA123456",
            "concession card" => "HCC 123 456 789",
            "citizenship certificate" => "20240123456",
            _ => "ABC123456"
        };

        public string IdIssuingCountryLabel => "Issuing Country";
        public string IdIssuingStateLabel => GetIdentityTypeKey() switch
        {
            "birth certificate" => "Issuing State / Registry",
            _ => "Issuing State / Province"
        };

        public string IdIssuingCountryWatermark => "Australia";
        public string IdIssuingStateWatermark => "NSW";

        public bool ShowIdIssuingCountry => GetIdentityTypeKey() switch
        {
            "driver licence" => false,
            "medicare card" => false,
            "proof of age card" => false,
            "concession card" => false,
            _ => true
        };

        public bool ShowIdIssuingState => GetIdentityTypeKey() switch
        {
            "passport" => false,
            _ => true
        };

        public bool ShowIdIssueDate => GetIdentityTypeKey() switch
        {
            "medicare card" => false,
            "proof of age card" => false,
            "concession card" => false,
            _ => true
        };

        public bool ShowIdExpiryDate => GetIdentityTypeKey() switch
        {
            "birth certificate" => false,
            _ => true
        };

        // WiFi properties
        public string WiFiSSID
        {
            get => _existingCredential?.WiFiSSID ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.WiFiSSID = value; this.RaisePropertyChanged(); } }
        }
        public string WiFiSecurityType
        {
            get => _existingCredential?.WiFiSecurityType ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.WiFiSecurityType = value; this.RaisePropertyChanged(); } }
        }
        public string WiFiBSSID
        {
            get => _existingCredential?.WiFiBSSID ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.WiFiBSSID = value; this.RaisePropertyChanged(); } }
        }

        public string WiFiPassword
        {
            get => _existingCredential?.WiFiPassword ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.WiFiPassword = value; this.RaisePropertyChanged(); } }
        }

        // API Key properties
        public string ApiKeyValue
        {
            get => _existingCredential?.ApiKeyValue ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.ApiKeyValue = value; this.RaisePropertyChanged(); } }
        }
        public string ApiKeyType
        {
            get => _existingCredential?.ApiKeyType ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.ApiKeyType = value; this.RaisePropertyChanged(); } }
        }
        public string ApiEndpoint
        {
            get => _existingCredential?.ApiEndpoint ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.ApiEndpoint = value; this.RaisePropertyChanged(); } }
        }
        public string ApiEnvironment
        {
            get => _existingCredential?.ApiEnvironment ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.ApiEnvironment = value; this.RaisePropertyChanged(); } }
        }
        public string ApiDocumentationUrl
        {
            get => _existingCredential?.ApiDocumentationUrl ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.ApiDocumentationUrl = value; this.RaisePropertyChanged(); } }
        }

        // Contact properties
        public string ContactFullName
        {
            get => _existingCredential?.ContactFullName ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.ContactFullName = value; this.RaisePropertyChanged(); } }
        }
        public string ContactEmail
        {
            get => _existingCredential?.ContactEmail ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.ContactEmail = value; this.RaisePropertyChanged(); } }
        }
        public string ContactPhone
        {
            get => _existingCredential?.ContactPhone ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.ContactPhone = value; this.RaisePropertyChanged(); } }
        }
        public string ContactAddress
        {
            get => _existingCredential?.ContactAddress ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.ContactAddress = value; this.RaisePropertyChanged(); } }
        }
        public string ContactCompany
        {
            get => _existingCredential?.ContactCompany ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.ContactCompany = value; this.RaisePropertyChanged(); } }
        }
        public string ContactJobTitle
        {
            get => _existingCredential?.ContactJobTitle ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.ContactJobTitle = value; this.RaisePropertyChanged(); } }
        }

        // TOTP authenticator fields
        public string TotpSecretInput
        {
            get => _totpSecretInput;
            set
            {
                this.RaiseAndSetIfChanged(ref _totpSecretInput, value);
                this.RaisePropertyChanged(nameof(HasTotpSecret));
            }
        }

        public bool HasTotpSecret => !string.IsNullOrWhiteSpace(TotpSecretInput);

        public string TotpSecret
        {
            get => _existingCredential?.TotpSecret ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.TotpSecret = value; this.RaisePropertyChanged(); } }
        }
        public int TotpDigits
        {
            get => _existingCredential?.TotpDigits ?? 6;
            set { if (_existingCredential != null) { _existingCredential.TotpDigits = value; this.RaisePropertyChanged(); } }
        }
        public int TotpTimeStep
        {
            get => _existingCredential?.TotpTimeStep ?? 30;
            set { if (_existingCredential != null) { _existingCredential.TotpTimeStep = value; this.RaisePropertyChanged(); } }
        }
        public string TotpAlgorithm
        {
            get => _existingCredential?.TotpAlgorithm ?? "SHA1";
            set { if (_existingCredential != null) { _existingCredential.TotpAlgorithm = value; this.RaisePropertyChanged(); } }
        }
        public string TotpIssuer
        {
            get => _existingCredential?.TotpIssuer ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.TotpIssuer = value; this.RaisePropertyChanged(); } }
        }
        public string TotpAccountName
        {
            get => _existingCredential?.TotpAccountName ?? string.Empty;
            set { if (_existingCredential != null) { _existingCredential.TotpAccountName = value; this.RaisePropertyChanged(); } }
        }

        // Commands
        public ReactiveCommand<Unit, Unit> SaveCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }
        public ReactiveCommand<Unit, Unit> TogglePasswordVisibilityCommand { get; }
        public ReactiveCommand<Unit, Unit> GeneratePasswordCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenPasswordGeneratorCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenIconPickerCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenIconLibraryCommand { get; }
        public ReactiveCommand<string, Unit> SetIconCommand { get; }
        public ReactiveCommand<Color, Unit> SelectColorCommand { get; }
        public ReactiveCommand<Unit, Unit> GenerateTotpSecretCommand { get; }
        public ReactiveCommand<Unit, Unit> RemoveTotpSecretCommand { get; }
        public ReactiveCommand<Unit, Unit> ImportFromOtpAuthCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenTotpSettingsCommand { get; }

        // Methods
        private void InitializeCategories()
        {
            _categories.Add(new CategoryViewModel { Name = "Logins", Icon = IconPathMigrator.LoginsIcon });
            _categories.Add(new CategoryViewModel { Name = "Credit Cards", Icon = IconPathMigrator.PaymentIcon });
            _categories.Add(new CategoryViewModel { Name = "Secure Notes", Icon = IconPathMigrator.NotesIcon });
            _categories.Add(new CategoryViewModel { Name = "Banking", Icon = IconPathMigrator.BankingIcon });
            _categories.Add(new CategoryViewModel { Name = "Personal", Icon = IconPathMigrator.PersonalIcon });
            _categories.Add(new CategoryViewModel { Name = "WiFi", Icon = IconPathMigrator.WiFiIcon });
            _categories.Add(new CategoryViewModel { Name = "ID", Icon = IconPathMigrator.IdIcon });
            _categories.Add(new CategoryViewModel { Name = "Notes", Icon = IconPathMigrator.NotesIcon });
            _categories.Add(new CategoryViewModel { Name = "Custom", Icon = IconPathMigrator.CustomIcon });
        }

        private string GetIdentityTypeKey()
        {
            return (IdDocumentType ?? string.Empty).Trim().ToLowerInvariant();
        }

        private bool ValidateForm()
        {
            bool isValid = true;

            if (string.IsNullOrWhiteSpace(Title))
            {
                TitleError = "Title is required";
                isValid = false;
            }

            if (IsPasswordEntry && !IsSecureNoteEntry)
            {
                if (string.IsNullOrWhiteSpace(Username))
                {
                    UsernameError = "Username or email is required";
                    isValid = false;
                }
                else
                {
                    UsernameError = string.Empty;
                }
            }
            else
            {
                UsernameError = string.Empty;
            }

            var passwordRequired = ShowPasswordField;
            if (passwordRequired)
            {
                if (string.IsNullOrWhiteSpace(Password))
                {
                    PasswordError = IsWiFiEntry ? "Network password is required" : "Password is required";
                    isValid = false;
                }
                else if (IsPasswordEntry && Password.Length < 8)
                {
                    PasswordError = "Password must be at least 8 characters";
                    isValid = false;
                }
                else
                {
                    PasswordError = string.Empty;
                }
            }
            else
            {
                PasswordError = string.Empty;
            }

            return isValid;
        }

        private void Save()
        {
            if (!ValidateForm())
            {
                return;
            }

            var credential = _existingCredential ?? new Credential();

            credential.Title = Title.Trim();
            credential.Username = Username.Trim();
            credential.Password = Password;
            credential.Url = Url.Trim();
            credential.Notes = Notes.Trim();
            credential.Icon = Icon.Trim();
            // Save selected icon color as hex string on the model
            try
            {
                credential.IconColor = SelectedIconColor.ToString();
            }
            catch
            {
                credential.IconColor = string.Empty;
            }
            credential.Group = SelectedCategory?.Name ?? "Logins";

            if (IsSecureNoteEntry)
            {
                credential.Username = string.Empty;
                credential.Password = string.Empty;
                credential.Url = string.Empty;
                credential.Group = "Secure Notes";
            }

            // Parse tags
            if (!string.IsNullOrWhiteSpace(TagsText))
            {
                credential.Tags = TagsText
                    .Split(',')
                    .Select(t => t.Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();
            }
            else
            {
                credential.Tags = new List<string>();
            }

            credential.CustomFields ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(_passwordFlagValue))
            {
                credential.CustomFields[PasswordStrengthHelper.PasswordFlagFieldKey] = _passwordFlagValue;
            }
            else if (credential.CustomFields.ContainsKey(PasswordStrengthHelper.PasswordFlagFieldKey))
            {
                credential.CustomFields.Remove(PasswordStrengthHelper.PasswordFlagFieldKey);
            }

            // Set expiry
            credential.ExpiryUtc = HasExpiryDate ? ExpiryDate : null;

            // Copy entry type from existing credential if present, or use the current EntryType
            credential.EntryType = _existingCredential?.EntryType ?? EntryType.Password;

            // Copy type-specific fields if _existingCredential was provided and modified
            // (These properties bind directly to _existingCredential, so they're already updated)
            // However, for new credentials where _existingCredential starts null, we need to copy from it
            if (_existingCredential != null)
            {
                // Credit Card fields
                credential.CardNumber = _existingCredential.CardNumber;
                credential.CardholderName = _existingCredential.CardholderName;
                credential.CardType = _existingCredential.CardType;
                credential.CardCVV = _existingCredential.CardCVV;
                credential.CardExpiryMonth = _existingCredential.CardExpiryMonth;
                credential.CardExpiryYear = _existingCredential.CardExpiryYear;
                credential.CardPIN = _existingCredential.CardPIN;
                credential.CardBillingAddress = _existingCredential.CardBillingAddress;

                // Bank Account fields
                credential.BankName = _existingCredential.BankName;
                credential.BankAccountNumber = _existingCredential.BankAccountNumber;
                credential.BankRoutingNumber = _existingCredential.BankRoutingNumber;
                credential.BankIBAN = _existingCredential.BankIBAN;
                credential.BankSWIFT = _existingCredential.BankSWIFT;
                credential.BankAccountType = _existingCredential.BankAccountType;
                credential.BankBranchCode = _existingCredential.BankBranchCode;
                credential.BankBranchAddress = _existingCredential.BankBranchAddress;

                // Identity fields
                credential.IdDocumentType = _existingCredential.IdDocumentType;
                credential.IdNumber = _existingCredential.IdNumber;
                credential.IdCardNumber = _existingCredential.IdCardNumber;
                credential.IdIssuingCountry = _existingCredential.IdIssuingCountry;
                credential.IdIssuingState = _existingCredential.IdIssuingState;
                credential.IdIssueDate = _existingCredential.IdIssueDate;
                credential.IdExpiryDate = _existingCredential.IdExpiryDate;

                // WiFi fields
                credential.WiFiSSID = _existingCredential.WiFiSSID;
                credential.WiFiSecurityType = _existingCredential.WiFiSecurityType;
                credential.WiFiBSSID = _existingCredential.WiFiBSSID;

                // API Key fields
                credential.ApiKeyValue = _existingCredential.ApiKeyValue;
                credential.ApiKeyType = _existingCredential.ApiKeyType;
                credential.ApiEndpoint = _existingCredential.ApiEndpoint;
                credential.ApiEnvironment = _existingCredential.ApiEnvironment;
                credential.ApiDocumentationUrl = _existingCredential.ApiDocumentationUrl;

                // Contact fields
                credential.ContactFullName = _existingCredential.ContactFullName;
                credential.ContactEmail = _existingCredential.ContactEmail;
                credential.ContactPhone = _existingCredential.ContactPhone;
                credential.ContactAddress = _existingCredential.ContactAddress;
                credential.ContactCompany = _existingCredential.ContactCompany;
                credential.ContactJobTitle = _existingCredential.ContactJobTitle;

                // Passkey flag
                credential.IsPasskey = _existingCredential.IsPasskey;

                // TOTP fields
                credential.TotpSecret = _existingCredential.TotpSecret;
                credential.TotpDigits = _existingCredential.TotpDigits;
                credential.TotpTimeStep = _existingCredential.TotpTimeStep;
                credential.TotpAlgorithm = _existingCredential.TotpAlgorithm;
                credential.TotpIssuer = _existingCredential.TotpIssuer;
                credential.TotpAccountName = _existingCredential.TotpAccountName;

                // PIN Code fields
                credential.PinLabel = _existingCredential.PinLabel;
                credential.PinValue = _existingCredential.PinValue;
                credential.PinCategory = _existingCredential.PinCategory;
                credential.PinIssuer = _existingCredential.PinIssuer;
            }

            // Apply TOTP secret from input field
            if (!string.IsNullOrWhiteSpace(TotpSecretInput))
            {
                credential.TotpSecret = TotpSecretInput.Trim().ToUpperInvariant();
            }
            else
            {
                // Clear TOTP if secret was removed
                credential.TotpSecret = string.Empty;
            }

            // Update timestamps
            if (_existingCredential == null)
            {
                credential.CreatedUtc = DateTimeOffset.UtcNow;
            }
            credential.LastUpdatedUtc = DateTimeOffset.UtcNow;

            _onSave?.Invoke(credential);
            _ownerWindow?.Close(true);
        }

        private void Cancel()
        {
            _ownerWindow?.Close(false);
        }

        private void TogglePasswordVisibility()
        {
            IsPasswordVisible = !IsPasswordVisible;
            PasswordChar = IsPasswordVisible ? '\0' : '●';
            PasswordVisibilityIcon = IsPasswordVisible ? "🙈" : "👁";
            PasswordVisibilitySvgIcon = IsPasswordVisible ? "Assets/SVG/Current/Hidden eye.svg" : "Assets/SVG/Current/Visible eye.svg";
        }

        private void GeneratePassword()
        {
            // Generate a strong random password
            const string upperChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            const string lowerChars = "abcdefghijklmnopqrstuvwxyz";
            const string digitChars = "0123456789";
            const string symbolChars = "!@#$%^&*()_+-=[]{}|;:,.<>?";
            var password = new System.Text.StringBuilder();

            // Ensure at least one of each type
            password.Append(upperChars[RandomNumberGenerator.GetInt32(upperChars.Length)]);
            password.Append(lowerChars[RandomNumberGenerator.GetInt32(lowerChars.Length)]);
            password.Append(digitChars[RandomNumberGenerator.GetInt32(digitChars.Length)]);
            password.Append(symbolChars[RandomNumberGenerator.GetInt32(symbolChars.Length)]);

            // Fill remaining with random characters
            string allChars = upperChars + lowerChars + digitChars + symbolChars;
            for (int i = 4; i < 16; i++)
            {
                password.Append(allChars[RandomNumberGenerator.GetInt32(allChars.Length)]);
            }

            // Shuffle the password
            var chars = password.ToString().ToCharArray();
            for (int i = chars.Length - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1);
                (chars[i], chars[j]) = (chars[j], chars[i]);
            }

            Password = new string(chars);
        }

        private void UpdatePasswordStrength()
        {
            var info = PasswordStrengthHelper.Evaluate(Password);

            PasswordStrength = info.Progress;
            _passwordFlagValue = info.Label;

            Dispatcher.UIThread.Post(() =>
            {
                if (!info.HasValue)
                {
                    PasswordStrengthText = string.Empty;
                    PasswordStrengthColor = Brushes.Gray;
                }
                else
                {
                    PasswordStrengthText = info.Label;
                    PasswordStrengthColor = info.CreateBrush();
                }

                ShowPasswordFlag = info.ShouldShowFlag;
                PasswordFlagText = info.ShouldShowFlag ? info.FlagText : string.Empty;
                PasswordFlagBackground = info.ShouldShowFlag ? info.CreateBadgeBrush() : Brushes.Transparent;
            });
        }

        private async System.Threading.Tasks.Task OpenPasswordGeneratorAsync()
        {
            if (_ownerWindow == null) return;

            var viewModel = new PasswordGeneratorViewModel();
            var window = new PasswordGeneratorWindow
            {
                DataContext = viewModel
            };
            viewModel.SetOwnerWindow(window);

            await window.ShowDialog(_ownerWindow);

            // If a password was generated, use it
            if (!string.IsNullOrEmpty(viewModel.GeneratedPassword) &&
                !viewModel.GeneratedPassword.StartsWith("Please select"))
            {
                Password = viewModel.GeneratedPassword;
            }
        }

        private void SetIcon(string icon)
        {
            Icon = icon;
            ShowQuickPicks = false; // Hide quick picks once an icon is selected
        }

        private static void LogIconPicker(string msg)
        {
            try
            {
                var logPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory ?? ".", "logs", "icon_picker_debug.log");
                var dir = System.IO.Path.GetDirectoryName(logPath);
                if (dir != null && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:O}] [VM] {msg}\n");
            }
            catch { /* logging must never crash UI */ }
        }

        private async System.Threading.Tasks.Task OpenIconPickerAsync()
        {
            try
            {
                LogIconPicker("OpenIconPickerAsync CALLED (from Command)");
                LogIconPicker($"_ownerWindow type: {_ownerWindow?.GetType().Name ?? "NULL"}");

                var viewModel = new IconPickerViewModel(Icon, SelectedIconColor);
                var window = new IconPickerWindow
                {
                    DataContext = viewModel,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };
                viewModel.SetOwnerWindow(window);

                // Find the best owner window
                Window? ownerToUse = _ownerWindow;
                if (ownerToUse == null)
                {
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                        ownerToUse = desktop.MainWindow;
                }

                LogIconPicker($"ownerToUse: {ownerToUse?.GetType().Name ?? "NULL"}");

                string? result = null;
                if (ownerToUse != null)
                {
                    LogIconPicker("Calling ShowDialog...");
                    result = await window.ShowDialog<string?>(ownerToUse);
                }
                else
                {
                    LogIconPicker("No owner, calling Show() non-modal...");
                    var tcs = new System.Threading.Tasks.TaskCompletionSource<string?>();
                    window.Closed += (_, _) => tcs.TrySetResult(viewModel.SelectedIcon);
                    window.Show();
                    result = await tcs.Task;
                }

                LogIconPicker($"Dialog closed with result: '{result ?? "null"}'");

                if (!string.IsNullOrEmpty(result))
                {
                    Icon = result;
                    SelectedIconColor = viewModel.SelectedIconColor;
                    ShowQuickPicks = false;
                    LogIconPicker($"Icon updated to: '{Icon}', color: '{SelectedIconColor}'");
                }
            }
            catch (Exception ex)
            {
                LogIconPicker($"EXCEPTION: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private async System.Threading.Tasks.Task OpenIconLibraryAsync()
        {
            await IconLibraryLauncher.ShowAsync(_ownerWindow, "Icon Library");
        }

        public void SetOwnerWindow(Window window)
        {
            Debug.WriteLine($"[INIT] SetOwnerWindow called with: {window?.GetType().Name ?? "null"}");
            _ownerWindow = window;
            Debug.WriteLine($"[INIT] Owner window is now: {(_ownerWindow == null ? "NULL" : "SET")}");
        }

        private void SelectColor(Color color)
        {
            SelectedIconColor = color;
        }

        /// <summary>
        /// Attempts to auto-detect an icon based on the current Title and Url
        /// </summary>
        private void UpdateAutoDetectedIcon()
        {
            Debug.WriteLine($"[AUTO-DETECT] UpdateAutoDetectedIcon called - Title: '{Title}', Url: '{Url}'");

            if (_iconManager == null)
            {
                Debug.WriteLine("[AUTO-DETECT] IconManager is null - cannot auto-detect");
                HasAutoDetectedIcon = false;
                UpdateIconInitials();
                return;
            }

            // Create a temporary credential for icon detection
            var tempCredential = new Credential
            {
                Title = Title,
                Url = Url
            };

            try
            {
                Debug.WriteLine("[AUTO-DETECT] Calling FindIconPathForCredential...");
                var iconPath = _iconManager.FindIconPathForCredential(tempCredential);
                Debug.WriteLine($"[AUTO-DETECT] IconManager returned: '{iconPath ?? "null"}'");

                if (!string.IsNullOrEmpty(iconPath))
                {
                    var fileExists = System.IO.File.Exists(iconPath);
                    Debug.WriteLine($"[AUTO-DETECT] File.Exists('{iconPath}'): {fileExists}");

                    if (fileExists)
                    {
                        AutoDetectedIconPath = iconPath;
                        HasAutoDetectedIcon = true;
                        Debug.WriteLine($"[AUTO-DETECT] ✅ Icon detected! Path: {iconPath}");

                        // Create Bitmap on UI thread so Avalonia can render it.
                        // Update: avoid blocking the UI thread (GetAwaiter().GetResult) which can deadlock
                        try
                        {
                            if (Dispatcher.UIThread.CheckAccess())
                            {
                                try
                                {
                                    AutoDetectedIconBitmap = new Bitmap(iconPath);
                                    Debug.WriteLine("[AUTO-DETECT] AutoDetectedIconBitmap created on UI thread (direct).");
                                }
                                catch (Exception bmpEx)
                                {
                                    AutoDetectedIconBitmap = null;
                                    Debug.WriteLine($"[AUTO-DETECT] Failed to create Bitmap: {bmpEx.Message}");
                                }
                            }
                            else
                            {
                                // Post the creation to the UI thread without blocking
                                Dispatcher.UIThread.Post(() =>
                                {
                                    try
                                    {
                                        AutoDetectedIconBitmap = new Bitmap(iconPath);
                                        Debug.WriteLine("[AUTO-DETECT] AutoDetectedIconBitmap created on UI thread (posted).");
                                    }
                                    catch (Exception bmpEx)
                                    {
                                        AutoDetectedIconBitmap = null;
                                        Debug.WriteLine($"[AUTO-DETECT] Failed to create Bitmap (posted): {bmpEx.Message}");
                                    }
                                });
                            }
                        }
                        catch (Exception dispEx)
                        {
                            AutoDetectedIconBitmap = null;
                            Debug.WriteLine($"[AUTO-DETECT] Dispatcher operation failed: {dispEx.Message}");
                        }
                    }
                    else
                    {
                        AutoDetectedIconPath = null;
                        AutoDetectedIconBitmap = null;
                        HasAutoDetectedIcon = false;
                        Debug.WriteLine("[AUTO-DETECT] ❌ Icon path returned but file doesn't exist");
                    }
                }
                else
                {
                    AutoDetectedIconPath = null;
                    AutoDetectedIconBitmap = null;
                    HasAutoDetectedIcon = false;
                    Debug.WriteLine("[AUTO-DETECT] ❌ No icon path returned");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AUTO-DETECT] ⚠️ Exception: {ex.Message}");
                AutoDetectedIconPath = null;
                AutoDetectedIconBitmap = null;
                HasAutoDetectedIcon = false;
            }

            UpdateIconInitials();
            Debug.WriteLine($"[AUTO-DETECT] Final state - HasAutoDetectedIcon: {HasAutoDetectedIcon}, IconInitials: '{IconInitials}'");
        }

        /// <summary>
        /// Updates the initials displayed when no icon image is available
        /// </summary>
        private void UpdateIconInitials()
        {
            if (HasAutoDetectedIcon)
            {
                IconInitials = string.Empty;
                return;
            }

            // Generate initials from title
            if (!string.IsNullOrWhiteSpace(Title))
            {
                var words = Title.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length >= 2)
                {
                    IconInitials = $"{words[0][0]}{words[1][0]}".ToUpper();
                }
                else if (words.Length == 1 && words[0].Length >= 2)
                {
                    IconInitials = words[0].Substring(0, 2).ToUpper();
                }
                else if (words.Length == 1)
                {
                    IconInitials = words[0][0].ToString().ToUpper();
                }
                else
                {
                    IconInitials = "?";
                }
            }
            else
            {
                IconInitials = "?";
            }
        }

        private void GenerateTotpSecret()
        {
            TotpSecretInput = PhantomVault.Core.Services.TotpService.GenerateSecret();
            if (_existingCredential != null)
            {
                _existingCredential.TotpSecret = TotpSecretInput;
            }
            this.RaisePropertyChanged(nameof(TotpSecret));
            this.RaisePropertyChanged(nameof(HasTotpSecret));
        }

        private void RemoveTotpSecretInput()
        {
            TotpSecretInput = string.Empty;
            if (_existingCredential != null)
            {
                _existingCredential.TotpSecret = string.Empty;
            }
            this.RaisePropertyChanged(nameof(TotpSecret));
            this.RaisePropertyChanged(nameof(HasTotpSecret));
        }

        /// <summary>
        /// Opens the TOTP Scanner Dialog to configure advanced TOTP settings
        /// (issuer, algorithm, digits, period) for this credential.
        /// </summary>
        private async Task OpenTotpSettingsAsync()
        {
            try
            {
                if (_ownerWindow == null) return;

                var viewModel = new TotpScannerViewModel
                {
                    Issuer = _existingCredential?.TotpIssuer ?? string.Empty,
                    AccountName = _existingCredential?.TotpAccountName ?? string.Empty,
                    SecretKey = TotpSecretInput,
                    Digits = _existingCredential?.TotpDigits ?? 6,
                    Period = _existingCredential?.TotpTimeStep ?? 30,
                    Algorithm = _existingCredential?.TotpAlgorithm ?? "SHA1",
                    IsEditing = HasTotpSecret
                };

                var dialog = new PhantomVault.UI.Views.TotpScannerDialog(viewModel)
                {
                    Title = HasTotpSecret
                        ? "TOTP Settings – " + (Title ?? "Entry")
                        : "Add TOTP – " + (Title ?? "Entry")
                };

                var result = await dialog.ShowDialog<TotpScanResult?>(_ownerWindow);

                if (result != null && result.Success)
                {
                    if (result.Deleted)
                    {
                        RemoveTotpSecretInput();
                        return;
                    }

                    TotpSecretInput = result.Secret;
                    if (_existingCredential != null)
                    {
                        _existingCredential.TotpSecret = result.Secret;
                        _existingCredential.TotpIssuer = result.Issuer;
                        _existingCredential.TotpAccountName = result.AccountName;
                        _existingCredential.TotpDigits = result.Digits;
                        _existingCredential.TotpTimeStep = result.Period;
                        _existingCredential.TotpAlgorithm = result.Algorithm;
                    }

                    this.RaisePropertyChanged(nameof(TotpSecret));
                    this.RaisePropertyChanged(nameof(HasTotpSecret));
                    this.RaisePropertyChanged(nameof(TotpIssuer));
                    this.RaisePropertyChanged(nameof(TotpAccountName));
                    this.RaisePropertyChanged(nameof(TotpDigits));
                    this.RaisePropertyChanged(nameof(TotpTimeStep));
                    this.RaisePropertyChanged(nameof(TotpAlgorithm));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] OpenTotpSettingsAsync: {ex.Message}");
            }
        }

        private async Task ImportFromOtpAuthAsync()
        {
            try
            {
                // Get clipboard content
                var clipboard = TopLevel.GetTopLevel(_ownerWindow)?.Clipboard;
                if (clipboard == null) return;

#pragma warning disable CS0618 // Type or member is obsolete
                var text = await clipboard.GetTextAsync();
#pragma warning restore CS0618
                if (string.IsNullOrWhiteSpace(text)) return;

                // Parse otpauth:// URL
                if (!text.StartsWith("otpauth://totp/", StringComparison.OrdinalIgnoreCase))
                {
                    // Show error message - invalid format
                    return;
                }

                var uri = new Uri(text);
                var pathParts = uri.AbsolutePath.TrimStart('/').Split(':');

                // Extract issuer and account name
                if (pathParts.Length >= 2)
                {
                    TotpIssuer = Uri.UnescapeDataString(pathParts[0]);
                    TotpAccountName = Uri.UnescapeDataString(pathParts[1]);
                }
                else if (pathParts.Length == 1)
                {
                    TotpAccountName = Uri.UnescapeDataString(pathParts[0]);
                }

                // Parse query parameters
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

                var secret = query["secret"];
                if (!string.IsNullOrWhiteSpace(secret))
                {
                    TotpSecret = secret;
                }

                var issuer = query["issuer"];
                if (!string.IsNullOrWhiteSpace(issuer) && string.IsNullOrWhiteSpace(TotpIssuer))
                {
                    TotpIssuer = issuer;
                }

                var digits = query["digits"];
                if (!string.IsNullOrWhiteSpace(digits) && int.TryParse(digits, out int digitCount))
                {
                    TotpDigits = digitCount;
                }

                var period = query["period"];
                if (!string.IsNullOrWhiteSpace(period) && int.TryParse(period, out int timeStep))
                {
                    TotpTimeStep = timeStep;
                }

                var algorithm = query["algorithm"];
                if (!string.IsNullOrWhiteSpace(algorithm))
                {
                    TotpAlgorithm = algorithm.ToUpper();
                }

                // Set title if not already set
                if (string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(TotpIssuer))
                {
                    Title = TotpIssuer;
                }

                this.RaisePropertyChanged(nameof(TotpSecret));
                this.RaisePropertyChanged(nameof(TotpIssuer));
                this.RaisePropertyChanged(nameof(TotpAccountName));
                this.RaisePropertyChanged(nameof(TotpDigits));
                this.RaisePropertyChanged(nameof(TotpTimeStep));
                this.RaisePropertyChanged(nameof(TotpAlgorithm));
                this.RaisePropertyChanged(nameof(Title));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TOTP] Failed to import from otpauth:// URL: {ex.Message}");
            }
        }
    }
}
