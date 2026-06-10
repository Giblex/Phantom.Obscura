using System;
using System.Collections.Generic;
using System.Linq;
using PhantomVault.Core.Models;
using PhantomVault.Core.Models.AutoInject;

namespace PhantomVault.Core.Services.AutoInject
{

    public class CredentialMatchingEngine : ICredentialMatchingEngine
    {
        private const int MinimumConfidenceScore = 30;

        public IReadOnlyList<CredentialMatch> FindMatches(
            AutoInjectContext context,
            IEnumerable<Credential> credentials)
        {
            var matches = new List<CredentialMatch>();

            foreach (var cred in credentials)
            {
                var score = CalculateMatchScore(context, cred);
                if (score >= MinimumConfidenceScore)
                {
                    matches.Add(new CredentialMatch
                    {
                        CredentialId = cred.Title,
                        DisplayName = GetDisplayName(cred),
                        Username = cred.Username ?? string.Empty,
                        Domain = ExtractDomain(cred.Url),
                        ConfidenceScore = score,
                        LastUsed = cred.LastUsedUtc,
                        IsPasskey = !string.IsNullOrEmpty(cred.PasskeyId),
                        Tags = cred.Tags?.ToArray() ?? Array.Empty<string>()
                    });
                }
            }

            return matches
                .OrderByDescending(m => m.ConfidenceScore)
                .ThenByDescending(m => m.LastUsed ?? DateTime.MinValue)
                .ToList();
        }

        public CredentialMatch? GetBestMatch(
            AutoInjectContext context,
            IEnumerable<Credential> credentials)
        {
            var matches = FindMatches(context, credentials);
            return matches.FirstOrDefault();
        }

        private int CalculateMatchScore(AutoInjectContext context, Credential credential)
        {
            int score = 0;

            if (!string.IsNullOrEmpty(context.Domain) && !string.IsNullOrEmpty(credential.Url))
            {
                var credDomain = ExtractDomain(credential.Url);

                if (context.Domain.Equals(credDomain, StringComparison.OrdinalIgnoreCase))
                {
                    score += 50;
                }

                else if (context.Domain.Contains(credDomain, StringComparison.OrdinalIgnoreCase) ||
                         credDomain.Contains(context.Domain, StringComparison.OrdinalIgnoreCase))
                {
                    score += 35;
                }
            }

            if (!string.IsNullOrEmpty(context.WindowTitle) && !string.IsNullOrEmpty(credential.Title))
            {
                if (context.WindowTitle.Contains(credential.Title, StringComparison.OrdinalIgnoreCase))
                {
                    score += 20;
                }
            }

            if (!string.IsNullOrEmpty(context.ProcessName))
            {

                if (credential.Title?.Contains(context.ProcessName, StringComparison.OrdinalIgnoreCase) == true)
                {
                    score += 15;
                }

                if (credential.Tags?.Any(t => t.Contains(context.ProcessName, StringComparison.OrdinalIgnoreCase)) == true)
                {
                    score += 15;
                }
            }

            if (credential.LastUsedUtc.HasValue)
            {
                var daysSinceUse = (DateTime.UtcNow - credential.LastUsedUtc.Value).TotalDays;
                if (daysSinceUse < 7)
                    score += 10;
                else if (daysSinceUse < 30)
                    score += 5;
            }

            if (!string.IsNullOrEmpty(credential.PasskeyId))
            {
                score += 5;
            }

            return Math.Min(score, 100);
        }

        private string GetDisplayName(Credential credential)
        {
            var name = credential.Title ?? "Untitled";
            var username = credential.Username;

            if (!string.IsNullOrEmpty(username))
            {
                return $"{name} ({username})";
            }

            return name;
        }

        private string ExtractDomain(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            try
            {

                url = url.Replace("https://", "").Replace("http://", "");

                var slashIndex = url.IndexOf('/');
                if (slashIndex > 0)
                {
                    url = url.Substring(0, slashIndex);
                }

                return url.ToLowerInvariant();
            }
            catch
            {
                return url.ToLowerInvariant();
            }
        }
    }
}

