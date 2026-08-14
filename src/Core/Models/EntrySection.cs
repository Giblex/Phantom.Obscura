using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text.Json.Serialization;
using PhantomVault.Core.Utils;

namespace PhantomVault.Core.Models
{

    public enum EntrySectionKind
    {
        Note = 0,
        PinCode = 1,
        Totp = 2,
        RecoveryEmail = 3,
        RecoveryCodes = 4,
        QrCode = 5,
        Text = 6,
        Secret = 7,
        Url = 8,
        Phone = 9,
        Address = 10,
        Date = 11,
        SecurityQuestion = 12,
        Custom = 99
    }

    public enum EntrySectionSource
    {
        Inline = 0,
        LinkedEntry = 1
    }

    public sealed class EntrySection : IDisposable
    {
        public const string MetaPinLength = "pinLength";
        public const string MetaTotpDigits = "totpDigits";
        public const string MetaTotpPeriod = "totpPeriod";
        public const string MetaTotpAlgorithm = "totpAlgorithm";
        public const string MetaTotpIssuer = "totpIssuer";
        public const string MetaTotpAccount = "totpAccount";
        public const string MetaUsedCodeIndexes = "usedCodeIndexes";
        public const string MetaQrEccLevel = "qrEccLevel";
        public const string MetaSecurityQuestion = "securityQuestion";

        public string Id { get; set; } = Guid.NewGuid().ToString();

        public EntrySectionKind Kind { get; set; } = EntrySectionKind.Text;

        public string Label { get; set; } = string.Empty;

        public string? LinkedEntryId { get; set; }

        [JsonIgnore]
        public EntrySectionSource Source =>
            string.IsNullOrWhiteSpace(LinkedEntryId) ? EntrySectionSource.Inline : EntrySectionSource.LinkedEntry;

        [JsonIgnore]
        public bool IsLinked => Source == EntrySectionSource.LinkedEntry;

        public string Value
        {
            get => _secureValue?.ToUnsecureString() ?? string.Empty;
            set
            {
                _secureValue?.Dispose();
                _secureValue = (value ?? string.Empty).ToSecureString();
            }
        }

        public bool IsSecret { get; set; }

        public int SortOrder { get; set; }

        public Dictionary<string, string> Meta { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonIgnore] private SecureString? _secureValue;

        public static bool KindDefaultsToSecret(EntrySectionKind kind) => kind switch
        {
            EntrySectionKind.PinCode => true,
            EntrySectionKind.Totp => true,
            EntrySectionKind.RecoveryCodes => true,
            EntrySectionKind.Secret => true,
            EntrySectionKind.SecurityQuestion => true,
            _ => false
        };

        public static string DefaultLabel(EntrySectionKind kind) => kind switch
        {
            EntrySectionKind.Note => "Note",
            EntrySectionKind.PinCode => "PIN code",
            EntrySectionKind.Totp => "Authenticator (TOTP)",
            EntrySectionKind.RecoveryEmail => "Recovery email",
            EntrySectionKind.RecoveryCodes => "Recovery codes",
            EntrySectionKind.QrCode => "QR code",
            EntrySectionKind.Text => "Text",
            EntrySectionKind.Secret => "Secret",
            EntrySectionKind.Url => "Link",
            EntrySectionKind.Phone => "Phone",
            EntrySectionKind.Address => "Address",
            EntrySectionKind.Date => "Date",
            EntrySectionKind.SecurityQuestion => "Security question",
            _ => "Section"
        };

        public static EntrySection CreateInline(EntrySectionKind kind, string? label = null, string? value = null)
        {
            return new EntrySection
            {
                Kind = kind,
                Label = string.IsNullOrWhiteSpace(label) ? DefaultLabel(kind) : label!.Trim(),
                Value = value ?? string.Empty,
                IsSecret = KindDefaultsToSecret(kind)
            };
        }

        public static EntrySection CreateLink(EntrySectionKind kind, string linkedEntryId, string? label = null)
        {
            if (string.IsNullOrWhiteSpace(linkedEntryId))
                throw new ArgumentException("Linked entry id is required.", nameof(linkedEntryId));

            return new EntrySection
            {
                Kind = kind,
                LinkedEntryId = linkedEntryId.Trim(),
                Label = string.IsNullOrWhiteSpace(label) ? DefaultLabel(kind) : label!.Trim(),
                IsSecret = KindDefaultsToSecret(kind)
            };
        }

        public static EntrySectionKind KindForEntryType(EntryType entryType) => entryType switch
        {
            EntryType.PinCode => EntrySectionKind.PinCode,
            EntryType.TotpGenerator => EntrySectionKind.Totp,
            EntryType.Contact => EntrySectionKind.RecoveryEmail,
            _ => EntrySectionKind.Custom
        };

        public string? GetMeta(string key)
            => Meta != null && Meta.TryGetValue(key, out var value) ? value : null;

        public int GetMetaInt(string key, int fallback)
            => int.TryParse(GetMeta(key), out var parsed) ? parsed : fallback;

        public void SetMeta(string key, string? value)
        {
            Meta ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(value))
                Meta.Remove(key);
            else
                Meta[key] = value;
        }

        public IReadOnlyList<string> GetRecoveryCodes()
        {
            if (Kind != EntrySectionKind.RecoveryCodes)
                return Array.Empty<string>();

            return Value
                .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .Where(c => c.Length > 0)
                .ToList();
        }

        public HashSet<int> GetUsedRecoveryCodeIndexes()
        {
            var raw = GetMeta(MetaUsedCodeIndexes);
            if (string.IsNullOrWhiteSpace(raw))
                return new HashSet<int>();

            return raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => int.TryParse(p.Trim(), out var i) ? i : -1)
                .Where(i => i >= 0)
                .ToHashSet();
        }

        public void SetUsedRecoveryCodeIndexes(IEnumerable<int> indexes)
        {
            var ordered = (indexes ?? Enumerable.Empty<int>()).Where(i => i >= 0).Distinct().OrderBy(i => i).ToList();
            SetMeta(MetaUsedCodeIndexes, ordered.Count == 0 ? null : string.Join(",", ordered));
        }

        public EntrySection Clone()
        {
            var clone = new EntrySection
            {
                Id = Guid.NewGuid().ToString(),
                Kind = Kind,
                Label = Label,
                LinkedEntryId = LinkedEntryId,
                Value = Value,
                IsSecret = IsSecret,
                SortOrder = SortOrder,
                CreatedUtc = CreatedUtc,
                LastUpdatedUtc = LastUpdatedUtc,
                Meta = Meta == null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(Meta, StringComparer.OrdinalIgnoreCase)
            };
            return clone;
        }

        public string ConsolidationKey()
        {
            var value = IsLinked
                ? $"link:{LinkedEntryId!.Trim().ToUpperInvariant()}"
                : $"inline:{Value.Trim()}";
            return $"{Kind}|{Label.Trim().ToUpperInvariant()}|{value}";
        }

        public void Dispose()
        {
            _secureValue?.Dispose();
            _secureValue = null;
        }
    }
}
