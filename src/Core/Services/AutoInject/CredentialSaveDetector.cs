using System;
using System.Collections.Generic;
using System.Linq;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.AutoInject
{
    /// <summary>What, if anything, to offer the user after a login form is submitted.</summary>
    public enum SavePromptKind
    {
        /// <summary>Nothing worth offering — already stored, or not a login.</summary>
        None = 0,

        /// <summary>No credential matches this site and username.</summary>
        SaveNew = 1,

        /// <summary>A credential matches, but the submitted password is different.</summary>
        UpdateExisting = 2
    }

    public sealed class SavePromptDecision
    {
        public SavePromptKind Kind { get; init; }
        public string Domain { get; init; } = string.Empty;
        public string Username { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;

        /// <summary>Title of the credential to update, when <see cref="Kind"/> is UpdateExisting.</summary>
        public string? ExistingCredentialId { get; init; }

        public static readonly SavePromptDecision None = new() { Kind = SavePromptKind.None };
    }

    /// <summary>
    /// Decides whether a submitted login form is worth offering to save or update.
    ///
    /// The browser extension has always emitted a submitForm message and the native
    /// host has always re-raised it as FormSubmitted — but nothing subscribed, so the
    /// event fired into the void and passwords were never offered for saving. This is
    /// the missing consumer: it turns a raw field list into a decision.
    /// </summary>
    public sealed class CredentialSaveDetector
    {
        private static readonly string[] UsernameHints =
        {
            "user", "username", "email", "e-mail", "login", "account", "identifier", "phone"
        };

        /// <summary>
        /// <paramref name="fields"/> is (name/id/type/autocomplete, value) as reported
        /// by the page. <paramref name="existing"/> is the vault's current contents.
        /// </summary>
        public SavePromptDecision Evaluate(
            string url,
            IReadOnlyList<(string Descriptor, string Type, string Value)> fields,
            IEnumerable<Credential> existing)
        {
            if (fields == null || fields.Count == 0) return SavePromptDecision.None;

            string password = fields
                .FirstOrDefault(f => string.Equals(f.Type, "password", StringComparison.OrdinalIgnoreCase)
                                     && !string.IsNullOrEmpty(f.Value)).Value ?? string.Empty;

            // No password submitted means this was a search box or a first-stage form,
            // not something worth offering to save.
            if (string.IsNullOrEmpty(password)) return SavePromptDecision.None;

            string username = FindUsername(fields);
            string domain = ExtractDomain(url);
            if (string.IsNullOrEmpty(domain)) return SavePromptDecision.None;

            var candidates = existing?
                .Where(c => DomainMatches(ExtractDomain(c.Url), domain))
                .ToList() ?? new List<Credential>();

            // Prefer an exact username match; fall back to a lone entry for the site so
            // a password change is still detected when the form omits the username.
            var match = candidates.FirstOrDefault(c =>
                            !string.IsNullOrEmpty(username) &&
                            string.Equals(c.Username, username, StringComparison.OrdinalIgnoreCase))
                        ?? (candidates.Count == 1 && string.IsNullOrEmpty(username) ? candidates[0] : null);

            if (match == null)
            {
                return new SavePromptDecision
                {
                    Kind = SavePromptKind.SaveNew,
                    Domain = domain,
                    Username = username,
                    Password = password
                };
            }

            if (string.Equals(match.Password, password, StringComparison.Ordinal))
                return SavePromptDecision.None; // already stored, nothing to do

            return new SavePromptDecision
            {
                Kind = SavePromptKind.UpdateExisting,
                Domain = domain,
                Username = string.IsNullOrEmpty(username) ? match.Username ?? string.Empty : username,
                Password = password,
                ExistingCredentialId = match.Title
            };
        }

        private static string FindUsername(IReadOnlyList<(string Descriptor, string Type, string Value)> fields)
        {
            // An explicit email/text field whose descriptor looks like an identifier.
            foreach (var f in fields)
            {
                if (string.IsNullOrEmpty(f.Value)) continue;
                if (string.Equals(f.Type, "password", StringComparison.OrdinalIgnoreCase)) continue;

                var d = f.Descriptor?.ToLowerInvariant() ?? string.Empty;
                if (string.Equals(f.Type, "email", StringComparison.OrdinalIgnoreCase) ||
                    UsernameHints.Any(h => d.Contains(h, StringComparison.Ordinal)))
                {
                    return f.Value;
                }
            }

            // Otherwise the first non-empty, non-password value is the best guess.
            foreach (var f in fields)
            {
                if (string.IsNullOrEmpty(f.Value)) continue;
                if (string.Equals(f.Type, "password", StringComparison.OrdinalIgnoreCase)) continue;
                return f.Value;
            }

            return string.Empty;
        }

        /// <summary>Registrable-ish domain compare, so www and bare host are the same site.</summary>
        private static bool DomainMatches(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
                   || a.EndsWith("." + b, StringComparison.OrdinalIgnoreCase)
                   || b.EndsWith("." + a, StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractDomain(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            try
            {
                if (!url.Contains("://", StringComparison.Ordinal)) url = "https://" + url;
                var host = new Uri(url).Host;
                return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
            }
            catch (UriFormatException)
            {
                return string.Empty;
            }
        }
    }
}
