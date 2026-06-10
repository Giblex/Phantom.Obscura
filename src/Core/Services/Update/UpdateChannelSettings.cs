using System;

namespace PhantomVault.Core.Services.Update
{
    /// <summary>
    /// User-facing update preferences. Persisted by the UI/settings layer
    /// (Phase D); the service consumes them as a snapshot per operation.
    /// </summary>
    public sealed class UpdateChannelSettings
    {
        /// <summary>Stable is the only safe default.</summary>
        public UpdateChannel Channel { get; set; } = UpdateChannel.Stable;

        /// <summary>
        /// When false, the service only checks on explicit user action.
        /// Default false — never reach out unprompted.
        /// </summary>
        public bool AutoCheckEnabled { get; set; }

        /// <summary>
        /// Minimum gap between auto-checks. Ignored when AutoCheckEnabled is false.
        /// </summary>
        public TimeSpan AutoCheckInterval { get; set; } = TimeSpan.FromHours(24);
    }
}
