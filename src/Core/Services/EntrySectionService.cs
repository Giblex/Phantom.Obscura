using System;
using System.Collections.Generic;
using System.Linq;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services
{

    public sealed record ResolvedSection
    {
        public required EntrySection Section { get; init; }

        public Credential? LinkedEntry { get; init; }

        public string Label { get; init; } = string.Empty;

        public string Value { get; init; } = string.Empty;

        public bool IsBrokenLink { get; init; }

        public int TotpDigits { get; init; } = 6;

        public int TotpPeriod { get; init; } = 30;

        public string TotpAlgorithm { get; init; } = "SHA1";

        public string TotpIssuer { get; init; } = string.Empty;

        public string TotpAccount { get; init; } = string.Empty;

        public string? QrPayload { get; init; }

        public IReadOnlyList<string> RecoveryCodes { get; init; } = Array.Empty<string>();
    }

    public sealed class EntrySectionService
    {

        public ResolvedSection Resolve(EntrySection section, Func<string, Credential?> lookupEntry)
        {
            ArgumentNullException.ThrowIfNull(section);
            ArgumentNullException.ThrowIfNull(lookupEntry);

            Credential? linked = null;
            var brokenLink = false;

            if (section.IsLinked)
            {
                linked = lookupEntry(section.LinkedEntryId!);
                brokenLink = linked == null;
            }

            var label = ResolveLabel(section, linked);
            var value = ResolveValue(section, linked);

            var totpDigits = section.GetMetaInt(EntrySection.MetaTotpDigits, linked?.TotpDigits ?? 6);
            var totpPeriod = section.GetMetaInt(EntrySection.MetaTotpPeriod, linked?.TotpTimeStep ?? 30);
            var totpAlgorithm = section.GetMeta(EntrySection.MetaTotpAlgorithm)
                ?? (string.IsNullOrWhiteSpace(linked?.TotpAlgorithm) ? "SHA1" : linked!.TotpAlgorithm);
            var totpIssuer = section.GetMeta(EntrySection.MetaTotpIssuer) ?? linked?.TotpIssuer ?? string.Empty;
            var totpAccount = section.GetMeta(EntrySection.MetaTotpAccount) ?? linked?.TotpAccountName ?? string.Empty;

            var resolved = new ResolvedSection
            {
                Section = section,
                LinkedEntry = linked,
                Label = label,
                Value = value,
                IsBrokenLink = brokenLink,
                TotpDigits = totpDigits <= 0 ? 6 : totpDigits,
                TotpPeriod = totpPeriod <= 0 ? 30 : totpPeriod,
                TotpAlgorithm = totpAlgorithm,
                TotpIssuer = totpIssuer,
                TotpAccount = totpAccount,
                RecoveryCodes = section.Kind == EntrySectionKind.RecoveryCodes
                    ? SplitRecoveryCodes(value)
                    : Array.Empty<string>()
            };

            return resolved with { QrPayload = BuildQrPayload(resolved) };
        }

        public IReadOnlyList<ResolvedSection> ResolveAll(Credential credential, Func<string, Credential?> lookupEntry)
        {
            ArgumentNullException.ThrowIfNull(credential);

            if (credential.Sections == null || credential.Sections.Count == 0)
                return Array.Empty<ResolvedSection>();

            return credential.Sections
                .Where(s => s != null)
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.CreatedUtc)
                .Select(s => Resolve(s, lookupEntry))
                .ToList();
        }

        public static string ResolveLabel(EntrySection section, Credential? linked)
        {
            if (!string.IsNullOrWhiteSpace(section.Label))
                return section.Label.Trim();

            if (linked != null && !string.IsNullOrWhiteSpace(linked.Title))
                return linked.Title;

            return EntrySection.DefaultLabel(section.Kind);
        }

        public static string ResolveValue(EntrySection section, Credential? linked)
        {
            if (!section.IsLinked)
                return section.Value;

            if (linked == null)
                return string.Empty;

            return section.Kind switch
            {
                EntrySectionKind.Note => FirstNonEmpty(linked.Notes, section.Value),
                EntrySectionKind.PinCode => FirstNonEmpty(linked.PinValue, linked.CardPIN, section.Value),
                EntrySectionKind.Totp => FirstNonEmpty(linked.TotpSecret, section.Value),
                EntrySectionKind.RecoveryEmail => FirstNonEmpty(linked.ContactEmail, linked.Username, section.Value),
                EntrySectionKind.RecoveryCodes => FirstNonEmpty(linked.Notes, section.Value),
                EntrySectionKind.Url => FirstNonEmpty(linked.Url, section.Value),
                EntrySectionKind.Phone => FirstNonEmpty(linked.ContactPhone, section.Value),
                EntrySectionKind.Address => FirstNonEmpty(linked.ContactAddress, linked.CardBillingAddress, section.Value),
                EntrySectionKind.Secret => FirstNonEmpty(linked.Password, linked.ApiKeyValue, section.Value),
                _ => FirstNonEmpty(section.Value, linked.Notes)
            };
        }

        public static string? BuildQrPayload(ResolvedSection resolved)
        {
            var section = resolved.Section;

            if (section.Kind == EntrySectionKind.Totp)
            {
                return string.IsNullOrWhiteSpace(resolved.Value)
                    ? null
                    : BuildOtpAuthUri(resolved.Value, resolved.TotpIssuer, resolved.TotpAccount,
                        resolved.TotpDigits, resolved.TotpPeriod, resolved.TotpAlgorithm);
            }

            if (section.Kind == EntrySectionKind.QrCode)
                return string.IsNullOrWhiteSpace(resolved.Value) ? null : resolved.Value;

            return null;
        }

        public static string BuildOtpAuthUri(string secret, string issuer, string account, int digits, int period, string algorithm)
        {
            var label = string.IsNullOrWhiteSpace(issuer)
                ? Uri.EscapeDataString(string.IsNullOrWhiteSpace(account) ? "Phantom Obscura" : account)
                : $"{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(string.IsNullOrWhiteSpace(account) ? "account" : account)}";

            var uri = $"otpauth://totp/{label}?secret={Uri.EscapeDataString(secret.Trim())}";

            if (!string.IsNullOrWhiteSpace(issuer))
                uri += $"&issuer={Uri.EscapeDataString(issuer)}";

            uri += $"&algorithm={Uri.EscapeDataString(string.IsNullOrWhiteSpace(algorithm) ? "SHA1" : algorithm.ToUpperInvariant())}";
            uri += $"&digits={digits}";
            uri += $"&period={period}";

            return uri;
        }

        public static IReadOnlyList<string> SplitRecoveryCodes(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Array.Empty<string>();

            return value
                .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .Where(c => c.Length > 0)
                .ToList();
        }

        public static EntrySection CreateLinkTo(Credential target, EntrySectionKind? kindOverride = null, string? label = null)
        {
            ArgumentNullException.ThrowIfNull(target);

            var kind = kindOverride ?? EntrySection.KindForEntryType(target.EntryType);
            var section = EntrySection.CreateLink(kind, target.Id, label ?? target.Title);

            if (kind == EntrySectionKind.Totp)
            {
                section.SetMeta(EntrySection.MetaTotpDigits, target.TotpDigits.ToString());
                section.SetMeta(EntrySection.MetaTotpPeriod, target.TotpTimeStep.ToString());
                section.SetMeta(EntrySection.MetaTotpAlgorithm, target.TotpAlgorithm);
                section.SetMeta(EntrySection.MetaTotpIssuer, target.TotpIssuer);
                section.SetMeta(EntrySection.MetaTotpAccount, target.TotpAccountName);
            }
            else if (kind == EntrySectionKind.PinCode && !string.IsNullOrEmpty(target.PinValue))
            {
                section.SetMeta(EntrySection.MetaPinLength, target.PinValue.Length.ToString());
            }

            return section;
        }

        public static IReadOnlyList<string> Validate(EntrySection section)
        {
            var issues = new List<string>();

            if (section == null)
            {
                issues.Add("Section is missing.");
                return issues;
            }

            if (section.IsLinked && string.IsNullOrWhiteSpace(section.LinkedEntryId))
                issues.Add("Linked section has no target entry.");

            if (!section.IsLinked && string.IsNullOrWhiteSpace(section.Value) && section.Kind != EntrySectionKind.Note)
                issues.Add($"'{ResolveLabel(section, null)}' has no value.");

            if (section.Kind == EntrySectionKind.RecoveryEmail && !section.IsLinked &&
                !string.IsNullOrWhiteSpace(section.Value) && !LooksLikeEmail(section.Value))
                issues.Add($"'{ResolveLabel(section, null)}' does not look like an email address.");

            if (section.Kind == EntrySectionKind.PinCode && !section.IsLinked)
            {
                var length = section.GetMetaInt(EntrySection.MetaPinLength, section.Value.Length);
                if (length is < PinLengthRange.Min or > PinLengthRange.Max)
                    issues.Add($"PIN length must be between {PinLengthRange.Min} and {PinLengthRange.Max}.");
            }

            return issues;
        }

        private static bool LooksLikeEmail(string value)
        {
            var trimmed = value.Trim();
            var at = trimmed.IndexOf('@');
            return at > 0 && at < trimmed.Length - 1 && trimmed.IndexOf('.', at) > at + 1;
        }

        private static string FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
    }

    public static class PinLengthRange
    {
        /// <summary>
        /// Length range for a PIN *stored as credential data* — a card PIN, a door code,
        /// an alarm code — entered on the add/edit entry form or in a PIN section.
        ///
        /// This is recorded data, not an authentication factor: the vault does not verify
        /// anything against it, so its length carries no security weight here and the
        /// range simply has to represent whatever the real-world PIN is. Four-digit card
        /// PINs and single-digit codes both exist, so the floor is 1.
        ///
        /// This was briefly raised to 6 after the vault's own unlock PIN was made to defer
        /// to this range. That conflated two unrelated things; the vault unlock PIN now has
        /// its own floor in <c>PinLockService.MinVaultPinLength</c>.
        /// </summary>
        public const int Min = 1;
        public const int Max = 8;

        public static int Clamp(int value) => value < Min ? Min : value > Max ? Max : value;

        public static IReadOnlyList<int> All { get; } = Enumerable.Range(Min, Max - Min + 1).ToList();
    }
}
