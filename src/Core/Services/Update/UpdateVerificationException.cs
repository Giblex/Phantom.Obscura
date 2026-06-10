using System;

namespace PhantomVault.Core.Services.Update
{
    /// <summary>
    /// Thrown when any update-verification step fails. Message is safe to surface
    /// to the user (no secrets, no internal paths). The <see cref="Reason"/>
    /// enum is the canonical failure category for tests and audit logs.
    /// </summary>
    public sealed class UpdateVerificationException : Exception
    {
        public UpdateVerificationFailure Reason { get; }

        public UpdateVerificationException(UpdateVerificationFailure reason, string message)
            : base(message)
        {
            Reason = reason;
        }

        public UpdateVerificationException(UpdateVerificationFailure reason, string message, Exception inner)
            : base(message, inner)
        {
            Reason = reason;
        }
    }

    public enum UpdateVerificationFailure
    {
        /// <summary>Embedded Ed25519 public key is the placeholder all-zero value.</summary>
        PublicKeyNotProvisioned = 1,
        /// <summary>Signature did not verify against the manifest bytes.</summary>
        BadSignature = 2,
        /// <summary>JSON did not parse, was not a known schema, or was missing required fields.</summary>
        BadManifest = 3,
        /// <summary>Channel did not match the client's configured channel.</summary>
        ChannelMismatch = 4,
        /// <summary>Version is not strictly greater than the installed version.</summary>
        VersionNotNewer = 5,
        /// <summary>Installed version is older than the manifest's minPreviousVersion.</summary>
        UpgradePathBlocked = 6,
        /// <summary>Asset host does not match the configured update host.</summary>
        AssetHostMismatch = 7,
        /// <summary>Asset size on disk did not match manifest.</summary>
        AssetSizeMismatch = 8,
        /// <summary>SHA-256 of the downloaded asset did not match the manifest.</summary>
        AssetHashMismatch = 9,
        /// <summary>Authenticode signature missing, invalid, or chain did not verify.</summary>
        AuthenticodeFailure = 10,
        /// <summary>Authenticode subject or thumbprint did not match the manifest pin.</summary>
        AuthenticodePinMismatch = 11,
    }
}
