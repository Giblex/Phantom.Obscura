namespace PhantomVault.Core.Models
{
    /// <summary>
    /// Argon2id parameters used to derive a manifest-encryption key.
    /// <para>
    /// Stored in the OUTER manifest payload (alongside salt/nonce/tag/ciphertext) so they
    /// can be read without first decrypting the manifest. Existing manifests written before
    /// this field existed fall back to <see cref="Standard"/> on read.
    /// </para>
    /// <para>
    /// The vault-database key derivation (<c>VaultDatabaseKeyService</c>) uses a SEPARATE,
    /// fixed parameter set so toggling Fast Unlock never invalidates the encrypted vault
    /// payload — only the manifest key needs to be re-derived.
    /// </para>
    /// <para>
    /// Named <c>ManifestKdfParams</c> to disambiguate from
    /// <c>GiblexVault.Security.ZK.Models.KdfParams</c> used by recovery-code derivation.
    /// </para>
    /// </summary>
    public sealed record ManifestKdfParams(int MemoryKb, int Iterations, int Parallelism)
    {
        /// <summary>
        /// Default high-security parameters: 256 MiB / 6 iterations / auto-parallelism.
        /// Matches the historical hardcoded defaults in EncryptionService.DeriveKey so
        /// manifests written before this field existed remain readable.
        /// </summary>
        public static ManifestKdfParams Standard { get; } = new(MemoryKb: 256 * 1024, Iterations: 6, Parallelism: 0);

        /// <summary>
        /// Reduced parameters for Fast Unlock mode: 64 MiB / 3 iterations / auto-parallelism.
        /// Roughly 4× faster wall-clock; still expensive enough to resist commodity GPU
        /// brute force when combined with the mandatory USB keyfile.
        /// </summary>
        public static ManifestKdfParams Fast { get; } = new(MemoryKb: 64 * 1024, Iterations: 3, Parallelism: 0);
    }
}
