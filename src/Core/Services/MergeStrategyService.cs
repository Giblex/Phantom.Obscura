using System;
using System.Collections.Generic;
using System.Linq;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services
{

    public enum MergeStrategy
    {
        ReplaceWithNew,
        KeepExisting,
        KeepBoth,
        MergeNotes,
        MergeTags,
        MergeCustomFields,
        MergeAll,
        KeepOlderPassword,
        KeepStrongerPassword
    }

    public sealed class MergeOptions
    {
        public MergeStrategy DefaultStrategy { get; set; } = MergeStrategy.ReplaceWithNew;
        public bool MergeNotes { get; set; } = false;
        public bool MergeTags { get; set; } = false;
        public bool MergeCustomFields { get; set; } = false;
        public bool PreferStrongerPassword { get; set; } = true;
        public bool UpdateTimestamps { get; set; } = true;
        public string NotesSeparator { get; set; } = "\n---\n";
        public Dictionary<string, MergeStrategy> PerCredentialStrategies { get; set; } = new();
    }

    public sealed class MergeStrategyService
    {

        public Credential MergeCredentials(
            Credential existing,
            Credential newCred,
            MergeStrategy strategy,
            MergeOptions? options = null)
        {
            options ??= new MergeOptions();

            // Start from a full copy of the existing entry so every field the strategies
            // below do not explicitly touch — entry type, card, bank, identity, Wi-Fi,
            // API, contact, TOTP, PIN details and sections — survives the merge.
            var merged = existing.Clone();

            switch (strategy)
            {
                case MergeStrategy.ReplaceWithNew:
                    return CloneCredential(newCred, existing.CreatedUtc);

                case MergeStrategy.KeepExisting:
                    return CloneCredential(existing);

                case MergeStrategy.KeepBoth:

                    // This copy is meant to live alongside the existing entry, so it needs
                    // its own identity rather than the imported record's.
                    var bothCred = newCred.Clone(newId: true);
                    bothCred.Title += " (imported)";
                    return bothCred;

                case MergeStrategy.MergeNotes:
                    merged.Password = SelectPassword(existing, newCred, options);
                    merged.Url = SelectNewerValue(existing.Url, newCred.Url, existing.LastUpdatedUtc, DateTimeOffset.UtcNow);
                    merged.Notes = MergeNotesText(existing.Notes, newCred.Notes, options.NotesSeparator);
                    merged.Tags = existing.Tags;
                    merged.CustomFields = new Dictionary<string, string>(existing.CustomFields);
                    break;

                case MergeStrategy.MergeTags:
                    merged.Password = SelectPassword(existing, newCred, options);
                    merged.Url = SelectNewerValue(existing.Url, newCred.Url, existing.LastUpdatedUtc, DateTimeOffset.UtcNow);
                    merged.Notes = SelectNewerValue(existing.Notes, newCred.Notes, existing.LastUpdatedUtc, DateTimeOffset.UtcNow);
                    merged.Tags = MergeTags(existing.Tags, newCred.Tags);
                    merged.CustomFields = new Dictionary<string, string>(existing.CustomFields);
                    break;

                case MergeStrategy.MergeCustomFields:
                    merged.Password = SelectPassword(existing, newCred, options);
                    merged.Url = SelectNewerValue(existing.Url, newCred.Url, existing.LastUpdatedUtc, DateTimeOffset.UtcNow);
                    merged.Notes = SelectNewerValue(existing.Notes, newCred.Notes, existing.LastUpdatedUtc, DateTimeOffset.UtcNow);
                    merged.Tags = existing.Tags;
                    merged.CustomFields = MergeCustomFields(existing.CustomFields, newCred.CustomFields);
                    break;

                case MergeStrategy.MergeAll:
                    merged.Password = SelectPassword(existing, newCred, options);
                    merged.Url = SelectNewerValue(existing.Url, newCred.Url, existing.LastUpdatedUtc, DateTimeOffset.UtcNow);
                    merged.Notes = MergeNotesText(existing.Notes, newCred.Notes, options.NotesSeparator);
                    merged.Tags = MergeTags(existing.Tags, newCred.Tags);
                    merged.CustomFields = MergeCustomFields(existing.CustomFields, newCred.CustomFields);
                    break;

                case MergeStrategy.KeepOlderPassword:
                    merged.Password = existing.Password;
                    merged.Url = newCred.Url;
                    merged.Notes = newCred.Notes;
                    merged.Tags = newCred.Tags;
                    merged.CustomFields = new Dictionary<string, string>(newCred.CustomFields);
                    break;

                case MergeStrategy.KeepStrongerPassword:
                    merged.Password = SelectStrongerPassword(existing.Password, newCred.Password);
                    merged.Url = SelectNewerValue(existing.Url, newCred.Url, existing.LastUpdatedUtc, DateTimeOffset.UtcNow);
                    merged.Notes = SelectNewerValue(existing.Notes, newCred.Notes, existing.LastUpdatedUtc, DateTimeOffset.UtcNow);
                    merged.Tags = MergeTags(existing.Tags, newCred.Tags);
                    merged.CustomFields = MergeCustomFields(existing.CustomFields, newCred.CustomFields);
                    break;
            }

            if (options.UpdateTimestamps)
            {
                merged.LastUpdatedUtc = DateTimeOffset.UtcNow;
            }

            merged.ExpiryUtc = newCred.ExpiryUtc ?? existing.ExpiryUtc;

            return merged;
        }

        public List<Credential> ApplyMergeStrategies(
            List<Credential> allCredentials,
            List<DuplicateInfo> duplicates,
            MergeOptions options)
        {
            var result = new List<Credential>();
            var processedNewCreds = new HashSet<Credential>();
            var duplicateExisting = new HashSet<Credential>(duplicates.Select(d => d.ExistingCredential).Where(c => c != null)!);

            result.AddRange(allCredentials.Where(c => !duplicates.Any(d => d.NewCredential == c)));

            foreach (var duplicate in duplicates)
            {
                if (processedNewCreds.Contains(duplicate.NewCredential))
                    continue;

                var strategy = options.DefaultStrategy;
                var credKey = $"{duplicate.NewCredential.Title}|{duplicate.NewCredential.Username}";
                if (options.PerCredentialStrategies.ContainsKey(credKey))
                {
                    strategy = options.PerCredentialStrategies[credKey];
                }

                if (duplicate.ExistingCredential != null)
                {
                    var merged = MergeCredentials(duplicate.ExistingCredential, duplicate.NewCredential, strategy, options);

                    if (strategy != MergeStrategy.KeepBoth)
                    {
                        result.Add(merged);
                    }
                    else
                    {

                        result.Add(merged);
                    }
                }
                else
                {
                    result.Add(duplicate.NewCredential);
                }

                processedNewCreds.Add(duplicate.NewCredential);
            }

            return result;
        }

        private Credential CloneCredential(Credential source, DateTimeOffset? created = null)
        {
            var clone = source.Clone();

            if (created.HasValue)
                clone.CreatedUtc = created.Value;

            return clone;
        }

        private string SelectPassword(Credential existing, Credential newCred, MergeOptions options)
        {
            if (!options.PreferStrongerPassword)
                return newCred.Password;

            return SelectStrongerPassword(existing.Password, newCred.Password);
        }

        private string SelectStrongerPassword(string? password1, string? password2)
        {
            if (string.IsNullOrEmpty(password1)) return password2 ?? "";
            if (string.IsNullOrEmpty(password2)) return password1;

            var strength1 = CalculateSimpleStrength(password1);
            var strength2 = CalculateSimpleStrength(password2);

            return strength2 > strength1 ? password2 : password1;
        }

        private int CalculateSimpleStrength(string password)
        {
            var score = password.Length;
            if (password.Any(char.IsUpper)) score += 2;
            if (password.Any(char.IsLower)) score += 2;
            if (password.Any(char.IsDigit)) score += 2;
            if (password.Any(c => !char.IsLetterOrDigit(c))) score += 3;
            return score;
        }

        private string SelectNewerValue(string? existing, string? newValue, DateTimeOffset existingTime, DateTimeOffset newTime)
        {
            if (string.IsNullOrEmpty(existing)) return newValue ?? "";
            if (string.IsNullOrEmpty(newValue)) return existing;
            return newTime > existingTime ? newValue : existing;
        }

        private string MergeNotesText(string? existing, string? newNotes, string separator)
        {
            if (string.IsNullOrEmpty(existing)) return newNotes ?? "";
            if (string.IsNullOrEmpty(newNotes)) return existing;
            if (existing.Contains(newNotes)) return existing;
            return existing + separator + newNotes;
        }

        private List<string> MergeTags(List<string> existing, List<string> newTags)
        {
            var merged = new HashSet<string>(existing);
            foreach (var tag in newTags)
            {
                merged.Add(tag);
            }
            return merged.ToList();
        }

        private Dictionary<string, string> MergeCustomFields(Dictionary<string, string> existing, Dictionary<string, string> newFields)
        {
            var merged = new Dictionary<string, string>(existing);
            foreach (var field in newFields)
            {
                if (!merged.ContainsKey(field.Key))
                {
                    merged[field.Key] = field.Value;
                }
                else if (string.IsNullOrEmpty(merged[field.Key]))
                {
                    merged[field.Key] = field.Value;
                }
            }
            return merged;
        }
    }
}

