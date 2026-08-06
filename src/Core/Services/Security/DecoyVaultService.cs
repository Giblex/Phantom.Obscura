using System;
using System.Collections.Generic;
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

        public Task<VaultDatabase> ActivateDecoyVaultAsync()
        {
            // Deniability rule: activation must leave NO trace that distinguishes the decoy
            // from a normal session. No "decoy"/"compromise" wording is ever written to the
            // shared (plaintext, on-disk) Serilog sink, no marker file is persisted, and the
            // fake credentials are kept in memory only. If the legitimate user wants an audit
            // of duress activations, that belongs in an encrypted in-vault record — never here.
            if (_isDecoyActive)
            {
                return Task.FromResult(_decoyDatabase!);
            }

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

            // _options.DecoyDatabasePath / _options.LogActivation are intentionally NOT
            // honoured here: a persisted marker or an activation log entry would betray the
            // decoy to anyone who can read the disk. The in-memory database is sufficient.

            return Task.FromResult(_decoyDatabase);
        }

        public void DeactivateDecoyVault()
        {
            // Silent by design — see deniability rule above.
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

        /// <summary>
        /// Produces a plausible Argon2id-style hash string for the decoy. Uses random bytes
        /// (not an all-zero buffer, which is instantly recognisable as fake) and randomised
        /// cost parameters within normal ranges so it is indistinguishable from a real hash.
        /// </summary>
        public string GenerateFakeMasterPasswordHash()
        {
            var saltBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
            var hashBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var salt = Convert.ToBase64String(saltBytes).TrimEnd('=');
            var hash = Convert.ToBase64String(hashBytes).TrimEnd('=');

            int memKib = new[] { 65536, 131072, 262144 }[System.Security.Cryptography.RandomNumberGenerator.GetInt32(3)];
            int iterations = System.Security.Cryptography.RandomNumberGenerator.GetInt32(2, 5);
            return $"$argon2id$v=19$m={memKib},t={iterations},p=1${salt}${hash}";
        }
    }
}

