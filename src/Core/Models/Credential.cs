using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text.Json.Serialization;
using PhantomVault.Core.Utils;

namespace PhantomVault.Core.Models
{

    public enum EntryType
    {
        Password = 0,
        WiFi = 1,
        Identity = 2,
        ApiKey = 3,
        Contact = 4,
        CreditCard = 5,
        BankAccount = 6,
        TotpGenerator = 7,
        PinCode = 8,

        Blank = 9
    }

    public enum CredentialType
    {
        Login = 0,
        Password = 1,
        WiFi = 2,
        Identity = 3,
        ApiKey = 4,
        Contact = 5,
        CreditCard = 6,
        BankAccount = 7,
        TotpGenerator = 8,
        PinCode = 9,
        Blank = 10
    }

    public sealed class Credential : IDisposable
    {

        public string Id { get; set; } = Guid.NewGuid().ToString();

        public EntryType EntryType { get; set; } = EntryType.Password;

        public CredentialType Type
        {
            get => EntryType switch
            {
                EntryType.WiFi => CredentialType.WiFi,
                EntryType.Identity => CredentialType.Identity,
                EntryType.ApiKey => CredentialType.ApiKey,
                EntryType.Contact => CredentialType.Contact,
                EntryType.CreditCard => CredentialType.CreditCard,
                EntryType.BankAccount => CredentialType.BankAccount,
                EntryType.TotpGenerator => CredentialType.TotpGenerator,
                EntryType.PinCode => CredentialType.PinCode,
                EntryType.Blank => CredentialType.Blank,
                EntryType.Password => CredentialType.Password,
                _ => CredentialType.Password
            };
            set => EntryType = value switch
            {
                CredentialType.WiFi => EntryType.WiFi,
                CredentialType.Identity => EntryType.Identity,
                CredentialType.ApiKey => EntryType.ApiKey,
                CredentialType.Contact => EntryType.Contact,
                CredentialType.CreditCard => EntryType.CreditCard,
                CredentialType.BankAccount => EntryType.BankAccount,
                CredentialType.TotpGenerator => EntryType.TotpGenerator,
                CredentialType.PinCode => EntryType.PinCode,
                CredentialType.Blank => EntryType.Blank,
                CredentialType.Login => EntryType.Password,
                CredentialType.Password => EntryType.Password,
                _ => EntryType.Password
            };
        }

        public string Title { get; set; } = string.Empty;
        public string Username
        {
            get => _secureUsername?.ToUnsecureString() ?? string.Empty;
            set
            {
                _secureUsername?.Dispose();
                _secureUsername = value.ToSecureString();
            }
        }

        public string Password
        {
            get => _securePassword?.ToUnsecureString() ?? string.Empty;
            set
            {
                _securePassword?.Dispose();
                _securePassword = value.ToSecureString();
            }
        }
        public string Url { get; set; } = string.Empty;
        public string Notes
        {
            get => _secureNotes?.ToUnsecureString() ?? string.Empty;
            set
            {
                _secureNotes?.Dispose();
                _secureNotes = value.ToSecureString();
            }
        }
        public string Group { get; set; } = string.Empty;
        public string Category
        {
            get => Group;
            set => Group = value ?? string.Empty;
        }

        public bool IsFavorite { get; set; }
        public string Icon { get; set; } = string.Empty;

        public string IconColor { get; set; } = string.Empty;
        public bool IsPasskey { get; set; } = false;
        public Dictionary<string, string> CustomFields { get; set; } = new();
        public List<string> Tags { get; set; } = new();

        public List<EntrySection> Sections { get; set; } = new();
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? ExpiryUtc { get; set; }

        public DateTime CreatedAt
        {
            get => CreatedUtc.UtcDateTime;
            set => CreatedUtc = value.Kind == DateTimeKind.Utc
                ? new DateTimeOffset(value)
                : new DateTimeOffset(value.ToUniversalTime());
        }

        public DateTime CreatedDate
        {
            get => CreatedAt;
            set => CreatedAt = value;
        }

        public DateTime ModifiedDate
        {
            get => LastUpdatedUtc.UtcDateTime;
            set => LastUpdatedUtc = value.Kind == DateTimeKind.Utc
                ? new DateTimeOffset(value)
                : new DateTimeOffset(value.ToUniversalTime());
        }

