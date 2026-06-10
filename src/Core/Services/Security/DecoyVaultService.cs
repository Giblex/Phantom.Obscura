using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.Security
{

    public sealed class DecoyVaultOptions
    {

        public int? CredentialCount { get; set; }

        public int? RandomSeed { get; set; }

        public string? DecoyDatabasePath { get; set; }

        public bool LogActivation { get; set; } = true;

        public bool SimulateReadOnly { get; set; } = true;
    }

    public sealed class DecoyVaultService
    {
        private readonly ILogger<DecoyVaultService>? _logger;
        private readonly DecoyCredentialGenerator _generator;
        private readonly DecoyVaultOptions _options;

        private VaultDatabase? _decoyDatabase;
        private bool _isDecoyActive;

        public bool IsDecoyActive => _isDecoyActive;

        public VaultDatabase? DecoyDatabase => _decoyDatabase;

        public DecoyVaultService(DecoyVaultOptions? options = null, ILogger<DecoyVaultService>? logger = null)
        {
            _options = options ?? new DecoyVaultOptions();
            _logger = logger;
            _generator = new DecoyCredentialGenerator(_options.RandomSeed);
        }

        public async Task<VaultDatabase> ActivateDecoyVaultAsync()
        {
            if (_isDecoyActive)
            {
                _logger?.LogWarning("Decoy vault already active, returning existing instance");
                return _decoyDatabase!;
            }

            _logger?.LogCritical("ACTIVATING DECOY VAULT - Suspected security compromise");

            var decoyCredentials = _generator.GenerateDecoyCredentials(_options.CredentialCount);

            var groups = OrganizeCredentialsIntoGroups(decoyCredentials);

            _decoyDatabase = new VaultDatabase
            {
                VaultName = "Personal Vault",
                Description = "Secure Password Manager",
                Created = DateTime.UtcNow.AddDays(-System.Security.Cryptography.RandomNumberGenerator.GetInt32(180, 730)),
                Groups = groups
            };

            _isDecoyActive = true;

            if (!string.IsNullOrEmpty(_options.DecoyDatabasePath))
            {
                await PersistDecoyDatabaseAsync(_decoyDatabase, _options.DecoyDatabasePath);
            }

            if (_options.LogActivation)
            {
                int totalCredentials = groups.Sum(g => g.Entries?.Count ?? 0);
                _logger?.LogCritical(
                    "Decoy vault activated with {CredentialCount} fake credentials. " +
                    "This event indicates potential security compromise. " +
                    "Real vault has been protected.",
                    totalCredentials);
            }

            return _decoyDatabase;
        }

        public void DeactivateDecoyVault()
        {
            if (!_isDecoyActive)
            {
                _logger?.LogWarning("Attempted to deactivate decoy vault but none is active");
                return;
            }

            _logger?.LogWarning("Deactivating decoy vault");

            _decoyDatabase = null;
            _isDecoyActive = false;
        }

        private List<VaultGroup> OrganizeCredentialsIntoGroups(List<Credential> credentials)
        {
            var groups = new List<VaultGroup>();

            var grouped = credentials.GroupBy(c => string.IsNullOrEmpty(c.Group) ? "Uncategorized" : c.Group);

            foreach (var group in grouped)
            {
                groups.Add(new VaultGroup
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = group.Key,
                    Icon = GetIconForGroup(group.Key),
                    Entries = group.ToList()
                });
            }

            return groups;
        }

        private string? GetIconForGroup(string groupName)
        {
            return groupName.ToLower() switch
            {
                "personal" => "Person",
                "work" => "Briefcase",
                "social" => "People",
                "shopping" => "Cart",
                "entertainment" => "Play",
                "financial" => "Money",
                "email" => "Mail",
                "development" => "Code",
                "network" => "Wifi",
                _ => "Folder"
            };
        }

        private async Task PersistDecoyDatabaseAsync(VaultDatabase database, string path)
        {
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(path, $"Decoy vault created: {DateTime.UtcNow:O}");
                _logger?.LogInformation("Decoy database marker persisted to {Path}", path);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to persist decoy database marker to {Path}", path);
            }
        }

        public string GenerateFakeMasterPasswordHash()
        {
            var fakeHash = Convert.ToBase64String(new byte[64]);
            return $"$argon2id$v=19$m=65536,t=3,p=1${fakeHash}";
        }
    }
}

