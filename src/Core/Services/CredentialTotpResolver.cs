using System;
using System.Linq;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services
{

    public enum TotpSource
    {

        Entry = 0,

        InlineSection = 1,

        LinkedSection = 2
    }

    /// <summary>
    /// The authenticator settings that should actually be used for a credential, wherever
    /// the seed happens to live.
    /// </summary>
    public sealed record EffectiveTotp(
        string Secret,
        int Digits,
        int Period,
        string Algorithm,
        string Issuer,
        string Account,
        TotpSource Source,
        string Label)
    {
        public TotpAlgorithm ParsedAlgorithm =>
            Enum.TryParse<TotpAlgorithm>(Algorithm, ignoreCase: true, out var parsed)
                ? parsed
                : TotpAlgorithm.SHA1;
    }

    /// <summary>
    /// Finds the authenticator seed for a credential.
    ///
    /// A seed can live in two places: on the entry itself (<see cref="Credential.TotpSecret"/>)
    /// or in a TOTP section, which may be stored inline or linked to a separate
    /// authenticator entry. Autofill, USB auto-inject and match ranking all used to read
    /// only the first of those, so a code the user could see in the vault could not be
    /// filled. Everything that needs a code should go through this.
    /// </summary>
    public static class CredentialTotpResolver
    {
        private static readonly EntrySectionService SectionService = new();

        /// <summary>
        /// Resolves the seed to use, preferring the entry's own so existing behaviour is
        /// unchanged, then falling back to the first usable TOTP section in display order.
        /// Returns null when no seed can actually produce a code — a linked section whose
        /// target is missing counts as no seed, so callers never promise a code they
        /// cannot generate.
        /// </summary>
        public static EffectiveTotp? Resolve(Credential? credential, Func<string, Credential?>? lookupEntry = null)
        {
            if (credential == null)
                return null;

            if (!string.IsNullOrWhiteSpace(credential.TotpSecret))
            {
                return new EffectiveTotp(
                    credential.TotpSecret.Trim(),
                    credential.TotpDigits > 0 ? credential.TotpDigits : 6,
                    credential.TotpTimeStep > 0 ? credential.TotpTimeStep : 30,
                    string.IsNullOrWhiteSpace(credential.TotpAlgorithm) ? "SHA1" : credential.TotpAlgorithm,
                    credential.TotpIssuer ?? string.Empty,
                    credential.TotpAccountName ?? string.Empty,
                    TotpSource.Entry,
                    string.IsNullOrWhiteSpace(credential.Title) ? "Authenticator" : credential.Title);
            }

            if (credential.Sections is not { Count: > 0 })
                return null;

            var resolver = lookupEntry ?? (_ => null);

            var candidates = credential.Sections
                .Where(s => s != null && s.Kind == EntrySectionKind.Totp)
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.CreatedUtc);

            foreach (var section in candidates)
            {
                var resolved = SectionService.Resolve(section, resolver);

                if (resolved.IsBrokenLink || string.IsNullOrWhiteSpace(resolved.Value))
                    continue;

                return new EffectiveTotp(
                    resolved.Value.Trim(),
                    resolved.TotpDigits > 0 ? resolved.TotpDigits : 6,
                    resolved.TotpPeriod > 0 ? resolved.TotpPeriod : 30,
                    string.IsNullOrWhiteSpace(resolved.TotpAlgorithm) ? "SHA1" : resolved.TotpAlgorithm,
                    resolved.TotpIssuer,
                    resolved.TotpAccount,
                    section.IsLinked ? TotpSource.LinkedSection : TotpSource.InlineSection,
                    resolved.Label);
            }

            return null;
        }

        /// <summary>
        /// Whether a code can actually be produced for this credential right now.
        /// </summary>
        public static bool HasTotp(Credential? credential, Func<string, Credential?>? lookupEntry = null)
            => Resolve(credential, lookupEntry) != null;

        /// <summary>
        /// Generates the current code, or null when there is no usable seed.
        /// </summary>
        public static string? GenerateCode(
            Credential? credential,
            Func<string, Credential?>? lookupEntry = null,
            DateTimeOffset? timestamp = null)
        {
            var totp = Resolve(credential, lookupEntry);
            if (totp == null)
                return null;

            try
            {
                return new TotpService().GenerateCode(
                    totp.Secret,
                    totp.ParsedAlgorithm,
                    timestamp ?? DateTimeOffset.UtcNow,
                    totp.Digits,
                    totp.Period);
            }
            catch
            {

                return null;
            }
        }
    }
}
