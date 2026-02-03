using System;
using PhantomVault.Core.Models.DomainStores;

namespace PhantomVault.Core.Services.DomainKeys
{
    /// <summary>
    /// Provides cryptographically isolated domain keys derived from a master key.
    ///
    /// Architecture:
    /// - Master key is derived from password via Argon2id (happens once at unlock)
    /// - Domain keys are derived via HKDF with domain-specific labels
    /// - Master key is zeroed immediately after domain key derivation
    /// - Each domain only receives its own key - never the master
    ///
    /// This creates cryptographic domain separation even within a single process.
    /// Later, process isolation can enforce this at the OS level.
    /// </summary>
    public interface IDomainKeyProvider : IDisposable
    {
        /// <summary>
        /// Whether the provider has been initialized with domain keys.
        /// </summary>
        bool IsUnlocked { get; }

        /// <summary>
        /// Gets the Obscura domain key for vault/credential storage operations.
        /// Throws if not unlocked.
        /// </summary>
        /// <returns>32-byte domain key (caller must NOT zero this - managed by provider)</returns>
        ReadOnlySpan<byte> GetObscuraKey();

        /// <summary>
        /// Gets the Attestor domain key for identity/authentication operations.
        /// Throws if not unlocked.
        /// </summary>
        /// <returns>32-byte domain key (caller must NOT zero this - managed by provider)</returns>
        ReadOnlySpan<byte> GetAttestorKey();

        /// <summary>
        /// Gets the Recovery domain key for break-glass operations.
        /// Throws if not unlocked.
        /// </summary>
        /// <returns>32-byte domain key (caller must NOT zero this - managed by provider)</returns>
        ReadOnlySpan<byte> GetRecoveryKey();

        /// <summary>
        /// Locks all domain keys, zeroing them from memory.
        /// Must be called on USB removal, session timeout, or explicit lock.
        /// </summary>
        void Lock();
    }
}
