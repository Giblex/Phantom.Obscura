using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using PhantomVault.Core.Models.DomainStores;

namespace PhantomVault.Core.Services.DomainKeys
{
    /// <summary>
    /// Produces and consumes the canonical recovery store. This is the single place that knows how
    /// to seed a recovery store at provisioning time and how to recover a domain key from one while
    /// the vault is LOCKED — the heart of direction A (scoped RecoveryStore as the canonical model).
    ///
    /// Design (breaks the historical Catch-22 and the per-code seal bug):
    ///   * A random RecoveryStoreKey (RSK) protects the store body and is wrapped once per recovery
    ///     code by <see cref="RecoveryStoreEnvelope"/> — so ANY code opens the store, no unlock.
    ///   * Each domain key is sealed under a KEK derived from that SAME RSK
    ///     (<see cref="RecoveryStoreEnvelope.DeriveDomainSealKey"/>), so every code (not just the
    ///     seed-time one) can unseal the domain keys.
    /// </summary>
    public sealed class RecoveryStoreFactory
    {
        private readonly ScopedRecoveryService _scopedRecoveryService;

        public RecoveryStoreFactory(ScopedRecoveryService scopedRecoveryService)
        {
            _scopedRecoveryService = scopedRecoveryService ?? throw new ArgumentNullException(nameof(scopedRecoveryService));
        }

        public sealed class SeedInput
        {
            /// <summary>The 32-byte secret to seal per domain (recovered verbatim later).</summary>
            public IReadOnlyDictionary<CryptoDomain, byte[]> DomainRecoveryKeys { get; init; }
                = new Dictionary<CryptoDomain, byte[]>();

            /// <summary>
            /// The mandatory keyfile material to back up (the only required credential on a
            /// USB-bound, password-less vault). Stored in the RSK-encrypted body; any recovery code
            /// restores it. Either this or <see cref="DomainRecoveryKeys"/> (or both) must be set.
            /// </summary>
            public byte[]? ProtectedKeyfile { get; init; }

            /// <summary>Plaintext recovery codes shown to the user; each can open the store.</summary>
            public IReadOnlyList<string> RecoveryCodes { get; init; } = Array.Empty<string>();

            /// <summary>Optional vault recovery key wrap, for in-app management while unlocked.</summary>
            public byte[]? VaultRecoveryKey { get; init; }

            public string? DeviceId { get; init; }
            public string? SetupAppVersion { get; init; }
            public RecoveryPolicy? Policy { get; init; }
        }

        /// <summary>
        /// Build the in-memory store, seal every domain key under the RSK-derived KEK, and return the
        /// encrypted envelope bytes ready to persist. The vault must be unlocked at provisioning, but
        /// the produced envelope can be opened later with only a recovery code.
        /// </summary>
        public byte[] Seed(SeedInput input, out RecoveryStore store)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            bool hasKeyfile = input.ProtectedKeyfile is { Length: > 0 };
            if (input.DomainRecoveryKeys.Count == 0 && !hasKeyfile)
                throw new ArgumentException("Provide a keyfile backup and/or at least one domain key to seed the recovery store.", nameof(input));
            if (input.RecoveryCodes.Count == 0)
                throw new ArgumentException("At least one recovery code is required to seed the recovery store.", nameof(input));

            store = new RecoveryStore
            {
                Policy = input.Policy ?? new RecoveryPolicy(),
                Salt = RandomNumberGenerator.GetBytes(32),
                DeviceId = input.DeviceId,
                SetupAppVersion = input.SetupAppVersion,
                MaxRecoveryCodes = input.RecoveryCodes.Count,
                ProtectedKeyfile = hasKeyfile ? (byte[])input.ProtectedKeyfile!.Clone() : null
            };

            for (int i = 0; i < input.RecoveryCodes.Count; i++)
            {
                store.RecoveryCodes.Add(new RecoveryCodeEntry
                {
                    CodeNumber = i,
                    Used = false,
                    CreatedUtc = DateTimeOffset.UtcNow
                });
            }

            var rsk = RandomNumberGenerator.GetBytes(32);
            var domainSealKey = RecoveryStoreEnvelope.DeriveDomainSealKey(rsk, store.Salt);
            try
            {
                foreach (var (domain, domainKey) in input.DomainRecoveryKeys)
                {
                    if (domainKey == null || domainKey.Length != 32)
                        throw new ArgumentException($"Domain key for {domain} must be 32 bytes.", nameof(input));

                    var sealedKey = _scopedRecoveryService.SealDomainRecoveryKey(domainKey, domainSealKey, domain);
                    switch (domain)
                    {
                        case CryptoDomain.Obscura:
                            store.ObscuraSealed = sealedKey;
                            break;
                        case CryptoDomain.Attestor:
                            store.AttestorSealed = sealedKey;
                            break;
                        default:
                            throw new ArgumentException($"Unsupported recovery domain {domain}.", nameof(input));
                    }
                }

                var vaultKey = input.VaultRecoveryKey ?? Array.Empty<byte>();
                return RecoveryStoreEnvelope.SealWithStoreKey(store, input.RecoveryCodes, rsk, vaultKey);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(rsk);
                CryptographicOperations.ZeroMemory(domainSealKey);
            }
        }

        /// <summary>
        /// Open the recovery store with a single code (no unlock) and return the backed-up keyfile
        /// material. The returned bytes are the caller's responsibility to zeroize.
        /// </summary>
        public byte[] RecoverProtectedKeyfile(byte[] envelopeBytes, string recoveryCode)
        {
            if (envelopeBytes == null) throw new ArgumentNullException(nameof(envelopeBytes));

            using var opened = RecoveryStoreEnvelope.OpenWithCode(envelopeBytes, recoveryCode);
            var keyfile = opened.Store.ProtectedKeyfile;
            if (keyfile == null || keyfile.Length == 0)
                throw new InvalidOperationException("This recovery store does not contain a keyfile backup.");

            return (byte[])keyfile.Clone();
        }

        /// <summary>
        /// Open the recovery store with a single code (no unlock) and unseal one domain key. The
        /// returned key is the caller's responsibility to zeroize.
        /// </summary>
        public byte[] RecoverDomainKey(byte[] envelopeBytes, string recoveryCode, CryptoDomain domain)
        {
            if (envelopeBytes == null) throw new ArgumentNullException(nameof(envelopeBytes));

            using var opened = RecoveryStoreEnvelope.OpenWithCode(envelopeBytes, recoveryCode);

            var sealedKey = domain switch
            {
                CryptoDomain.Obscura => opened.Store.ObscuraSealed,
                CryptoDomain.Attestor => opened.Store.AttestorSealed,
                _ => throw new ArgumentException($"Unsupported recovery domain {domain}.", nameof(domain))
            };

            if (sealedKey == null)
                throw new InvalidOperationException($"No sealed recovery key exists for domain {domain}.");

            var domainSealKey = RecoveryStoreEnvelope.DeriveDomainSealKey(opened.RecoveryStoreKey, opened.Store.Salt);
            try
            {
                return _scopedRecoveryService.UnsealDomainRecoveryKey(sealedKey, domainSealKey, domain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(domainSealKey);
            }
        }
    }
}