        public DateTime? LastAccessedDate
        {
            get => LastUsedUtc;
            set => LastUsedUtc = value;
        }

        public string? AutoTypeSequence { get; set; }
        public DateTime? LastUsedUtc { get; set; }
        public string? PasskeyId { get; set; }

        public string WiFiSSID { get; set; } = string.Empty;
        public string WiFiSecurityType { get; set; } = string.Empty;
        public string WiFiBSSID { get; set; } = string.Empty;
        public string WiFiPassword
        {
            get => _secureWiFiPassword?.ToUnsecureString() ?? string.Empty;
            set
            {
                _secureWiFiPassword?.Dispose();
                _secureWiFiPassword = value.ToSecureString();
            }
        }

        public string IdDocumentType { get; set; } = string.Empty;
        public string IdNumber { get; set; } = string.Empty;
        public string IdCardNumber { get; set; } = string.Empty;
        public string IdIssuingCountry { get; set; } = string.Empty;
        public string IdIssuingState { get; set; } = string.Empty;
        public DateTimeOffset? IdIssueDate { get; set; }
        public DateTimeOffset? IdExpiryDate { get; set; }

        public string ApiKeyValue { get; set; } = string.Empty;
        public string ApiKeyType { get; set; } = string.Empty;
        public string ApiEndpoint { get; set; } = string.Empty;
        public string ApiEnvironment { get; set; } = string.Empty;
        public string ApiDocumentationUrl { get; set; } = string.Empty;

        public string ContactFullName { get; set; } = string.Empty;
        public string ContactEmail { get; set; } = string.Empty;
        public string ContactPhone { get; set; } = string.Empty;
        public string ContactAddress { get; set; } = string.Empty;
        public string ContactCompany { get; set; } = string.Empty;
        public string ContactJobTitle { get; set; } = string.Empty;

        public string CardNumber
        {
            get => _secureCardNumber?.ToUnsecureString() ?? string.Empty;
            set
            {
                _secureCardNumber?.Dispose();
                _secureCardNumber = value.ToSecureString();
            }
        }
        public string CardholderName { get; set; } = string.Empty;
        public string CardType { get; set; } = string.Empty;
        public string CardCVV
        {
            get => _secureCardCvv?.ToUnsecureString() ?? string.Empty;
            set
            {
                _secureCardCvv?.Dispose();
                _secureCardCvv = value.ToSecureString();
            }
        }
        public string CardExpiryMonth { get; set; } = string.Empty;
        public string CardExpiryYear { get; set; } = string.Empty;
        public string CardPIN
        {
            get => _secureCardPin?.ToUnsecureString() ?? string.Empty;
            set
            {
                _secureCardPin?.Dispose();
                _secureCardPin = value.ToSecureString();
            }
        }
        public string CardBillingAddress { get; set; } = string.Empty;

        public string BankName { get; set; } = string.Empty;
        public string BankAccountNumber
        {
            get => _secureBankAccountNumber?.ToUnsecureString() ?? string.Empty;
            set
            {
                _secureBankAccountNumber?.Dispose();
                _secureBankAccountNumber = value.ToSecureString();
            }
        }
        public string BankRoutingNumber
        {
            get => _secureBankRoutingNumber?.ToUnsecureString() ?? string.Empty;
            set
            {
                _secureBankRoutingNumber?.Dispose();
                _secureBankRoutingNumber = value.ToSecureString();
            }
        }
        public string BankIBAN
        {
            get => _secureBankIban?.ToUnsecureString() ?? string.Empty;
            set
            {
                _secureBankIban?.Dispose();
                _secureBankIban = value.ToSecureString();
            }
        }
        public string BankSWIFT
        {
            get => _secureBankSwift?.ToUnsecureString() ?? string.Empty;
            set
            {
                _secureBankSwift?.Dispose();
                _secureBankSwift = value.ToSecureString();
            }
        }
        public string BankAccountType { get; set; } = string.Empty;
        public string BankBranchCode { get; set; } = string.Empty;
        public string BankBranchAddress { get; set; } = string.Empty;

        public string TotpSecret
        {
            get => _secureTotpSecret?.ToUnsecureString() ?? string.Empty;
            set
            {
                _secureTotpSecret?.Dispose();
                _secureTotpSecret = value.ToSecureString();
            }
        }
        public int TotpDigits { get; set; } = 6;
        public int TotpTimeStep { get; set; } = 30;
        public string TotpAlgorithm { get; set; } = "SHA1";
        public string TotpIssuer { get; set; } = string.Empty;
        public string TotpAccountName { get; set; } = string.Empty;

