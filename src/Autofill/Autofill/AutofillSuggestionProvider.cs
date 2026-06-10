using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.Autofill
{

    public sealed class AutofillSuggestionProvider
    {
        private readonly ICredentialRepository _repository;

        public AutofillSuggestionProvider(ICredentialRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<List<CredentialSuggestion>> GetSuggestionsForDomainAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return new List<CredentialSuggestion>();

            var domain = ExtractDomain(url);
            if (string.IsNullOrEmpty(domain))
                return new List<CredentialSuggestion>();

            var allCredentials = await _repository.GetAllCredentialsAsync();
            var suggestions = new List<CredentialSuggestion>();

            foreach (var credential in allCredentials)
            {
                if (credential.EntryType != EntryType.Password)
                    continue;

                var credentialDomain = ExtractDomain(credential.Url);
                if (string.IsNullOrEmpty(credentialDomain))
                    continue;

                var matchScore = CalculateMatchScore(domain, credentialDomain);
                if (matchScore > 0)
                {
                    suggestions.Add(new CredentialSuggestion
                    {
                        Credential = credential,
                        MatchScore = matchScore,
                        MatchType = GetMatchType(domain, credentialDomain)
                    });
                }
            }

            return suggestions
                .OrderByDescending(s => s.MatchScore)
                .ThenBy(s => s.Credential.Title)
                .ToList();
        }

        public async Task<List<CredentialSuggestion>> GetSuggestionsForUsernameAsync(string url, string partialUsername)
        {
            var domainSuggestions = await GetSuggestionsForDomainAsync(url);

            if (string.IsNullOrWhiteSpace(partialUsername))
                return domainSuggestions;

            var filtered = domainSuggestions
                .Where(s => s.Credential.Username?.StartsWith(partialUsername, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            return filtered.Any() ? filtered : domainSuggestions ?? new List<CredentialSuggestion>();
        }

        private string ExtractDomain(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            try
            {

                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    url = "https://" + url;
                }

                var uri = new Uri(url);
                return uri.Host.ToLowerInvariant();
            }
            catch
            {
                return string.Empty;
            }
        }

        private int CalculateMatchScore(string targetDomain, string credentialDomain)
        {
            if (targetDomain == credentialDomain)
                return 100;

            var targetClean = targetDomain.Replace("www.", "");
            var credentialClean = credentialDomain.Replace("www.", "");

            if (targetClean == credentialClean)
                return 95;

            if (targetDomain.EndsWith("." + credentialDomain) || credentialDomain.EndsWith("." + targetDomain))
                return 80;

            var targetParts = targetClean.Split('.');
            var credentialParts = credentialClean.Split('.');

            if (targetParts.Length >= 2 && credentialParts.Length >= 2)
            {
                var targetBase = string.Join(".", targetParts.Skip(Math.Max(0, targetParts.Length - 2)));
                var credentialBase = string.Join(".", credentialParts.Skip(Math.Max(0, credentialParts.Length - 2)));

                if (targetBase == credentialBase)
                    return 60;
            }

            return 0;
        }

        private MatchType GetMatchType(string targetDomain, string credentialDomain)
        {
            if (targetDomain == credentialDomain)
                return MatchType.Exact;

            var targetClean = targetDomain.Replace("www.", "");
            var credentialClean = credentialDomain.Replace("www.", "");

            if (targetClean == credentialClean)
                return MatchType.Exact;

            if (targetDomain.EndsWith("." + credentialDomain) || credentialDomain.EndsWith("." + targetDomain))
                return MatchType.Subdomain;

            return MatchType.BaseDomain;
        }
    }

    public sealed class CredentialSuggestion
    {
        public Credential Credential { get; set; } = null!;
        public int MatchScore { get; set; }
        public MatchType MatchType { get; set; }
    }

    public enum MatchType
    {
        Exact,
        Subdomain,
        BaseDomain
    }
}

