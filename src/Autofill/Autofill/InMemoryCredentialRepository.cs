using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.Autofill
{
    /// <summary>
    /// In-memory credential repository for testing and development.
    /// WARNING: This is NOT persistent and NOT encrypted. 
    /// For production, use a KeePass-based or encrypted database implementation.
    /// </summary>
    public class InMemoryCredentialRepository : ICredentialRepository
    {
        private readonly List<Credential> _credentials = new();
        private readonly object _lock = new();

        public Task<List<Credential>> GetCredentialsByDomainAsync(string domain, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(domain))
                return Task.FromResult(new List<Credential>());

            lock (_lock)
            {
                var normalizedDomain = NormalizeDomain(domain);
                var matches = _credentials
                    .Where(c => NormalizeDomain(ExtractDomain(c.Url)).Contains(normalizedDomain, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                return Task.FromResult(matches);
            }
        }

        public Task SaveCredentialAsync(Credential credential, CancellationToken cancellationToken = default)
        {
            if (credential == null)
                throw new ArgumentNullException(nameof(credential));

            lock (_lock)
            {
                // Check if credential already exists (by title)
                var existing = _credentials.FirstOrDefault(c =>
                    c.Title.Equals(credential.Title, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    // Update existing
                    existing.Username = credential.Username;
                    existing.Password = credential.Password;
                    existing.Url = credential.Url;
                    existing.Notes = credential.Notes;
                    existing.Group = credential.Group;
                    existing.Icon = credential.Icon;
                    existing.IconColor = credential.IconColor;
                    existing.CustomFields = credential.CustomFields;
                    existing.Tags = credential.Tags;
                    existing.LastUpdatedUtc = DateTimeOffset.UtcNow;
                    existing.ExpiryUtc = credential.ExpiryUtc;
                }
                else
                {
                    // Add new
                    _credentials.Add(credential);
                }
            }

            return Task.CompletedTask;
        }

        public Task UpdateCredentialAsync(Credential credential, CancellationToken cancellationToken = default)
        {
            return SaveCredentialAsync(credential, cancellationToken);
        }

        public Task DeleteCredentialAsync(string title, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Title must be provided", nameof(title));

            lock (_lock)
            {
                var credential = _credentials.FirstOrDefault(c =>
                    c.Title.Equals(title, StringComparison.OrdinalIgnoreCase));

                if (credential != null)
                {
                    _credentials.Remove(credential);
                }
            }

            return Task.CompletedTask;
        }

        public Task<List<Credential>> GetAllCredentialsAsync(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                return Task.FromResult(new List<Credential>(_credentials));
            }
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
                return uri.Host;
            }
            catch
            {
                return url;
            }
        }

        private string NormalizeDomain(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
                return string.Empty;

            // Remove www. prefix
            domain = domain.Replace("www.", "", StringComparison.OrdinalIgnoreCase);

            // Convert to lowercase for case-insensitive comparison
            return domain.ToLowerInvariant();
        }
    }
}