        public string PinLabel { get; set; } = string.Empty;
        public string PinValue
        {
            get => _securePinValue?.ToUnsecureString() ?? string.Empty;
            set
            {
                _securePinValue?.Dispose();
                _securePinValue = value.ToSecureString();
            }
        }
        public string PinCategory { get; set; } = string.Empty;
        public string PinIssuer { get; set; } = string.Empty;

        [JsonIgnore] private SecureString? _secureUsername;
        [JsonIgnore] private SecureString? _securePassword;
        [JsonIgnore] private SecureString? _secureNotes;

        [JsonIgnore] private SecureString? _secureApiKey;
        [JsonIgnore] private SecureString? _secureWiFiPassword;
        [JsonIgnore] private SecureString? _secureCardNumber;
        [JsonIgnore] private SecureString? _secureCardCvv;
        [JsonIgnore] private SecureString? _secureCardPin;
        [JsonIgnore] private SecureString? _secureBankAccountNumber;
        [JsonIgnore] private SecureString? _secureBankRoutingNumber;
        [JsonIgnore] private SecureString? _secureBankIban;
        [JsonIgnore] private SecureString? _secureBankSwift;
        [JsonIgnore] private SecureString? _secureTotpSecret;
        [JsonIgnore] private SecureString? _securePinValue;

        [JsonIgnore]
        public string CardCVVSecure
        {
            get => _secureCardCvv?.ToUnsecureString() ?? CardCVV;
            set
            {
                _secureCardCvv?.Dispose();
                _secureCardCvv = value.ToSecureString();
                CardCVV = value;
            }
        }

        [JsonIgnore]
        public string CardPINSecure
        {
            get => _secureCardPin?.ToUnsecureString() ?? CardPIN;
            set
            {
                _secureCardPin?.Dispose();
                _secureCardPin = value.ToSecureString();
                CardPIN = value;
            }
        }
        public string ApiKeyValueSecure
        {
            get => _secureApiKey?.ToUnsecureString() ?? ApiKeyValue;
            set
            {
                _secureApiKey?.Dispose();
                _secureApiKey = value.ToSecureString();
                ApiKeyValue = value;
            }
        }

        [JsonIgnore]
        public Dictionary<SecureString, SecureString> SecureCustomFields
        {
            get
            {
                var dict = new Dictionary<SecureString, SecureString>();
                foreach (var kvp in CustomFields)
                {
                    dict.Add(kvp.Key.ToSecureString(), kvp.Value.ToSecureString());
                }
                return dict;
            }
            set
            {
                CustomFields.Clear();
                if (value == null) return;
                foreach (var kvp in value)
                {
                    CustomFields[kvp.Key.ToUnsecureString()] = kvp.Value.ToUnsecureString();
                }
            }
        }

