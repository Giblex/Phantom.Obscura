using System.Security.Cryptography;
using System.Text;

namespace GiblexVault.Security.ZK.Util
{
    public static class DeviceBinding
    {
        /// <summary>
        /// Derives a device-specific salt by hashing the device identifier with an
        /// application-level salt using SHA-512. The device identifier should be a
        /// cryptographically strong value (e.g., GPT partition GUID hash) when available.
        /// </summary>
        /// <param name="deviceId">Device identifier (hex-encoded, from UsbBindingService.ComputeDeviceId).</param>
        /// <param name="appSalt">Application-level salt for domain separation.</param>
        /// <returns>64-byte device-specific salt.</returns>
        public static byte[] DeviceSalt(string deviceId, byte[] appSalt)
        {
            var data = Encoding.UTF8.GetBytes(deviceId);
            var combined = new byte[data.Length + appSalt.Length];
            Buffer.BlockCopy(data, 0, combined, 0, data.Length);
            Buffer.BlockCopy(appSalt, 0, combined, data.Length, appSalt.Length);
            return SHA512.HashData(combined);
        }
    }
}