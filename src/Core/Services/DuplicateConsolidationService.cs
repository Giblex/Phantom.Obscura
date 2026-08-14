using System;
using System.Collections.Generic;
using System.Linq;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services
{

    public sealed class ConsolidationConflict
    {
        public required string FieldName { get; init; }

        public required string KeptValue { get; init; }

        public required IReadOnlyList<string> DiscardedValues { get; init; }

        public string Describe()
            => $"{FieldName}: kept \"{Truncate(KeptValue)}\", discarded {string.Join(", ", DiscardedValues.Select(v => $"\"{Truncate(v)}\""))}";

        private static string Truncate(string value)
            => value.Length <= 40 ? value : value[..37] + "...";
    }

    public sealed class ConsolidationResult
    {
        public required Credential Consolidated { get; init; }

        public required Credential Primary { get; init; }

        public required IReadOnlyList<Credential> Absorbed { get; init; }

        public required IReadOnlyList<ConsolidationConflict> Conflicts { get; init; }

        public bool HasConflicts => Conflicts.Count > 0;

        public string Summary => Absorbed.Count == 0
            ? "Nothing to consolidate."
            : $"Consolidated {Absorbed.Count + 1} entries into \"{Consolidated.Title}\"" +
              (HasConflicts ? $" with {Conflicts.Count} field conflict(s) resolved in favour of the retained entry." : ".");
    }

    public sealed class DuplicateConsolidationService
    {
        public const string NotesSeparator = "\n\n--- merged ---\n";

        /// <summary>
        /// Fields where losing a differing value would be unrecoverable in practice.
        /// When copies disagree on one of these, the retained entry still wins, but the
        /// discarded value is preserved as a section rather than simply dropped — the
        /// absorbed copy only survives until the secure bin is purged.
        /// </summary>
        private static readonly HashSet<string> CriticalFields = new(StringComparer.Ordinal)
        {
            "Password",
            "TOTP secret",
            "PIN value",
            "API key",
            "Card number",
            "Card CVV",
            "Card PIN",
            "Bank account number",
            "Bank routing number",
            "Bank IBAN",
            "Wi-Fi password"
        };

        public ConsolidationResult Consolidate(IReadOnlyList<Credential> group, string? preferredPrimaryId = null)
        {
            ArgumentNullException.ThrowIfNull(group);

            var members = group.Where(c => c != null).ToList();
            if (members.Count == 0)
                throw new ArgumentException("Consolidation requires at least one credential.", nameof(group));

            var primary = SelectPrimary(members, preferredPrimaryId);
            var others = members.Where(c => !ReferenceEquals(c, primary))
                .OrderByDescending(c => c.LastUpdatedUtc)
                .ToList();

            var conflicts = new List<ConsolidationConflict>();
            var merged = ClonePrimary(primary);

            foreach (var (fieldName, get, set) in StringFields(merged))
            {
                MergeStringField(fieldName, merged, primary, others, get, set, conflicts);
            }

            merged.Notes = MergeNotes(primary, others);
            merged.Tags = MergeTags(primary, others);
            merged.CustomFields = MergeCustomFields(primary, others, conflicts);
            merged.Sections = MergeSections(primary, others);
            PreserveCriticalConflicts(merged, conflicts);

            merged.IsFavorite = primary.IsFavorite || others.Any(o => o.IsFavorite);
            merged.IsPasskey = primary.IsPasskey || others.Any(o => o.IsPasskey);
            merged.EntryType = primary.EntryType;

            merged.CreatedUtc = members.Min(c => c.CreatedUtc);
            merged.LastUpdatedUtc = DateTimeOffset.UtcNow;
            merged.ExpiryUtc = primary.ExpiryUtc ?? others.Select(o => o.ExpiryUtc).FirstOrDefault(e => e.HasValue);
            merged.LastUsedUtc = members.Select(c => c.LastUsedUtc).Where(d => d.HasValue).DefaultIfEmpty(null).Max();

            merged.TotpDigits = primary.TotpDigits > 0 ? primary.TotpDigits : 6;
            merged.TotpTimeStep = primary.TotpTimeStep > 0 ? primary.TotpTimeStep : 30;

            return new ConsolidationResult
            {
                Consolidated = merged,
                Primary = primary,
                Absorbed = others,
                Conflicts = conflicts
            };
        }

        public IReadOnlyList<ConsolidationResult> ConsolidateGroups(
            IEnumerable<IReadOnlyList<Credential>> groups,
            IReadOnlyDictionary<string, string>? preferredPrimaryByGroupKey = null)
        {
            ArgumentNullException.ThrowIfNull(groups);

            var results = new List<ConsolidationResult>();
            foreach (var group in groups)
            {
                if (group == null || group.Count < 2)
                    continue;

                string? preferred = null;
                preferredPrimaryByGroupKey?.TryGetValue(BuildGroupKey(group), out preferred);
                results.Add(Consolidate(group, preferred));
            }

            return results;
        }

        public static string BuildGroupKey(IReadOnlyList<Credential> group)
            => string.Join("|", group.Where(c => c != null).Select(c => c.Id).OrderBy(id => id, StringComparer.Ordinal));

        public static Credential SelectPrimary(IReadOnlyList<Credential> members, string? preferredPrimaryId)
        {
            if (!string.IsNullOrWhiteSpace(preferredPrimaryId))
            {
                var explicitPick = members.FirstOrDefault(c =>
                    string.Equals(c.Id, preferredPrimaryId, StringComparison.Ordinal));
                if (explicitPick != null)
                    return explicitPick;
            }

            return members
                .OrderByDescending(InformationScore)
                .ThenByDescending(c => c.LastUpdatedUtc)
                .ThenBy(c => c.Id, StringComparer.Ordinal)
                .First();
        }

        public static int InformationScore(Credential credential)
        {
            var score = 0;

            foreach (var (_, get, _) in StringFields(credential))
            {
                if (!string.IsNullOrWhiteSpace(get()))
                    score++;
            }

            if (!string.IsNullOrWhiteSpace(credential.Notes)) score++;
            if (credential.IsFavorite) score++;
            if (credential.IsPasskey) score++;
            if (credential.ExpiryUtc.HasValue) score++;
            if (credential.Tags?.Count > 0) score += Math.Min(credential.Tags.Count, 5);
            if (credential.CustomFields?.Count > 0) score += Math.Min(credential.CustomFields.Count, 5);
            if (credential.Sections?.Count > 0) score += Math.Min(credential.Sections.Count * 2, 10);

            return score;
        }

        private static Credential ClonePrimary(Credential primary) => primary.Clone();

        private static void MergeStringField(
            string fieldName,
            Credential merged,
            Credential primary,
            IReadOnlyList<Credential> others,
            Func<string> getMerged,
            Action<string> setMerged,
            List<ConsolidationConflict> conflicts)
        {
            var primaryValue = getMerged();
            var otherValues = others
                .Select(o => ValueOf(o, fieldName))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (string.IsNullOrWhiteSpace(primaryValue))
            {
                if (otherValues.Count > 0)
                {
                    setMerged(otherValues[0]);

                    if (otherValues.Count > 1)
                    {
                        conflicts.Add(new ConsolidationConflict
                        {
                            FieldName = fieldName,
                            KeptValue = otherValues[0],
                            DiscardedValues = otherValues.Skip(1).ToList()
                        });
                    }
                }
                return;
            }

            var differing = otherValues
                .Where(v => !string.Equals(v, primaryValue.Trim(), StringComparison.Ordinal))
                .ToList();

            if (differing.Count > 0)
            {
                conflicts.Add(new ConsolidationConflict
                {
                    FieldName = fieldName,
                    KeptValue = primaryValue.Trim(),
                    DiscardedValues = differing
                });
            }
        }

        private static string MergeNotes(Credential primary, IReadOnlyList<Credential> others)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(primary.Notes))
                parts.Add(primary.Notes.Trim());

            foreach (var other in others)
            {
                var note = other.Notes?.Trim();
                if (string.IsNullOrWhiteSpace(note))
                    continue;

                if (parts.Any(p => string.Equals(p, note, StringComparison.Ordinal)))
                    continue;

                parts.Add(note);
            }

            return string.Join(NotesSeparator, parts);
        }

        private static List<string> MergeTags(Credential primary, IReadOnlyList<Credential> others)
        {
            var tags = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tag in (primary.Tags ?? new List<string>()).Concat(others.SelectMany(o => o.Tags ?? new List<string>())))
            {
                var trimmed = tag?.Trim();
                if (string.IsNullOrEmpty(trimmed) || !seen.Add(trimmed))
                    continue;

                tags.Add(trimmed);
            }

            return tags;
        }

        private static Dictionary<string, string> MergeCustomFields(
            Credential primary,
            IReadOnlyList<Credential> others,
            List<ConsolidationConflict> conflicts)
        {
            var merged = new Dictionary<string, string>(
                primary.CustomFields ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase);

            foreach (var other in others)
            {
                foreach (var kvp in other.CustomFields ?? new Dictionary<string, string>())
                {
                    if (!merged.TryGetValue(kvp.Key, out var existing))
                    {
                        merged[kvp.Key] = kvp.Value ?? string.Empty;
                        continue;
                    }

                    if (!string.Equals(existing, kvp.Value, StringComparison.Ordinal))
                    {
                        conflicts.Add(new ConsolidationConflict
                        {
                            FieldName = $"Custom field '{kvp.Key}'",
                            KeptValue = existing ?? string.Empty,
                            DiscardedValues = new[] { kvp.Value ?? string.Empty }
                        });
                    }
                }
            }

            return merged;
        }

        /// <summary>
        /// Appends any discarded value for a critical field as a secret section, so a
        /// consolidation can never silently destroy a password or seed the user still needs.
        /// </summary>
        private static void PreserveCriticalConflicts(Credential merged, IReadOnlyList<ConsolidationConflict> conflicts)
        {
            var existingKeys = new HashSet<string>(
                merged.Sections.Select(s => s.ConsolidationKey()),
                StringComparer.OrdinalIgnoreCase);

            foreach (var conflict in conflicts.Where(c => CriticalFields.Contains(c.FieldName)))
            {
                foreach (var discarded in conflict.DiscardedValues.Where(v => !string.IsNullOrWhiteSpace(v)))
                {
                    var section = EntrySection.CreateInline(
                        EntrySectionKind.Secret,
                        $"{conflict.FieldName} (from merged copy)",
                        discarded);

                    section.IsSecret = true;
                    section.SortOrder = merged.Sections.Count;

                    if (!existingKeys.Add(section.ConsolidationKey()))
                        continue;

                    merged.Sections.Add(section);
                }
            }
        }

        private static List<EntrySection> MergeSections(Credential primary, IReadOnlyList<Credential> others)
        {
            var merged = new List<EntrySection>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var section in (primary.Sections ?? new List<EntrySection>())
                         .Concat(others.SelectMany(o => o.Sections ?? new List<EntrySection>())))
            {
                if (section == null || !seen.Add(section.ConsolidationKey()))
                    continue;

                var clone = section.Clone();
                clone.SortOrder = merged.Count;
                merged.Add(clone);
            }

            return merged;
        }

        private static string ValueOf(Credential credential, string fieldName)
            => StringFields(credential).FirstOrDefault(f => f.Name == fieldName).Get?.Invoke() ?? string.Empty;

        private static IEnumerable<(string Name, Func<string> Get, Action<string> Set)> StringFields(Credential c)
        {
            yield return ("Title", () => c.Title, v => c.Title = v);
            yield return ("Username", () => c.Username, v => c.Username = v);
            yield return ("Password", () => c.Password, v => c.Password = v);
            yield return ("URL", () => c.Url, v => c.Url = v);
            yield return ("Category", () => c.Group, v => c.Group = v);
            yield return ("Icon", () => c.Icon, v => c.Icon = v);
            yield return ("Icon colour", () => c.IconColor, v => c.IconColor = v);

            yield return ("Wi-Fi SSID", () => c.WiFiSSID, v => c.WiFiSSID = v);
            yield return ("Wi-Fi security", () => c.WiFiSecurityType, v => c.WiFiSecurityType = v);
            yield return ("Wi-Fi BSSID", () => c.WiFiBSSID, v => c.WiFiBSSID = v);
            yield return ("Wi-Fi password", () => c.WiFiPassword, v => c.WiFiPassword = v);

            yield return ("ID document type", () => c.IdDocumentType, v => c.IdDocumentType = v);
            yield return ("ID number", () => c.IdNumber, v => c.IdNumber = v);
            yield return ("ID card number", () => c.IdCardNumber, v => c.IdCardNumber = v);
            yield return ("ID issuing country", () => c.IdIssuingCountry, v => c.IdIssuingCountry = v);
            yield return ("ID issuing state", () => c.IdIssuingState, v => c.IdIssuingState = v);

            yield return ("API key", () => c.ApiKeyValue, v => c.ApiKeyValue = v);
            yield return ("API key type", () => c.ApiKeyType, v => c.ApiKeyType = v);
            yield return ("API endpoint", () => c.ApiEndpoint, v => c.ApiEndpoint = v);
            yield return ("API environment", () => c.ApiEnvironment, v => c.ApiEnvironment = v);
            yield return ("API docs URL", () => c.ApiDocumentationUrl, v => c.ApiDocumentationUrl = v);

            yield return ("Contact name", () => c.ContactFullName, v => c.ContactFullName = v);
            yield return ("Contact email", () => c.ContactEmail, v => c.ContactEmail = v);
            yield return ("Contact phone", () => c.ContactPhone, v => c.ContactPhone = v);
            yield return ("Contact address", () => c.ContactAddress, v => c.ContactAddress = v);
            yield return ("Contact company", () => c.ContactCompany, v => c.ContactCompany = v);
            yield return ("Contact job title", () => c.ContactJobTitle, v => c.ContactJobTitle = v);

            yield return ("Card number", () => c.CardNumber, v => c.CardNumber = v);
            yield return ("Cardholder name", () => c.CardholderName, v => c.CardholderName = v);
            yield return ("Card type", () => c.CardType, v => c.CardType = v);
            yield return ("Card CVV", () => c.CardCVV, v => c.CardCVV = v);
            yield return ("Card expiry month", () => c.CardExpiryMonth, v => c.CardExpiryMonth = v);
            yield return ("Card expiry year", () => c.CardExpiryYear, v => c.CardExpiryYear = v);
            yield return ("Card PIN", () => c.CardPIN, v => c.CardPIN = v);
            yield return ("Card billing address", () => c.CardBillingAddress, v => c.CardBillingAddress = v);

            yield return ("Bank name", () => c.BankName, v => c.BankName = v);
            yield return ("Bank account number", () => c.BankAccountNumber, v => c.BankAccountNumber = v);
            yield return ("Bank routing number", () => c.BankRoutingNumber, v => c.BankRoutingNumber = v);
            yield return ("Bank IBAN", () => c.BankIBAN, v => c.BankIBAN = v);
            yield return ("Bank SWIFT", () => c.BankSWIFT, v => c.BankSWIFT = v);
            yield return ("Bank account type", () => c.BankAccountType, v => c.BankAccountType = v);
            yield return ("Bank branch code", () => c.BankBranchCode, v => c.BankBranchCode = v);
            yield return ("Bank branch address", () => c.BankBranchAddress, v => c.BankBranchAddress = v);

            yield return ("TOTP secret", () => c.TotpSecret, v => c.TotpSecret = v);
            yield return ("TOTP algorithm", () => c.TotpAlgorithm, v => c.TotpAlgorithm = v);
            yield return ("TOTP issuer", () => c.TotpIssuer, v => c.TotpIssuer = v);
            yield return ("TOTP account", () => c.TotpAccountName, v => c.TotpAccountName = v);

            yield return ("PIN label", () => c.PinLabel, v => c.PinLabel = v);
            yield return ("PIN value", () => c.PinValue, v => c.PinValue = v);
            yield return ("PIN category", () => c.PinCategory, v => c.PinCategory = v);
            yield return ("PIN issuer", () => c.PinIssuer, v => c.PinIssuer = v);
        }

    }
}
