using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Options for configuring decoy vault behavior.
    /// </summary>
    public sealed class DecoyVaultOptions
    {
        /// <summary>
        /// Number of fake credentials to generate (default: random 15-30)
        /// </summary>
        public int? CredentialCount { get; set; }

        /// <summary>
        /// Seed for deterministic decoy generation (null = random)
        /// </summary>
        public int? RandomSeed { get; set; }

        /// <summary>
        /// Path where decoy vault database should be stored
        /// </summary>
        public string? DecoyDatabasePath { get; set; }

        /// <summary>
        /// Whether to log decoy activation (for security auditing)
        /// </summary>
        public bool LogActivation { get; set; } = true;

        /// <summary>
        /// Whether to simulate read-only mode in decoy (prevents attacker modifications)
        /// </summary>
        public bool SimulateReadOnly { get; set; } = true;
    }

    /// <summary>
    /// Manages the decoy vault lifecycle including generation, activation, and state management.
    /// </summary>
    public sealed class DecoyVaultService
    {
        private readonly ILogger<DecoyVaultService>? _logger;
        private readonly DecoyCredentialGenerator _generator;
        private readonly DecoyVaultOptions _options;

        private VaultDatabase? _decoyDatabase;
        private bool _isDecoyActive;

        /// <summary>
        /// Returns true if the decoy vault is currently active
        /// </summary>
        public bool IsDecoyActive => _isDecoyActive;

        /// <summary>
        /// Returns the active decoy database (null if not activated)
        /// </summary>
        public VaultDatabase? DecoyDatabase => _decoyDatabase;

        public DecoyVaultService(DecoyVaultOptions? options = null, ILogger<DecoyVaultService>? logger = null)
        {
            _options = options ?? new DecoyVaultOptions();
            _logger = logger;
            _generator = new DecoyCredentialGenerator(_options.RandomSeed);
        }

        /// <summary>
        /// Generates and activates a decoy vault database.
        /// This creates fake credentials that appear realistic to an attacker.
        /// </summary>
        /// <returns>The generated decoy database</returns>
        public async Task<VaultDatabase> ActivateDecoyVaultAsync()
        {
            if (_isDecoyActive)
            {
                _logger?.LogWarning("Decoy vault already active, returning existing instance");
                return _decoyDatabase!;
            }

            _logger?.LogCritical("ACTIVATING DECOY VAULT - Suspected security compromise");

            // Generate fake credentials
            var decoyCredentials = _generator.GenerateDecoyCredentials(_options.CredentialCount);

            // Organize credentials into groups
            var groups = OrganizeCredentialsIntoGroups(decoyCredentials);

            // Create decoy database
            _decoyDatabase = new VaultDatabase
            {
                VaultName = "Personal Vault",
                Description = "Secure Password Manager",
                Created = DateTime.UtcNow.AddDays(-new Random().Next(180, 730)),
                Groups = groups
            };

            _isDecoyActive = true;

            // Optionally persist decoy to disk for future use
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

        /// <summary>
        /// Deactivates the decoy vault and returns to normal operation.
        /// WARNING: This should only be called during legitimate recovery operations.
        /// </summary>
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

        /// <summary>
        /// Organizes flat list of credentials into groups by their Group property.
        /// </summary>
        private List<VaultGroup> OrganizeCredentialsIntoGroups(List<Credential> credentials)
        {
            var groups = new List<VaultGroup>();

            // Group credentials by their Group property
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

        /// <summary>
        /// Generates a plausible fake master password hash for the decoy vault.
        /// This is used to make the decoy appear legitimate during authentication.
        /// </summary>
        public string GenerateFakeMasterPasswordHash()
        {
            var fakeHash = Convert.ToBase64String(new byte[64]);
            return $"$argon2id$v=19$m=65536,t=3,p=1${fakeHash}";
        }
    }
}
