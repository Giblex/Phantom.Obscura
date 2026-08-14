using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using ReactiveUI;
using PhantomVault.Core.Models;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PhantomVault.UI.Services;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.ViewModels
{

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
        private int _passwordStrength;
        private string _passwordStrengthText = string.Empty;
        private IBrush _passwordStrengthColor = Brushes.Gray;
        private string _currentTotpCode = string.Empty;
        private int _totpSecondsRemaining;
        private Timer? _totpTimer;
        private double _totpCodeOpacity = 1.0;
        private double _totpCodeScale = 1.0;

        private static readonly Lazy<IconManager> _sharedIconManager = new(() =>
        {
            var visualsDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Visuals");
            return new IconManager(visualsDir);
        }, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        public CredentialViewModel(Credential credential)
        {
            _credential = credential ?? throw new ArgumentNullException(nameof(credential));

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

            try
            {
                var iconManager = _sharedIconManager.Value;
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
            UpdatePasswordStrengthState();

            if (!string.IsNullOrWhiteSpace(_credential.TotpSecret))
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
        public bool IsPinCodeEntry => EntryType == EntryType.PinCode;
        public bool IsBlankEntry => EntryType == EntryType.Blank;
        public bool HasUsername => !string.IsNullOrWhiteSpace(Username);
        public bool HasPassword => !string.IsNullOrWhiteSpace(Password);

        public string? CategoryIcon
        {
            get
            {

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

                this.RaisePropertyChanged(nameof(FavoriteIcon));
                this.RaisePropertyChanged(nameof(FavoriteIconBrush));
                this.RaisePropertyChanged(nameof(FavoriteIconOpacity));
            }
        }

        public bool HasCustomIcon => !string.IsNullOrEmpty(GetDisplayIcon());

        public string DisplayIcon => GetDisplayIcon();

        private string GetDisplayIcon()
        {

            if (!string.IsNullOrEmpty(Icon))
            {
                return Icon;
            }

            if (HasAutoDetectedIcon)
            {
                return string.Empty;
            }

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

            }

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

        public IBrush FavoriteIconBrush => IsFavorite
            ? new SolidColorBrush(Color.Parse("#FFD700"))
            : new SolidColorBrush(Color.Parse("#AAAAAA"));

        public double FavoriteIconOpacity => IsFavorite ? 1.0 : 0.45;

        public bool HasGroup => !string.IsNullOrEmpty(Group);
        public bool HasUrl => !string.IsNullOrEmpty(Url);
        public bool HasNotes => !string.IsNullOrEmpty(Notes);

        public static Func<string, Credential?>? LinkedEntryResolver { get; set; }

        public static Action<string, string>? SectionCopyHandler { get; set; }

        public static Action<string>? SectionOpenLinkedHandler { get; set; }

        public static Action<Credential>? SectionPersistHandler { get; set; }

        private System.Collections.ObjectModel.ObservableCollection<EntrySectionViewModel>? _sections;

        public System.Collections.ObjectModel.ObservableCollection<EntrySectionViewModel> Sections
            => _sections ??= BuildSections();

        public bool HasSections => _credential.Sections is { Count: > 0 };

        public int SectionCount => _credential.Sections?.Count ?? 0;

        public string SectionsHeader => SectionCount == 1 ? "1 linked section" : $"{SectionCount} linked sections";

        private System.Collections.ObjectModel.ObservableCollection<EntrySectionViewModel> BuildSections()
        {
            var collection = new System.Collections.ObjectModel.ObservableCollection<EntrySectionViewModel>();

            if (_credential.Sections is not { Count: > 0 })
                return collection;

            var service = new EntrySectionService();
            var resolver = LinkedEntryResolver ?? (_ => null);

            foreach (var resolved in service.ResolveAll(_credential, resolver))
            {
                var vm = new EntrySectionViewModel(resolved);
                vm.CopyRequested += (value, label) => SectionCopyHandler?.Invoke(value, label);
                vm.OpenLinkedEntryRequested += id => SectionOpenLinkedHandler?.Invoke(id);
                vm.SectionChanged += _ => SectionPersistHandler?.Invoke(_credential);
                collection.Add(vm);
            }

            return collection;
        }

        /// <summary>
        /// Text from sections that search is allowed to see. Labels are always searchable;
        /// values only when the section is not marked secret, so a hidden PIN or TOTP seed
        /// is never matched by typing it into the search box.
        /// </summary>
        public string SectionSearchText
        {
            get
            {
                if (_credential.Sections is not { Count: > 0 })
                    return string.Empty;

                var parts = new List<string>();

                foreach (var section in _credential.Sections)
                {
                    if (section == null)
                        continue;

                    if (!string.IsNullOrWhiteSpace(section.Label))
                        parts.Add(section.Label);

                    if (!section.IsSecret && !string.IsNullOrWhiteSpace(section.Value))
                        parts.Add(section.Value);
                }

                return string.Join(" ", parts);
            }
        }

        public void RefreshSections()
        {
            if (_sections != null)
            {
                foreach (var section in _sections)
                {
                    section.Dispose();
                }
            }

            _sections = null;
            this.RaisePropertyChanged(nameof(Sections));
            this.RaisePropertyChanged(nameof(HasSections));
            this.RaisePropertyChanged(nameof(SectionCount));
            this.RaisePropertyChanged(nameof(SectionsHeader));
        }
        public string DetailLine1 => GetDetailLines().line1;
        public string DetailLine2 => GetDetailLines().line2;
        public bool HasDetailLine2 => !string.IsNullOrWhiteSpace(DetailLine2);

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

        public string WiFiSSID => _credential.WiFiSSID;
        public string WiFiSecurityType => _credential.WiFiSecurityType;
        public string WiFiBSSID => _credential.WiFiBSSID;
        public string WiFiPassword => _credential.WiFiPassword;
        public string MaskedWiFiPassword => MaskApiKey(WiFiPassword);
        public bool HasWiFiSSID => !string.IsNullOrWhiteSpace(WiFiSSID);
        public bool HasWiFiSecurity => !string.IsNullOrWhiteSpace(WiFiSecurityType);
        public bool HasWiFiBssid => !string.IsNullOrWhiteSpace(WiFiBSSID);
        public bool HasWiFiPassword => !string.IsNullOrWhiteSpace(WiFiPassword);

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

        public string ApiKeyValue => _credential.ApiKeyValue;
        public string ApiKeyType => _credential.ApiKeyType;
        public string MaskedApiKeyValue => MaskApiKey(ApiKeyValue);
        public string ApiEndpoint => _credential.ApiEndpoint;
        public string ApiEnvironment => _credential.ApiEnvironment;
        public string ApiDocumentationUrl => _credential.ApiDocumentationUrl;
        public bool HasApiKeyValue => !string.IsNullOrWhiteSpace(ApiKeyValue);
        public bool HasApiKeyType => !string.IsNullOrWhiteSpace(ApiKeyType);
        public bool HasApiEndpoint => !string.IsNullOrWhiteSpace(ApiEndpoint);

        public string TotpSecret => _credential.TotpSecret;

        /// <summary>
        /// The seed actually in force for this entry: its own, or the first usable TOTP
        /// section. Resolved fresh so it tracks edits to the sections.
        /// </summary>
        private EffectiveTotp? EffectiveTotp
            => CredentialTotpResolver.Resolve(_credential, LinkedEntryResolver ?? (_ => null));
        public int TotpDigits => _credential.TotpDigits;
        public int TotpTimeStep => _credential.TotpTimeStep;
        public string TotpAlgorithm => _credential.TotpAlgorithm;
        public string TotpIssuer => _credential.TotpIssuer;
        public string TotpAccountName => _credential.TotpAccountName;
        public bool HasTotpSecret => EffectiveTotp != null;
        public bool HasTotpIssuer => !string.IsNullOrWhiteSpace(TotpIssuer);
        public bool HasTotpAccountName => !string.IsNullOrWhiteSpace(TotpAccountName);

        public string PinLabel => _credential.PinLabel;
        public string PinValue => _credential.PinValue;
        public string MaskedPinValue => MaskSensitiveValue(PinValue, 0);
        public string PinCategory => _credential.PinCategory;
        public string PinIssuer => _credential.PinIssuer;
        public bool HasPinLabel => !string.IsNullOrWhiteSpace(PinLabel);
        public bool HasPinValue => !string.IsNullOrWhiteSpace(PinValue);
        public bool HasPinCategory => !string.IsNullOrWhiteSpace(PinCategory);
        public bool HasPinIssuer => !string.IsNullOrWhiteSpace(PinIssuer);

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
                    this.RaisePropertyChanged(nameof(TotpTimerArcPath));
                    this.RaisePropertyChanged(nameof(TotpIsExpiring));
                    this.RaisePropertyChanged(nameof(TotpTimerArcPathCompact));
                }
            }
        }

        public double TotpProgressPercent => TotpTimeStep > 0 ? (double)TotpSecondsRemaining / TotpTimeStep * 100 : 0;

        public bool TotpIsExpiring => TotpSecondsRemaining > 0 && TotpSecondsRemaining <= 5;

        public double TotpCodeOpacity
        {
            get => _totpCodeOpacity;
            private set
            {
                if (Math.Abs(_totpCodeOpacity - value) > 0.001)
                {
                    _totpCodeOpacity = value;
                    this.RaisePropertyChanged();
                }
            }
        }

        public double TotpCodeScale
        {
            get => _totpCodeScale;
            private set
            {
                if (Math.Abs(_totpCodeScale - value) > 0.001)
                {
                    _totpCodeScale = value;
                    this.RaisePropertyChanged();
                }
            }
        }

        public Geometry? TotpTimerArcPath
        {
            get => BuildArcGeometry(20, 16, TotpSecondsRemaining, TotpTimeStep);
        }

        public Geometry? TotpTimerArcPathCompact
        {
            get => BuildArcGeometry(16, 12, TotpSecondsRemaining, TotpTimeStep);
        }

        private static Geometry? BuildArcGeometry(double center, double radius, int secondsRemaining, int timeStep)
        {
            if (secondsRemaining <= 0 || timeStep <= 0)
                return null;

            double progress = (double)secondsRemaining / timeStep;
            double angleDegrees = progress * 360.0;

            var startX = center;
            var startY = center - radius;

            string pathData;
            if (angleDegrees >= 359.99)
            {

                pathData = string.Format(
                    CultureInfo.InvariantCulture,
                    "M {0},{1} A {2},{2} 0 1,1 {3},{1}",
                    startX, startY, radius, center - 0.01);
            }
            else
            {
                double angleRadians = (angleDegrees - 90) * Math.PI / 180.0;
                double endX = center + radius * Math.Cos(angleRadians);
                double endY = center + radius * Math.Sin(angleRadians);
                int largeArcFlag = angleDegrees > 180 ? 1 : 0;

                pathData = string.Format(
                    CultureInfo.InvariantCulture,
                    "M {0},{1} A {2},{2} 0 {3},1 {4},{5}",
                    startX, startY, radius, largeArcFlag, endX, endY);
            }

            return StreamGeometry.Parse(pathData);
        }

        public bool HasApiEnvironment => !string.IsNullOrWhiteSpace(ApiEnvironment);
        public bool HasApiDocumentation => !string.IsNullOrWhiteSpace(ApiDocumentationUrl);

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

        public int PasswordStrength => _passwordStrength;

        public string PasswordStrengthText => _passwordStrengthText;

        public IBrush PasswordStrengthColor => _passwordStrengthColor;

        public Credential GetCredential() => _credential;

        public void Refresh()
        {
            UpdatePasswordFlagState();
            UpdatePasswordStrengthState();
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
            this.RaisePropertyChanged(nameof(PasswordStrength));
            this.RaisePropertyChanged(nameof(PasswordStrengthText));
            this.RaisePropertyChanged(nameof(PasswordStrengthColor));
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
            this.RaisePropertyChanged(nameof(IsPinCodeEntry));
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
            this.RaisePropertyChanged(nameof(ApiKeyType));
            this.RaisePropertyChanged(nameof(MaskedApiKeyValue));
            this.RaisePropertyChanged(nameof(ApiEndpoint));
            this.RaisePropertyChanged(nameof(ApiEnvironment));
            this.RaisePropertyChanged(nameof(ApiDocumentationUrl));
            this.RaisePropertyChanged(nameof(HasApiKeyValue));
            this.RaisePropertyChanged(nameof(HasApiKeyType));
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
            this.RaisePropertyChanged(nameof(PinLabel));
            this.RaisePropertyChanged(nameof(PinValue));
            this.RaisePropertyChanged(nameof(MaskedPinValue));
            this.RaisePropertyChanged(nameof(PinCategory));
            this.RaisePropertyChanged(nameof(PinIssuer));
            this.RaisePropertyChanged(nameof(HasPinLabel));
            this.RaisePropertyChanged(nameof(HasPinValue));
            this.RaisePropertyChanged(nameof(HasPinCategory));
            this.RaisePropertyChanged(nameof(HasPinIssuer));
            RefreshSections();
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

        private void UpdatePasswordStrengthState()
        {
            var info = PasswordStrengthHelper.Evaluate(_credential.Password);
            _passwordStrength = info.Progress;
            if (info.HasValue)
            {
                _passwordStrengthText = info.Label;
                _passwordStrengthColor = info.CreateBrush();
            }
            else
            {
                _passwordStrengthText = string.Empty;
                _passwordStrengthColor = Brushes.Gray;
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
                    BuildJoinedParts(_credential.ApiKeyType, _credential.ApiEnvironment)
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
                EntryType.PinCode =>
                (
                    BuildJoinedParts(_credential.PinLabel, _credential.PinIssuer),
                    _credential.PinCategory
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
                var totp = EffectiveTotp;
                if (totp == null)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        CurrentTotpCode = string.Empty;
                        TotpSecondsRemaining = 0;
                    });
                    return;
                }

                var totpService = new TotpService();
                var code = totpService.GenerateCode(
                    totp.Secret,
                    totp.ParsedAlgorithm,
                    DateTimeOffset.UtcNow,
                    totp.Digits,
                    totp.Period
                );

                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var timeStep = totp.Period;
                var secondsElapsed = (int)(now % timeStep);
                var remaining = timeStep - secondsElapsed;

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    var codeChanged = _currentTotpCode != code;
                    CurrentTotpCode = code;
                    TotpSecondsRemaining = remaining;

                    if (codeChanged && !string.IsNullOrEmpty(code))
                    {

                        TotpCodeOpacity = 0.0;
                        TotpCodeScale = 0.85;

                        _ = AnimateTotpCodeInAsync();
                    }
                });
            }
            catch
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    CurrentTotpCode = "------";
                    TotpSecondsRemaining = 0;
                });
            }
        }

        private async System.Threading.Tasks.Task AnimateTotpCodeInAsync()
        {

            await System.Threading.Tasks.Task.Delay(30);
            TotpCodeOpacity = 1.0;
            TotpCodeScale = 1.0;
        }

        public void Dispose()
        {
            _totpTimer?.Dispose();
            _totpTimer = null;
        }
    }
}

