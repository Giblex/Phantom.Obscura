using System;
using PhantomVault.Core.Models.Licensing;

namespace PhantomVault.Core.Services.Licensing
{
    public interface ILicenseVerifier
    {
        /// <summary>
        /// Verifies a stored license token and returns the resulting status.
        /// Always returns a safe Free status on any failure.
        /// </summary>
        /// <param name="token">The signed token from the manifest, or null.</param>
        /// <param name="currentUsbBindingId">The active vault's USB/device binding id, for binding checks.</param>
        /// <param name="offlineGrace">Grace window after expiry during which the license still unlocks (offline tolerance).</param>
        LicenseStatus Verify(string? token, string? currentUsbBindingId, TimeSpan offlineGrace);
    }
}
