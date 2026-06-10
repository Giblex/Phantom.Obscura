using System;
using PhantomVault.Core.Services;

namespace PhantomVault.Core.Services.DomainKeys
{
    /// <summary>
    /// Orchestrates the keyfile-only recovery flow for the USB-bound vault. At provisioning it seals
    /// the mandatory keyfile material under a fresh set of recovery codes and produces a single
    /// self-describing recovery file that the user exports OFF the USB. If the USB is later lost or
    /// damaged, any one recovery code reopens that exported file (no unlock, no vault key) and yields
    /// the original keyfile bytes, which can be rewritten onto a new USB to restore access.
    ///
    /// This is the honest, USB-bound answer to the historical recovery Catch-22: the only mandatory
    /// credential (the keyfile) is what gets backed up, and recovery never needs an already-unlocked
    /// vault.
    /// </summary>
    public sealed class KeyfileRecoveryBundleService
    {
        public const int DefaultRecoveryCodeCount = 10;

        private readonly RecoveryStoreFactory _factory;
        private readonly RecoveryCodeService _codeService;

        public KeyfileRecoveryBundleService(RecoveryStoreFactory factory, RecoveryCodeService codeService)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _codeService = codeService ?? throw new ArgumentNullException(nameof(codeService));
        }

        /// <summary>
        /// The result of sealing a keyfile for recovery. <see cref="RecoveryCodes"/> are shown to the
        /// user once (each can open the file); <see cref="RecoveryFileBytes"/> is the exportable blob.
        /// </summary>
        public sealed class RecoveryBundle
        {
            public RecoveryBundle(string[] recoveryCodes, byte[] recoveryFileBytes)
            {
                RecoveryCodes = recoveryCodes;
                RecoveryFileBytes = recoveryFileBytes;
            }

            public string[] RecoveryCodes { get; }
            public byte[] RecoveryFileBytes { get; }
        }

        /// <summary>
        /// Seal <paramref name="keyfileBytes"/> under a fresh set of recovery codes and return both the
        /// plaintext codes and the exportable recovery file. The caller must persist the file OFF the
        /// USB and have the user record the codes before finishing setup.
        /// </summary>
        public RecoveryBundle Create(
            byte[] keyfileBytes,
            int codeCount = DefaultRecoveryCodeCount,
            byte[]? vaultRecoveryKey = null,
            string? deviceId = null,
            string? appVersion = null)
        {
            if (keyfileBytes == null || keyfileBytes.Length == 0)
                throw new ArgumentException("Keyfile material is required to build a recovery bundle.", nameof(keyfileBytes));

            var codes = _codeService.GenerateRecoveryCodes(codeCount);

            var envelope = _factory.Seed(new RecoveryStoreFactory.SeedInput
            {
                ProtectedKeyfile = keyfileBytes,
                RecoveryCodes = codes,
                VaultRecoveryKey = vaultRecoveryKey,
                DeviceId = deviceId,
                SetupAppVersion = appVersion
            }, out _);

            return new RecoveryBundle(codes, envelope);
        }

        /// <summary>
        /// Reopen an exported recovery file with a single recovery code and return the original keyfile
        /// bytes. No unlocked vault and no vault key are required. The returned bytes are the caller's
        /// responsibility to zeroize after rewriting the keyfile.
        /// </summary>
        public byte[] RestoreKeyfile(byte[] recoveryFileBytes, string recoveryCode)
        {
            if (recoveryFileBytes == null || recoveryFileBytes.Length == 0)
                throw new ArgumentException("Recovery file is required.", nameof(recoveryFileBytes));
            if (string.IsNullOrWhiteSpace(recoveryCode))
                throw new ArgumentException("A recovery code is required.", nameof(recoveryCode));

            return _factory.RecoverProtectedKeyfile(recoveryFileBytes, recoveryCode);
        }

        /// <summary>True if <paramref name="data"/> looks like an exported Phantom recovery file.</summary>
        public bool IsRecoveryFile(byte[] data) => RecoveryStoreEnvelope.IsEnvelope(data);
    }
}
