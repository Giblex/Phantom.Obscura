using System;

namespace PhantomVault.Core.Models.Security
{
    /// <summary>
    /// Host-local, per-vault trust anchor used to detect whole-volume rollback.
    /// Stored off the USB (DPAPI-protected in the user profile) so an attacker who
    /// rolls the device back to an earlier copy cannot also roll back the anchor.
    /// </summary>
    public sealed class VolumeTrustAnchor
    {
        public string VaultId { get; set; } = string.Empty;

        /// <summary>
        /// Highest monotonic vault save-sequence this host has observed for the vault.
        /// </summary>
        public long SaveSequence { get; set; }

        public DateTimeOffset UpdatedUtc { get; set; }
    }
}
