using System;
using PhantomVault.Core.Models.Security;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Tracks cumulative security incident score with time decay.
    /// Used by Defence Engine to determine vault security state.
    /// </summary>
    public sealed class IncidentState
    {
        /// <summary>
        /// Cumulative incident score from threat events.
        /// Decays exponentially over time (default 30 minutes).
        /// </summary>
        public double Score { get; set; }

        /// <summary>
        /// Current vault security state based on score thresholds.
        /// Normal (less than 4), Suspicious (4 or greater), Compromised (10 or greater).
        /// </summary>
        public VaultSecurityState State { get; set; } = VaultSecurityState.Normal;

        /// <summary>
        /// Timestamp of last incident score update.
        /// Used to calculate time decay when new threats occur.
        /// </summary>
        public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
    }
}
