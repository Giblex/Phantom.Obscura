using System;
using PhantomVault.Core.Models;
using PhantomVault.Core.Utils;

namespace PhantomVault.Core.Services
{

    public sealed class MigrationService
    {
        private readonly ManifestService _manifestService;

        public MigrationService(ManifestService manifestService)
        {
            _manifestService = manifestService;
        }

        public void MigrateManifest(string manifestPath, string passphrase, string? keyfilePath, string newAlgorithm)
        {
            if (string.IsNullOrWhiteSpace(newAlgorithm)) throw new ArgumentException("New algorithm must be specified", nameof(newAlgorithm));

            using var sp = SecurePassword.FromString(passphrase);
            var manifest = _manifestService.ReadManifestSecure(manifestPath, sp, keyfilePath);
            if (string.Equals(manifest.Algorithm, newAlgorithm, StringComparison.OrdinalIgnoreCase))
            {

                return;
            }

            manifest.Algorithm = newAlgorithm;

            _manifestService.WriteManifestSecure(manifest, manifestPath, sp, keyfilePath);
        }
    }
}