        /// <summary>
        /// Copies every value from <paramref name="source"/> onto this credential,
        /// deep-copying the collections.
        ///
        /// This exists because hand-written field lists were scattered across cloning,
        /// merging and export code, and each one silently dropped whichever fields its
        /// author forgot — type-specific data and, later, sections. Anything that copies a
        /// credential should call this so a new field is picked up everywhere at once.
        /// </summary>
        public void CopyValuesFrom(Credential source, bool copyId = true)
        {
            ArgumentNullException.ThrowIfNull(source);

            if (copyId)
                Id = source.Id;

            EntryType = source.EntryType;

            Title = source.Title;
            Username = source.Username;
            Password = source.Password;
            Url = source.Url;
            Notes = source.Notes;
            Group = source.Group;
            Icon = source.Icon;
            IconColor = source.IconColor;
            IsFavorite = source.IsFavorite;
            IsPasskey = source.IsPasskey;
            AutoTypeSequence = source.AutoTypeSequence;
            PasskeyId = source.PasskeyId;

            CreatedUtc = source.CreatedUtc;
            LastUpdatedUtc = source.LastUpdatedUtc;
            ExpiryUtc = source.ExpiryUtc;
            LastUsedUtc = source.LastUsedUtc;

            Tags = source.Tags == null ? new List<string>() : new List<string>(source.Tags);
            CustomFields = source.CustomFields == null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(source.CustomFields);
            Sections = source.Sections == null
                ? new List<EntrySection>()
                : source.Sections.Where(s => s != null).Select(s => s.Clone()).ToList();

            WiFiSSID = source.WiFiSSID;
            WiFiSecurityType = source.WiFiSecurityType;
            WiFiBSSID = source.WiFiBSSID;
            WiFiPassword = source.WiFiPassword;

            IdDocumentType = source.IdDocumentType;
            IdNumber = source.IdNumber;
            IdCardNumber = source.IdCardNumber;
            IdIssuingCountry = source.IdIssuingCountry;
            IdIssuingState = source.IdIssuingState;
            IdIssueDate = source.IdIssueDate;
            IdExpiryDate = source.IdExpiryDate;

            ApiKeyValue = source.ApiKeyValue;
            ApiKeyType = source.ApiKeyType;
            ApiEndpoint = source.ApiEndpoint;
            ApiEnvironment = source.ApiEnvironment;
            ApiDocumentationUrl = source.ApiDocumentationUrl;

            ContactFullName = source.ContactFullName;
            ContactEmail = source.ContactEmail;
            ContactPhone = source.ContactPhone;
            ContactAddress = source.ContactAddress;
            ContactCompany = source.ContactCompany;
            ContactJobTitle = source.ContactJobTitle;

            CardNumber = source.CardNumber;
            CardholderName = source.CardholderName;
            CardType = source.CardType;
            CardCVV = source.CardCVV;
            CardExpiryMonth = source.CardExpiryMonth;
            CardExpiryYear = source.CardExpiryYear;
            CardPIN = source.CardPIN;
            CardBillingAddress = source.CardBillingAddress;

            BankName = source.BankName;
            BankAccountNumber = source.BankAccountNumber;
            BankRoutingNumber = source.BankRoutingNumber;
            BankIBAN = source.BankIBAN;
            BankSWIFT = source.BankSWIFT;
            BankAccountType = source.BankAccountType;
            BankBranchCode = source.BankBranchCode;
            BankBranchAddress = source.BankBranchAddress;

            TotpSecret = source.TotpSecret;
            TotpDigits = source.TotpDigits;
            TotpTimeStep = source.TotpTimeStep;
            TotpAlgorithm = source.TotpAlgorithm;
            TotpIssuer = source.TotpIssuer;
            TotpAccountName = source.TotpAccountName;

            PinLabel = source.PinLabel;
            PinValue = source.PinValue;
            PinCategory = source.PinCategory;
            PinIssuer = source.PinIssuer;
        }

        /// <summary>
        /// A complete independent copy. Pass <paramref name="newId"/> to give the copy its
        /// own identity, for a "keep both" style duplicate.
        /// </summary>
        public Credential Clone(bool newId = false)
        {
            var clone = new Credential();
            clone.CopyValuesFrom(this, copyId: !newId);

            if (newId)
                clone.Id = Guid.NewGuid().ToString();

            return clone;
        }

        public void DisposeSecure()
        {
            _secureUsername?.Dispose();
            _securePassword?.Dispose();
            _secureNotes?.Dispose();
            _secureApiKey?.Dispose();
            _secureWiFiPassword?.Dispose();
            _secureCardNumber?.Dispose();
            _secureCardCvv?.Dispose();
            _secureCardPin?.Dispose();
            _secureBankAccountNumber?.Dispose();
            _secureBankRoutingNumber?.Dispose();
            _secureBankIban?.Dispose();
            _secureBankSwift?.Dispose();
            _secureTotpSecret?.Dispose();
            _securePinValue?.Dispose();
            _secureUsername = null;
            _securePassword = null;
            _secureNotes = null;
            _secureApiKey = null;
            _secureWiFiPassword = null;
            _secureCardNumber = null;
            _secureCardCvv = null;
            _secureCardPin = null;
            _secureBankAccountNumber = null;
            _secureBankRoutingNumber = null;
            _secureBankIban = null;
            _secureBankSwift = null;
            _secureTotpSecret = null;
            _securePinValue = null;

            if (Sections != null)
            {
                foreach (var section in Sections)
                {
                    section?.Dispose();
                }
            }
        }

        public void Dispose() => DisposeSecure();
    }
}

