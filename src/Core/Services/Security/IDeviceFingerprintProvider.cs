using PhantomVault.Core.Models.Security;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Generates stable device fingerprints for detecting vault access from new/unknown devices.
    /// Fingerprints are based on hardware, OS, and user context to provide consistent identification
    /// while remaining offline (no network-based identifiers).
    /// </summary>
    public interface IDeviceFingerprintProvider
    {
        /// <summary>
        /// Generates a fingerprint for the current device and user context.
        /// The fingerprint includes machine ID, OS family/version, hostname, and username.
        /// </summary>
        /// <returns>A DeviceFingerprint representing the current device.</returns>
        DeviceFingerprint GetCurrentFingerprint();
    }
}
