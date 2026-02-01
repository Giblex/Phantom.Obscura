using System;
using System.Collections.Generic;
using System.Security;
using System.Text.Json.Serialization;
using PhantomVault.Core.Utils;

namespace PhantomVault.Core.Models
{
    /// <summary>
    /// Type of credential entry to determine which fields are relevant.
    /// </summary>
    public enum EntryType
    {
        Password = 0,   // Traditional website/app login
        WiFi = 1,       // WiFi network credentials
        Identity = 2,   // ID documents (driver's license, passport, etc.)
        ApiKey = 3,     // API keys and tokens
        Contact = 4,    // Contact information cards
        CreditCard = 5, // Credit/debit cards
        BankAccount = 6,// Bank accounts with EFT support
        TotpGenerator = 7 // TOTP authenticator codes
    }

    /// <summary>
    /// Represents a single credential (e.g. a website login) stored in
    /// the vault. In a full implementation this model would map to
    /// records in an encrypted database. Here it is kept simple for
    /// demonstration purposes.
    /// </summary>
    public sealed class Credential : IDisposable
    {
        // Entry type classification
        public EntryType EntryType { get; set; } = EntryType.Password;

        // Common fields (used by all types)
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
        public string Group { get; set; } = string.Empty; // Folder/category
        public string Icon { get; set; } = string.Empty; // Custom icon emoji or brand name
        // Optional hex color for the icon background (e.g. "#FFB5E5FF")
        public string IconColor { get; set; } = string.Empty;
        public bool IsPasskey { get; set; } = false; // Identifies if this is a passkey vs traditional password
        public Dictionary<string, string> CustomFields { get; set; } = new();
        public List<string> Tags { get; set; } = new();
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? ExpiryUtc { get; set; }

        // Auto-type/Auto-inject fields
        public string? AutoTypeSequence { get; set; } // Custom auto-type sequence (e.g., "{username}{tab}{password}{delay:500}{enter}")
        public DateTime? LastUsedUtc { get; set; } // Timestamp when credential was last used for auto-fill
        public string? PasskeyId { get; set; } // Passkey credential ID if this is a passkey entry

        // WiFi-specific fields (EntryType.WiFi)
        public string WiFiSSID { get; set; } = string.Empty;
        public string WiFiSecurityType { get; set; } = string.Empty; // WPA2, WPA3, WEP, Open, etc.
        public string WiFiBSSID { get; set; } = string.Empty; // MAC address of access point
        public string WiFiPassword
        {
            get => _secureWiFiPassword?.ToUnsecureString() ?? string.Empty;
            set
            {
                _secureWiFiPassword?.Dispose();
                _secureWiFiPassword = value.ToSecureString();
            }
        }

        // Identity document fields (EntryType.Identity)
        public string IdDocumentType { get; set; } = string.Empty; // Passport, Driver's License, Medicare, etc.
        public string IdNumber { get; set; } = string.Empty;
        public string IdCardNumber { get; set; } = string.Empty; // Card/License number for physical documents
        public string IdIssuingCountry { get; set; } = string.Empty;
        public string IdIssuingState { get; set; } = string.Empty;
        public DateTimeOffset? IdIssueDate { get; set; }
        public DateTimeOffset? IdExpiryDate { get; set; }

        // API key fields (EntryType.ApiKey)
        public string ApiKeyValue { get; set; } = string.Empty;
        public string ApiKeyType { get; set; } = string.Empty;
        public string ApiEndpoint { get; set; } = string.Empty;
        public string ApiEnvironment { get; set; } = string.Empty; // Production, Staging, Development
        public string ApiDocumentationUrl { get; set; } = string.Empty;

        // Contact card fields (EntryType.Contact)
        public string ContactFullName { get; set; } = string.Empty;
        public string ContactEmail { get; set; } = string.Empty;
        public string ContactPhone { get; set; } = string.Empty;
        public string ContactAddress { get; set; } = string.Empty;
        public string ContactCompany { get; set; } = string.Empty;
        public string ContactJobTitle { get; set; } = string.Empty;

        // Credit card fields (EntryType.CreditCard)
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
        public string CardType { get; set; } = string.Empty; // Visa, Mastercard, Amex, etc.
        public string CardCVV
        {
            get => _secureCardCvv?.ToUnsecureString() ?? string.Empty;
            set
            {
                _secureCardCvv?.Dispose();
                _secureCardCvv = value.ToSecureString();
            }
        }
        public string CardExpiryMonth { get; set; } = string.Empty; // MM format
        public string CardExpiryYear { get; set; } = string.Empty; // YYYY format
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

        // Bank account fields (EntryType.BankAccount)
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
        } // For US accounts
        public string BankIBAN
        {
            get => _secureBankIban?.ToUnsecureString() ?? string.Empty;
            set
            {
                _secureBankIban?.Dispose();
                _secureBankIban = value.ToSecureString();
            }
        } // International Bank Account Number
        public string BankSWIFT
        {
            get => _secureBankSwift?.ToUnsecureString() ?? string.Empty;
            set
            {
                _secureBankSwift?.Dispose();
                _secureBankSwift = value.ToSecureString();
            }
        } // SWIFT/BIC code
        public string BankAccountType { get; set; } = string.Empty; // Checking, Savings, etc.
        public string BankBranchCode { get; set; } = string.Empty;
        public string BankBranchAddress { get; set; } = string.Empty;

        // TOTP authenticator fields (EntryType.TotpGenerator)
        public string TotpSecret
        {
            get => _secureTotpSecret?.ToUnsecureString() ?? string.Empty;
            set
            {
                _secureTotpSecret?.Dispose();
                _secureTotpSecret = value.ToSecureString();
            }
        } // Base32-encoded secret
        public int TotpDigits { get; set; } = 6; // Usually 6, sometimes 8
        public int TotpTimeStep { get; set; } = 30; // Seconds per code
        public string TotpAlgorithm { get; set; } = "SHA1"; // SHA1, SHA256, or SHA512
        public string TotpIssuer { get; set; } = string.Empty; // e.g., "Google", "Microsoft"
        public string TotpAccountName { get; set; } = string.Empty; // e.g., "user@example.com"

        // Secure in-memory copies of sensitive fields.
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

        /// <summary>
        /// Explicit secure-only accessors for highly sensitive card data.
        /// They mirror the plain properties but avoid binding collisions.
        /// </summary>
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

        /// <summary>
        /// Secure view of custom fields. Values are converted to/from SecureString on demand.
        /// </summary>
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
        }

        public void Dispose() => DisposeSecure();
    }
}
