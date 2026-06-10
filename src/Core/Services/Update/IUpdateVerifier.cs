using System;
using System.IO;

namespace PhantomVault.Core.Services.Update
{
    /// <summary>
    /// Verifies the integrity, authority, and identity of an update payload.
    /// Three independent gates, all of which must pass:
    ///   1. Ed25519 signature on the manifest bytes (offline-key authority).
    ///   2. SHA-256 hash of the downloaded asset matches the manifest.
    ///   3. (Windows only, when configured) Authenticode signature on the
    ///      asset chains to a trusted root, AND the signer subject +
    ///      SHA-256 thumbprint match the values pinned in the manifest.
    ///
    /// Every failure raises <see cref="UpdateVerificationException"/> with a
    /// canonical <see cref="UpdateVerificationFailure"/> reason; success is
    /// represented by an <see cref="UpdateInfo"/> for gate 1, and a normal
    /// (non-throwing) return for gates 2 + 3.
    /// </summary>
    public interface IUpdateVerifier
    {
        /// <summary>
        /// Verify the Ed25519 signature on the manifest bytes and return the
        /// parsed <see cref="UpdateInfo"/>. The bytes are verified verbatim;
        /// callers must pass the exact bytes that were signed (no
        /// re-canonicalization). Refuses to run if
        /// <see cref="UpdatePublicKey.IsProvisioned"/> is false.
        /// </summary>
        /// <param name="manifestBytes">Raw bytes of <c>manifest.json</c>.</param>
        /// <param name="signature">Raw bytes of the detached Ed25519 signature
        /// (64 bytes).</param>
        /// <param name="expectedChannel">Channel the client is configured for.
        /// Manifests on a different channel are rejected.</param>
        /// <param name="installedVersion">Currently-installed application version.
        /// The manifest version must be strictly greater.</param>
        /// <param name="expectedAssetHost">Host the asset URL must use. Defence
        /// against a forged manifest pointing the binary at an unexpected
        /// host (even though the manifest itself was signed).</param>
        UpdateInfo VerifyManifest(
            ReadOnlySpan<byte> manifestBytes,
            ReadOnlySpan<byte> signature,
            UpdateChannel expectedChannel,
            Version installedVersion,
            string expectedAssetHost);

        /// <summary>
        /// Verify a downloaded asset. Computes SHA-256 of the file at
        /// <paramref name="assetPath"/>, compares against
        /// <see cref="UpdateInfo.AssetSha256"/>, then (on Windows when
        /// <see cref="UpdateInfo.AuthenticodeSubject"/> is set) runs
        /// WinVerifyTrust and pins the signer subject + thumbprint.
        /// </summary>
        void VerifyAsset(string assetPath, UpdateInfo info);

        /// <summary>
        /// Stream-friendly variant for verifying as the asset is being
        /// written to disk. Hashes the stream and compares to the manifest.
        /// Authenticode is NOT checked here (file-only); call
        /// <see cref="VerifyAsset"/> after the file is fully on disk.
        /// </summary>
        void VerifyAssetStream(Stream stream, UpdateInfo info);
    }
}
