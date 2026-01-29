using System;
using System.Threading;
using ReactiveUI;
using PhantomVault.Core.Models;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PhantomVault.UI.Services;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model wrapper for a credential with UI-specific properties.
    /// </summary>
    public sealed class CredentialViewModel : ReactiveObject
    {
        private readonly Credential _credential;
        private bool _isFavorite;
        private IBrush? _iconBackgroundBrush;
        private Color _iconColor;
        private Bitmap? _autoDetectedIconBitmap;
        private bool _hasAutoDetectedIcon;
        private string _passwordFlagText = string.Empty;
        private IBrush _passwordFlagBackground = Brushes.Transparent;
        private bool _showPasswordFlag;
        private string _passwordFlagValue = string.Empty;
        private string _currentTotpCode = string.Empty;
        private int _totpSecondsRemaining;
        private Timer? _totpTimer;

        public CredentialViewModel(Credential credential)
        {
            _credential = credential ?? throw new ArgumentNullException(nameof(credential));
            // Initialize icon color/brush from model if available
            try
            {
                if (!string.IsNullOrEmpty(_credential.IconColor))
                {
                    _iconColor = Color.Parse(_credential.IconColor);
                    _iconBackgroundBrush = new SolidColorBrush(_iconColor);
                }
                else
                {
                    _iconColor = Colors.Transparent;
                    _iconBackgroundBrush = new SolidColorBrush(Colors.Transparent);
                }
            }
            catch
            {
                _iconColor = Colors.Transparent;
                _iconBackgroundBrush = new SolidColorBrush(Colors.Transparent);
            }

            // Attempt to load an image icon from the Assets/Icons folder
            try
            {
                var iconsDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Icons");
                var iconManager = new PhantomVault.Core.Services.IconManager(iconsDir);
                var path = iconManager.FindIconPathForCredential(_credential);
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    _autoDetectedIconBitmap = new Bitmap(path);
                    _hasAutoDetectedIcon = true;
                }
                else
                {
                    _autoDetectedIconBitmap = null;
                    _hasAutoDetectedIcon = false;
                }
            }
            catch
            {
                _autoDetectedIconBitmap = null;
                _hasAutoDetectedIcon = false;
            }

            UpdatePasswordFlagState();

            // Initialize TOTP timer if this is a TOTP entry
            if (_credential.EntryType == EntryType.TotpGenerator && !string.IsNullOrWhiteSpace(_credential.TotpSecret))
            {
                UpdateTotpCode();
                _totpTimer = new Timer(_ => UpdateTotpCode(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            }
        }

        public string Title => _credential.Title;
        public string Username => _credential.Username;
        public string Password => _credential.Password;
        public string Url => _credential.Url;
        public string Notes => _credential.Notes;
        public string Group => _credential.Group;
        public string Icon => _credential.Icon;
        public EntryType EntryType => _credential.EntryType;
        public bool IsPasswordEntry => EntryType == EntryType.Password;
        public bool IsWiFiEntry => EntryType == EntryType.WiFi;
        public bool IsIdentityEntry => EntryType == EntryType.Identity;
        public bool IsApiKeyEntry => EntryType == EntryType.ApiKey;
        public bool IsContactEntry => EntryType == EntryType.Contact;
        public bool IsCreditCardEntry => EntryType == EntryType.CreditCard;
        public bool IsBankAccountEntry => EntryType == EntryType.BankAccount;
        public bool IsTotpEntry => EntryType == EntryType.TotpGenerator;
        public bool HasUsername => !string.IsNullOrWhiteSpace(Username);
        public bool HasPassword => !string.IsNullOrWhiteSpace(Password);

        /// <summary>
        /// Gets the icon path for the credential's category by looking it up in the vault manifest.
        /// Returns null if category not found or has no icon.
        /// </summary>
        public string? CategoryIcon
        {
            get
            {
                // This will be populated by the VaultViewModel when categories are available
                // For now, return null as a placeholder
                return null;
            }
        }

        public Bitmap? AutoDetectedIconBitmap => _autoDetectedIconBitmap;
        public bool HasAutoDetectedIcon => _hasAutoDetectedIcon;
        public Color IconColor => _iconColor;
        public IBrush? IconBackgroundBrush => _iconBackgroundBrush;
        public DateTimeOffset CreatedUtc => _credential.CreatedUtc;
        public DateTimeOffset LastUpdatedUtc => _credential.LastUpdatedUtc;
        public DateTimeOffset? ExpiryUtc => _credential.ExpiryUtc;

        public bool IsFavorite
        {
            get => _isFavorite;
            set
            {
                this.RaiseAndSetIfChanged(ref _isFavorite, value);
                // Notify the FavoriteIcon text and any other dependent UI
                this.RaisePropertyChanged(nameof(FavoriteIcon));
            }
        }

        public bool HasCustomIcon => !string.IsNullOrEmpty(GetDisplayIcon());

        public string DisplayIcon => GetDisplayIcon();

        private string GetDisplayIcon()
        {
            // Priority 1: Use explicitly set icon
            if (!string.IsNullOrEmpty(Icon))
            {
                return Icon;
            }

            // If we found an image for this credential, return empty string
            // so the UI knows to render the image instead of a text icon.
            if (HasAutoDetectedIcon)
            {
                return string.Empty;
            }

            // Fallback: attempt to return an emoji fallback
            try
            {
                var iconsDir = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Assets", "Icons"
                );

                var iconManager = new PhantomVault.Core.Services.IconManager(iconsDir);
                var autoIcon = iconManager.FindIconForCredential(_credential);

                if (!string.IsNullOrEmpty(autoIcon))
                {
                    return autoIcon;
                }
            }
            catch
            {
                // Ignore errors in auto-detection
            }

            // Priority 3: No icon found
            return string.Empty;
        }

        public string IconText
        {
            get
            {
                if (!string.IsNullOrEmpty(Title))
                {
                    return Title.Substring(0, Math.Min(2, Title.Length)).ToUpper();
                }
                return "??";
            }
        }

        public string FavoriteIcon => IsFavorite ? "⭐" : "☆";

        public bool HasGroup => !string.IsNullOrEmpty(Group);
        public bool HasUrl => !string.IsNullOrEmpty(Url);
        public bool HasNotes => !string.IsNullOrEmpty(Notes);
        public string DetailLine1 => GetDetailLines().line1;
        public string DetailLine2 => GetDetailLines().line2;
        public bool HasDetailLine2 => !string.IsNullOrWhiteSpace(DetailLine2);

        // Credit card details
        public string CardholderName => _credential.CardholderName;
        public string CardNumber => _credential.CardNumber;
        public string MaskedCardNumber => MaskSensitiveValue(CardNumber);
        public string CardType => _credential.CardType;
        public string CardExpiryMonth => _credential.CardExpiryMonth;
        public string CardExpiryYear => _credential.CardExpiryYear;
        public string CardExpiryText => FormatExpiry(CardExpiryMonth, CardExpiryYear);
        public string CardCVV => _credential.CardCVV;
        public string MaskedCardCVV => MaskSensitiveValue(CardCVV, 0);
        public string CardPIN => _credential.CardPIN;
        public string MaskedCardPIN => MaskSensitiveValue(CardPIN, 0);
        public string CardBillingAddress => _credential.CardBillingAddress;
        public bool HasCardholderName => !string.IsNullOrWhiteSpace(CardholderName);
        public bool HasCardNumber => !string.IsNullOrWhiteSpace(CardNumber);
        public bool HasCardType => !string.IsNullOrWhiteSpace(CardType);
        public bool HasCardExpiry => !string.IsNullOrWhiteSpace(CardExpiryText);
        public bool HasCardCVV => !string.IsNullOrWhiteSpace(CardCVV);
        public bool HasCardPIN => !string.IsNullOrWhiteSpace(CardPIN);
        public bool HasCardBillingAddress => !string.IsNullOrWhiteSpace(CardBillingAddress);

        // Bank account details
        public string BankName => _credential.BankName;
        public string BankAccountNumber => _credential.BankAccountNumber;
        public string MaskedBankAccountNumber => MaskBankAccount(BankAccountNumber);
        public string BankRoutingNumber => _credential.BankRoutingNumber;
        public string MaskedBankRoutingNumber => MaskSensitiveValue(BankRoutingNumber, 2);
        public string BankIBAN => _credential.BankIBAN;
        public string MaskedBankIban => MaskSensitiveValue(BankIBAN, 4);
        public string BankSWIFT => _credential.BankSWIFT;
        public string MaskedBankSwift => MaskSensitiveValue(BankSWIFT, 4);
        public string BankAccountType => _credential.BankAccountType;
        public string BankBranchCode => _credential.BankBranchCode;
        public string BankBranchAddress => _credential.BankBranchAddress;
        public bool HasBankName => !string.IsNullOrWhiteSpace(BankName);
        public bool HasBankAccountNumber => !string.IsNullOrWhiteSpace(BankAccountNumber);
        public bool HasBankRoutingNumber => !string.IsNullOrWhiteSpace(BankRoutingNumber);
        public bool HasBankIban => !string.IsNullOrWhiteSpace(BankIBAN);
        public bool HasBankSwift => !string.IsNullOrWhiteSpace(BankSWIFT);
        public bool HasBankAccountType => !string.IsNullOrWhiteSpace(BankAccountType);
        public bool HasBankBranchCode => !string.IsNullOrWhiteSpace(BankBranchCode);
        public bool HasBankBranchAddress => !string.IsNullOrWhiteSpace(BankBranchAddress);

        // WiFi details
        public string WiFiSSID => _credential.WiFiSSID;
        public string WiFiSecurityType => _credential.WiFiSecurityType;
        public string WiFiBSSID => _credential.WiFiBSSID;
        public string WiFiPassword => _credential.WiFiPassword;
        public string MaskedWiFiPassword => MaskApiKey(WiFiPassword);
        public bool HasWiFiSSID => !string.IsNullOrWhiteSpace(WiFiSSID);
        public bool HasWiFiSecurity => !string.IsNullOrWhiteSpace(WiFiSecurityType);
        public bool HasWiFiBssid => !string.IsNullOrWhiteSpace(WiFiBSSID);
        public bool HasWiFiPassword => !string.IsNullOrWhiteSpace(WiFiPassword);

        // Identity details
        public string IdDocumentType => _credential.IdDocumentType;
        public string IdNumber => _credential.IdNumber;
        public string IdIssuingCountry => _credential.IdIssuingCountry;
        public string IdIssuingState => _credential.IdIssuingState;
        public DateTimeOffset? IdIssueDate => _credential.IdIssueDate;
        public DateTimeOffset? IdExpiryDate => _credential.IdExpiryDate;
        public string IdIssueDateText => FormatDate(IdIssueDate);
        public string IdExpiryDateText => FormatDate(IdExpiryDate);
        public bool HasIdDocumentType => !string.IsNullOrWhiteSpace(IdDocumentType);
        public bool HasIdNumber => !string.IsNullOrWhiteSpace(IdNumber);
        public bool HasIdIssuingCountry => !string.IsNullOrWhiteSpace(IdIssuingCountry);
        public bool HasIdIssuingState => !string.IsNullOrWhiteSpace(IdIssuingState);
        public bool HasIdIssueDate => IdIssueDate.HasValue;
        public bool HasIdExpiryDate => IdExpiryDate.HasValue;

        // API key details
        public string ApiKeyValue => _credential.ApiKeyValue;
        public string MaskedApiKeyValue => MaskApiKey(ApiKeyValue);
        public string ApiEndpoint => _credential.ApiEndpoint;
        public string ApiEnvironment => _credential.ApiEnvironment;
        public string ApiDocumentationUrl => _credential.ApiDocumentationUrl;
        public bool HasApiKeyValue => !string.IsNullOrWhiteSpace(ApiKeyValue);
        public bool HasApiEndpoint => !string.IsNullOrWhiteSpace(ApiEndpoint);

        // TOTP authenticator details
        public string TotpSecret => _credential.TotpSecret;
        public int TotpDigits => _credential.TotpDigits;
        public int TotpTimeStep => _credential.TotpTimeStep;
        public string TotpAlgorithm => _credential.TotpAlgorithm;
        public string TotpIssuer => _credential.TotpIssuer;
        public string TotpAccountName => _credential.TotpAccountName;
        public bool HasTotpSecret => !string.IsNullOrWhiteSpace(TotpSecret);
        public bool HasTotpIssuer => !string.IsNullOrWhiteSpace(TotpIssuer);
        public bool HasTotpAccountName => !string.IsNullOrWhiteSpace(TotpAccountName);

        public string CurrentTotpCode
        {
            get => _currentTotpCode;
            private set
            {
                if (_currentTotpCode != value)
                {
                    _currentTotpCode = value;
                    this.RaisePropertyChanged();
                }
            }
        }

        public int TotpSecondsRemaining
        {
            get => _totpSecondsRemaining;
            private set
            {
                if (_totpSecondsRemaining != value)
                {
                    _totpSecondsRemaining = value;
                    this.RaisePropertyChanged();
                    this.RaisePropertyChanged(nameof(TotpProgressPercent));
                }
            }
        }

        public double TotpProgressPercent => TotpTimeStep > 0 ? (double)TotpSecondsRemaining / TotpTimeStep * 100 : 0;
        public bool HasApiEnvironment => !string.IsNullOrWhiteSpace(ApiEnvironment);
        public bool HasApiDocumentation => !string.IsNullOrWhiteSpace(ApiDocumentationUrl);

        // Contact details
        public string ContactFullName => _credential.ContactFullName;
        public string ContactEmail => _credential.ContactEmail;
        public string ContactPhone => _credential.ContactPhone;
        public string ContactAddress => _credential.ContactAddress;
        public string ContactCompany => _credential.ContactCompany;
        public string ContactJobTitle => _credential.ContactJobTitle;
        public bool HasContactName => !string.IsNullOrWhiteSpace(ContactFullName);
        public bool HasContactEmail => !string.IsNullOrWhiteSpace(ContactEmail);
        public bool HasContactPhone => !string.IsNullOrWhiteSpace(ContactPhone);
        public bool HasContactAddress => !string.IsNullOrWhiteSpace(ContactAddress);
        public bool HasContactCompany => !string.IsNullOrWhiteSpace(ContactCompany);
        public bool HasContactJobTitle => !string.IsNullOrWhiteSpace(ContactJobTitle);

        public string Created => CreatedUtc.ToString("MMM dd, yyyy");
        public string LastModified => LastUpdatedUtc.ToString("MMM dd, yyyy");

        public string TagsText => _credential.Tags != null && _credential.Tags.Count > 0
            ? string.Join(", ", _credential.Tags)
            : "No tags";

        public string ExpiryText => ExpiryUtc.HasValue
            ? ExpiryUtc.Value.ToString("MMM dd, yyyy")
            : "Never expires";

        public string PasswordFlagText => _passwordFlagText;

        public IBrush PasswordFlagBackground => _passwordFlagBackground;

        public bool ShowPasswordFlag => _showPasswordFlag;

        public string PasswordFlagValue => _passwordFlagValue;

        public Credential GetCredential() => _credential;

        /// <summary>
        /// Refresh raises property change notifications for all derived
        /// properties so views bound to this view model update when the
        /// underlying model is modified externally (for example while an
        /// edit dialog is open).
        /// </summary>
        public void Refresh()
        {
            UpdatePasswordFlagState();
            this.RaisePropertyChanged(nameof(Title));
            this.RaisePropertyChanged(nameof(Username));
            this.RaisePropertyChanged(nameof(Password));
            this.RaisePropertyChanged(nameof(Url));
            this.RaisePropertyChanged(nameof(Notes));
            this.RaisePropertyChanged(nameof(Group));
            this.RaisePropertyChanged(nameof(Icon));
            this.RaisePropertyChanged(nameof(CreatedUtc));
            this.RaisePropertyChanged(nameof(LastUpdatedUtc));
            this.RaisePropertyChanged(nameof(ExpiryUtc));
            this.RaisePropertyChanged(nameof(HasCustomIcon));
            this.RaisePropertyChanged(nameof(DisplayIcon));
            this.RaisePropertyChanged(nameof(IconText));
            this.RaisePropertyChanged(nameof(IconColor));
            this.RaisePropertyChanged(nameof(IconBackgroundBrush));
            this.RaisePropertyChanged(nameof(HasGroup));
            this.RaisePropertyChanged(nameof(HasUrl));
            this.RaisePropertyChanged(nameof(PasswordFlagText));
            this.RaisePropertyChanged(nameof(PasswordFlagBackground));
            this.RaisePropertyChanged(nameof(ShowPasswordFlag));
            this.RaisePropertyChanged(nameof(PasswordFlagValue));
            this.RaisePropertyChanged(nameof(DetailLine1));
            this.RaisePropertyChanged(nameof(DetailLine2));
            this.RaisePropertyChanged(nameof(HasDetailLine2));
            this.RaisePropertyChanged(nameof(IsPasswordEntry));
            this.RaisePropertyChanged(nameof(IsWiFiEntry));
            this.RaisePropertyChanged(nameof(IsIdentityEntry));
            this.RaisePropertyChanged(nameof(IsApiKeyEntry));
            this.RaisePropertyChanged(nameof(IsContactEntry));
            this.RaisePropertyChanged(nameof(IsCreditCardEntry));
            this.RaisePropertyChanged(nameof(IsBankAccountEntry));
            this.RaisePropertyChanged(nameof(IsTotpEntry));
            this.RaisePropertyChanged(nameof(HasUsername));
            this.RaisePropertyChanged(nameof(HasPassword));
            this.RaisePropertyChanged(nameof(CardholderName));
            this.RaisePropertyChanged(nameof(CardNumber));
            this.RaisePropertyChanged(nameof(MaskedCardNumber));
            this.RaisePropertyChanged(nameof(CardType));
            this.RaisePropertyChanged(nameof(CardExpiryText));
            this.RaisePropertyChanged(nameof(CardCVV));
            this.RaisePropertyChanged(nameof(MaskedCardCVV));
            this.RaisePropertyChanged(nameof(CardPIN));
            this.RaisePropertyChanged(nameof(MaskedCardPIN));
            this.RaisePropertyChanged(nameof(CardBillingAddress));
            this.RaisePropertyChanged(nameof(HasCardholderName));
            this.RaisePropertyChanged(nameof(HasCardNumber));
            this.RaisePropertyChanged(nameof(HasCardType));
            this.RaisePropertyChanged(nameof(HasCardExpiry));
            this.RaisePropertyChanged(nameof(HasCardCVV));
            this.RaisePropertyChanged(nameof(HasCardPIN));
            this.RaisePropertyChanged(nameof(HasCardBillingAddress));
            this.RaisePropertyChanged(nameof(BankName));
            this.RaisePropertyChanged(nameof(BankAccountNumber));
            this.RaisePropertyChanged(nameof(MaskedBankAccountNumber));
            this.RaisePropertyChanged(nameof(BankRoutingNumber));
            this.RaisePropertyChanged(nameof(MaskedBankRoutingNumber));
            this.RaisePropertyChanged(nameof(BankIBAN));
            this.RaisePropertyChanged(nameof(MaskedBankIban));
            this.RaisePropertyChanged(nameof(BankSWIFT));
            this.RaisePropertyChanged(nameof(MaskedBankSwift));
            this.RaisePropertyChanged(nameof(BankAccountType));
            this.RaisePropertyChanged(nameof(BankBranchCode));
            this.RaisePropertyChanged(nameof(BankBranchAddress));
            this.RaisePropertyChanged(nameof(HasBankName));
            this.RaisePropertyChanged(nameof(HasBankAccountNumber));
            this.RaisePropertyChanged(nameof(HasBankRoutingNumber));
            this.RaisePropertyChanged(nameof(HasBankIban));
            this.RaisePropertyChanged(nameof(HasBankSwift));
            this.RaisePropertyChanged(nameof(HasBankAccountType));
            this.RaisePropertyChanged(nameof(HasBankBranchCode));
            this.RaisePropertyChanged(nameof(HasBankBranchAddress));
            this.RaisePropertyChanged(nameof(WiFiSSID));
            this.RaisePropertyChanged(nameof(WiFiSecurityType));
            this.RaisePropertyChanged(nameof(WiFiBSSID));
            this.RaisePropertyChanged(nameof(WiFiPassword));
            this.RaisePropertyChanged(nameof(MaskedWiFiPassword));
            this.RaisePropertyChanged(nameof(HasWiFiSSID));
            this.RaisePropertyChanged(nameof(HasWiFiSecurity));
            this.RaisePropertyChanged(nameof(HasWiFiBssid));
            this.RaisePropertyChanged(nameof(HasWiFiPassword));
            this.RaisePropertyChanged(nameof(IdDocumentType));
            this.RaisePropertyChanged(nameof(IdNumber));
            this.RaisePropertyChanged(nameof(IdIssuingCountry));
            this.RaisePropertyChanged(nameof(IdIssuingState));
            this.RaisePropertyChanged(nameof(IdIssueDate));
            this.RaisePropertyChanged(nameof(IdExpiryDate));
            this.RaisePropertyChanged(nameof(IdIssueDateText));
            this.RaisePropertyChanged(nameof(IdExpiryDateText));
            this.RaisePropertyChanged(nameof(HasIdDocumentType));
            this.RaisePropertyChanged(nameof(HasIdNumber));
            this.RaisePropertyChanged(nameof(HasIdIssuingCountry));
            this.RaisePropertyChanged(nameof(HasIdIssuingState));
            this.RaisePropertyChanged(nameof(HasIdIssueDate));
            this.RaisePropertyChanged(nameof(HasIdExpiryDate));
            this.RaisePropertyChanged(nameof(ApiKeyValue));
            this.RaisePropertyChanged(nameof(MaskedApiKeyValue));
            this.RaisePropertyChanged(nameof(ApiEndpoint));
            this.RaisePropertyChanged(nameof(ApiEnvironment));
            this.RaisePropertyChanged(nameof(ApiDocumentationUrl));
            this.RaisePropertyChanged(nameof(HasApiKeyValue));
            this.RaisePropertyChanged(nameof(HasApiEndpoint));
            this.RaisePropertyChanged(nameof(HasApiEnvironment));
            this.RaisePropertyChanged(nameof(HasApiDocumentation));
            this.RaisePropertyChanged(nameof(ContactFullName));
            this.RaisePropertyChanged(nameof(ContactEmail));
            this.RaisePropertyChanged(nameof(ContactPhone));
            this.RaisePropertyChanged(nameof(ContactAddress));
            this.RaisePropertyChanged(nameof(ContactCompany));
            this.RaisePropertyChanged(nameof(ContactJobTitle));
            this.RaisePropertyChanged(nameof(HasContactName));
            this.RaisePropertyChanged(nameof(HasContactEmail));
            this.RaisePropertyChanged(nameof(HasContactPhone));
            this.RaisePropertyChanged(nameof(HasContactAddress));
            this.RaisePropertyChanged(nameof(HasContactCompany));
            this.RaisePropertyChanged(nameof(HasContactJobTitle));
        }

        private void UpdatePasswordFlagState()
        {
            _passwordFlagText = string.Empty;
            _passwordFlagBackground = Brushes.Transparent;
            _showPasswordFlag = false;
            _passwordFlagValue = string.Empty;

            if (_credential.CustomFields == null)
            {
                return;
            }

            if (_credential.CustomFields.TryGetValue(PasswordStrengthHelper.PasswordFlagFieldKey, out var storedValue) &&
                PasswordStrengthHelper.TryGetInfoForFlag(storedValue, out var info) &&
                info.ShouldShowFlag)
            {
                _passwordFlagText = info.FlagText;
                _passwordFlagBackground = info.CreateBadgeBrush();
                _showPasswordFlag = true;
                _passwordFlagValue = storedValue ?? string.Empty;
            }
        }

        private (string line1, string line2) GetDetailLines()
        {
            return _credential.EntryType switch
            {
                EntryType.CreditCard =>
                (
                    MaskSensitiveValue(_credential.CardNumber),
                    BuildJoinedParts(_credential.CardType, MaskSensitiveValue(_credential.CardPIN, 0))
                ),
                EntryType.BankAccount =>
                (
                    BuildJoinedParts(_credential.BankName, MaskBankAccount(_credential.BankAccountNumber)),
                    BuildJoinedParts(_credential.BankAccountType, MaskSensitiveValue(_credential.BankRoutingNumber, 2))
                ),
                EntryType.WiFi =>
                (
                    _credential.WiFiSSID,
                    BuildJoinedParts(_credential.WiFiSecurityType, MaskApiKey(_credential.WiFiPassword))
                ),
                EntryType.Identity =>
                (
                    BuildJoinedParts(_credential.IdDocumentType, _credential.IdNumber),
                    BuildJoinedParts(_credential.IdIssuingCountry, _credential.IdIssuingState)
                ),
                EntryType.ApiKey =>
                (
                    MaskApiKey(_credential.ApiKeyValue),
                    BuildJoinedParts(_credential.ApiEnvironment, _credential.ApiEndpoint)
                ),
                EntryType.Contact =>
                (
                    _credential.ContactFullName,
                    BuildJoinedParts(_credential.ContactEmail, _credential.ContactPhone)
                ),
                EntryType.TotpGenerator =>
                (
                    BuildJoinedParts(_credential.TotpIssuer, _credential.TotpAccountName),
                    $"{_credential.TotpDigits} digits · {_credential.TotpTimeStep}s"
                ),
                _ =>
                (
                    _credential.Username,
                    _credential.Url
                )
            };
        }

        private static string BuildJoinedParts(string? left, string? right)
        {
            left = string.IsNullOrWhiteSpace(left) ? string.Empty : left.Trim();
            right = string.IsNullOrWhiteSpace(right) ? string.Empty : right.Trim();

            if (string.IsNullOrEmpty(left))
            {
                return right;
            }

            if (string.IsNullOrEmpty(right))
            {
                return left;
            }

            return $"{left} · {right}";
        }

        private static string MaskSensitiveValue(string? value, int visibleDigits = 4)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var digitsOnly = value.Replace(" ", string.Empty).Replace("-", string.Empty);
            if (digitsOnly.Length <= visibleDigits)
            {
                return digitsOnly;
            }

            var hidden = new string('•', Math.Max(0, digitsOnly.Length - visibleDigits));
            var suffix = digitsOnly[^visibleDigits..];
            return $"{hidden}{suffix}";
        }

        private static string MaskBankAccount(string? accountNumber)
        {
            if (string.IsNullOrWhiteSpace(accountNumber))
            {
                return string.Empty;
            }

            var digitsOnly = accountNumber.Replace(" ", string.Empty);
            if (digitsOnly.Length <= 2)
            {
                return digitsOnly;
            }

            return $"••••{digitsOnly[^2..]}";
        }

        private static string MaskApiKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            var trimmed = key.Trim();
            if (trimmed.Length <= 8)
            {
                return trimmed;
            }

            return $"{trimmed[..4]}••••{trimmed[^4..]}";
        }

        private static string FormatExpiry(string? month, string? year)
        {
            month = string.IsNullOrWhiteSpace(month) ? string.Empty : month.Trim();
            year = string.IsNullOrWhiteSpace(year) ? string.Empty : year.Trim();

            if (string.IsNullOrEmpty(month) && string.IsNullOrEmpty(year))
            {
                return string.Empty;
            }

            if (!string.IsNullOrEmpty(month) && month.Length == 1)
            {
                month = $"0{month}";
            }

            return string.IsNullOrEmpty(year) ? month : string.IsNullOrEmpty(month) ? year : $"{month}/{year}";
        }

        private static string FormatDate(DateTimeOffset? value)
        {
            return value?.ToString("MMM dd, yyyy") ?? string.Empty;
        }

        private void UpdateTotpCode()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_credential.TotpSecret))
                {
                    CurrentTotpCode = string.Empty;
                    TotpSecondsRemaining = 0;
                    return;
                }

                var totpService = new TotpService();
                var code = totpService.GenerateCode(
                    _credential.TotpSecret,
                    DateTimeOffset.UtcNow,
                    _credential.TotpDigits,
                    _credential.TotpTimeStep
                );

                CurrentTotpCode = code;

                // Calculate seconds remaining
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var timeStep = _credential.TotpTimeStep;
                var secondsElapsed = (int)(now % timeStep);
                TotpSecondsRemaining = timeStep - secondsElapsed;
            }
            catch
            {
                CurrentTotpCode = "------";
                TotpSecondsRemaining = 0;
            }
        }

        public void Dispose()
        {
            _totpTimer?.Dispose();
            _totpTimer = null;
        }
    }
}
