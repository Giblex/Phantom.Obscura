using System;
using System.Security.Cryptography;
using System.Text;

namespace PhantomVault.Core.Services.Security
{
    public enum VolumeIntegrityVerdict
    {
        /// <summary>On-disk state is at or ahead of the last-known-good anchor.</summary>
        Ok,

        /// <summary>No anchor exists yet for this vault on this host (trust on first use).</summary>
        FirstUse,

        /// <summary>On-disk save-sequence is BEHIND the last-known-good anchor — the volume
        /// was rolled back to an earlier copy.</summary>
        Rollback
    }

    /// <summary>
    /// Pure, side-effect-free rollback evaluation. Persistence of the anchor is the
    /// caller's responsibility (host-local DPAPI store on the desktop). Kept in Core so
    /// the comparison and vault-identity logic is unit-testable without DPAPI.
    /// </summary>
    public static class VolumeRollbackEvaluator
    {
        public static VolumeIntegrityVerdict Evaluate(long? knownSequence, long currentSequence)
        {
            if (knownSequence is null)
                return VolumeIntegrityVerdict.FirstUse;

            return currentSequence < knownSequence.Value
                ? VolumeIntegrityVerdict.Rollback
                : VolumeIntegrityVerdict.Ok;
        }

        /// <summary>
        /// Derives a stable, non-secret identifier for a vault from its device binding and
        /// salt, so multiple vaults on one device each get an independent anchor. Returns
        /// null when there is insufficient identity material to anchor reliably.
        /// </summary>
        public static string? ComputeVaultId(string? deviceId, string? saltBase64)
        {
            if (string.IsNullOrWhiteSpace(deviceId) && string.IsNullOrWhiteSpace(saltBase64))
                return null;

            var material = Encoding.UTF8.GetBytes($"{deviceId}|{saltBase64}");
            var hash = SHA256.HashData(material);
            return Convert.ToHexString(hash);
        }
    }
}
