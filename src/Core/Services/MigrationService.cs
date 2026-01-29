using System;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Provides facilities for migrating a vault manifest between
    /// encryption algorithms. As cryptography evolves over time and new
    /// standards emerge (such as XChaCha20 or post‑quantum hybrids),
    /// existing vaults may need to be re‑encrypted with a different
    /// algorithm. The migration service reads the current manifest using
    /// the provided passphrase and keyfile, updates its algorithm
    /// identifier and re‑writes it using <see cref="ManifestService"/>.
    /// 
    /// Note: In this simplified implementation, only the manifest
    /// encryption algorithm is updated. Migrating the underlying
    /// container (e.g. a VeraCrypt volume) would require creating a new
    /// container and transferring its contents, which is outside the
    /// scope of this service.
    /// </summary>
    public sealed class MigrationService
    {
        private readonly ManifestService _manifestService;

        public MigrationService(ManifestService manifestService)
        {
            _manifestService = manifestService;
        }

        /// <summary>
        /// Migrates the manifest at the specified path to a new
        /// encryption algorithm. If the manifest already uses the
        /// requested algorithm, no action is taken. Otherwise the
        /// algorithm field is updated and the manifest is re‑written
        /// encrypted under the existing passphrase/keyfile.
        /// </summary>
        /// <param name="manifestPath">Absolute path to the encrypted manifest.</param>
        /// <param name="passphrase">Passphrase used to decrypt the manifest.</param>
        /// <param name="keyfilePath">Optional keyfile path if used during provisioning.</param>
        /// <param name="newAlgorithm">The name of the new encryption algorithm (e.g. "XChaCha20-Poly1305").</param>
        public void MigrateManifest(string manifestPath, string passphrase, string? keyfilePath, string newAlgorithm)
        {
            if (string.IsNullOrWhiteSpace(newAlgorithm)) throw new ArgumentException("New algorithm must be specified", nameof(newAlgorithm));
            // Read the existing manifest using the current algorithm. The
            // ManifestService handles decryption based on the stored
            // algorithm.
            var manifest = _manifestService.ReadManifest(manifestPath, passphrase, keyfilePath);
            if (string.Equals(manifest.Algorithm, newAlgorithm, StringComparison.OrdinalIgnoreCase))
            {
                // Already on the desired algorithm; nothing to do.
                return;
            }
            // Update algorithm identifier. In a full implementation, you
            // would re‑encrypt the manifest with the new algorithm here.
            manifest.Algorithm = newAlgorithm;
            // Write the updated manifest back to disk using the existing
            // passphrase and keyfile. The ManifestService always uses
            // the algorithm recorded in the manifest when encrypting.
            _manifestService.WriteManifest(manifest, manifestPath, passphrase, keyfilePath);
        }
    }
}