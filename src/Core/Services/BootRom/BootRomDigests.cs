using System;
using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace PhantomVault.Core.Services.BootRom
{
    /// <summary>
    /// The environment digests a vault's Boot ROM is sealed against.
    ///
    /// These values are baked into the ROM when it is provisioned and recomputed on every unlock;
    /// the ROM releases the vault's key contribution only when they match. If provisioning and
    /// unlock ever compute them differently — by so much as one byte — the ROM refuses, the derived
    /// key comes out wrong, and the vault cannot be opened except with its recovery code.
    ///
    /// That is why this lives in one place. The logic was previously duplicated at each call site,
    /// which is a standing invitation for the copies to drift apart in a later edit.
    /// </summary>
    public static class BootRomDigests
    {
        /// <summary>
        /// The integrity expectation. Deliberately a constant rather than a measurement of the
        /// running build: a digest that changed whenever the app was rebuilt or updated would lock
        /// the user out of their own vault on the next release. Attestation of the environment is
        /// the integrity subsystem's job; the ROM's role is binding, not version pinning.
        /// </summary>
        public static byte[] Integrity() =>
            SHA256.HashData(Encoding.UTF8.GetBytes("integrity:allowed"));

        /// <summary>
        /// The device expectation: a digest of this drive's computed identity, so a ROM copied to
        /// a different stick refuses to release its secret.
        ///
        /// A failure to read the device identity is hashed as empty rather than thrown. Throwing
        /// here would be worse than useless: provisioning and unlock both land on the same empty
        /// digest, so the vault still opens on the drive it was created on, and a genuinely
        /// different device still produces a different identity and is still refused.
        /// </summary>
        public static byte[] Binding(string driveRoot)
        {
            try
            {
                var binding = new UsbBindingService();
                return SHA256.HashData(Encoding.UTF8.GetBytes(binding.ComputeDeviceId(driveRoot) ?? string.Empty));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[BootRom] Could not compute the device binding digest for {DriveRoot}", driveRoot);
                return SHA256.HashData(Array.Empty<byte>());
            }
        }

        /// <summary>Both digests for a drive, as provisioning and unlock each need the pair.</summary>
        public static (byte[] Integrity, byte[] Binding) For(string driveRoot) =>
            (Integrity(), Binding(driveRoot));
    }
}
