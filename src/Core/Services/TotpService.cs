using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Specifies the HMAC algorithm used for TOTP generation.
    /// RFC 6238 defaults to SHA1 but allows SHA256 and SHA512 for higher security.
    /// </summary>
    public enum TotpAlgorithm
    {
        /// <summary>HMAC-SHA1 (RFC 6238 default, 160-bit digest).</summary>
        SHA1,
        /// <summary>HMAC-SHA256 (256-bit digest, recommended for new implementations).</summary>
        SHA256,
        /// <summary>HMAC-SHA512 (512-bit digest, highest security).</summary>
        SHA512
    }

    /// <summary>
    /// Implements RFC 6238 Time-based One-Time Password (TOTP) generation.
    /// Given a shared secret and the current time, this service produces a
    /// numeric code valid for a short time window (usually 30 seconds). TOTP
    /// is commonly used as a second factor in multi-factor authentication.
    /// Supports SHA1 (default), SHA256, and SHA512 hash algorithms.
    /// </summary>
    public sealed class TotpService
    {
        private const int DefaultTimeStepSeconds = 30;
        private const int DefaultDigits = 6;

        /// <summary>
        /// Generates a new random secret suitable for TOTP. The secret
        /// consists of the specified number of bytes and is returned as a
        /// base32-encoded string without padding.
        /// </summary>
        public static string GenerateSecret(int length = 16)
        {
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
            byte[] bytes = new byte[length];
            RandomNumberGenerator.Fill(bytes);
            return Base32Encode(bytes);
        }

        /// <summary>
        /// Generates a TOTP code from a base32-encoded secret using HMAC-SHA1 (default).
        /// </summary>
        public string GenerateCode(string base32Secret, DateTimeOffset? timestamp = null, int digits = DefaultDigits, int timeStepSeconds = DefaultTimeStepSeconds)
        {
            return GenerateCode(base32Secret, TotpAlgorithm.SHA1, timestamp, digits, timeStepSeconds);
        }

        /// <summary>
        /// Generates a TOTP code from a base32-encoded secret using the specified HMAC algorithm.
        /// SHA256 and SHA512 provide stronger security than the default SHA1.
        /// </summary>
        public string GenerateCode(string base32Secret, TotpAlgorithm algorithm, DateTimeOffset? timestamp = null, int digits = DefaultDigits, int timeStepSeconds = DefaultTimeStepSeconds)
        {
            if (string.IsNullOrWhiteSpace(base32Secret)) throw new ArgumentException("Secret must not be null or empty", nameof(base32Secret));
            byte[] secretBytes = Base32Decode(base32Secret);
            long unixTime = timestamp?.ToUnixTimeSeconds() ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long counter = unixTime / timeStepSeconds;
            byte[] counterBytes = BitConverter.GetBytes(counter);
            if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);

            byte[] hash;
            using (var hmac = CreateHmac(algorithm, secretBytes))
            {
                hash = hmac.ComputeHash(counterBytes);
            }

            int offset = hash[^1] & 0x0F;
            int binaryCode = ((hash[offset] & 0x7F) << 24)
                             | ((hash[offset + 1] & 0xFF) << 16)
                             | ((hash[offset + 2] & 0xFF) << 8)
                             | (hash[offset + 3] & 0xFF);
            int modulus = (int)Math.Pow(10, digits);
            int otp = binaryCode % modulus;
            return otp.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
        }

        private static HMAC CreateHmac(TotpAlgorithm algorithm, byte[] key)
        {
            return algorithm switch
            {
                TotpAlgorithm.SHA256 => new HMACSHA256(key),
                TotpAlgorithm.SHA512 => new HMACSHA512(key),
                _ => new HMACSHA1(key)
            };
        }

        private static string Base32Encode(byte[] data)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            int outputLength = (int)Math.Ceiling(data.Length / 5.0) * 8;
            var result = new StringBuilder(outputLength);
            int bitBuffer = 0;
            int bitsInBuffer = 0;
            foreach (byte b in data)
            {
                bitBuffer = (bitBuffer << 8) | b;
                bitsInBuffer += 8;
                while (bitsInBuffer >= 5)
                {
                    int index = (bitBuffer >> (bitsInBuffer - 5)) & 0x1F;
                    result.Append(alphabet[index]);
                    bitsInBuffer -= 5;
                }
            }
            if (bitsInBuffer > 0)
            {
                int index = (bitBuffer << (5 - bitsInBuffer)) & 0x1F;
                result.Append(alphabet[index]);
            }
            return result.ToString();
        }

        private static byte[] Base32Decode(string input)
        {
            string normalized = input.Trim().TrimEnd('=').Replace(" ", string.Empty).ToUpperInvariant();
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var bits = new List<int>(normalized.Length * 5);
            foreach (char c in normalized)
            {
                int val = alphabet.IndexOf(c);
                if (val < 0) throw new FormatException($"Invalid base32 character: {c}");
                for (int i = 4; i >= 0; i--) bits.Add((val >> i) & 1);
            }
            int byteCount = bits.Count / 8;
            var bytes = new byte[byteCount];
            for (int i = 0; i < byteCount; i++)
            {
                byte b = 0;
                for (int bit = 0; bit < 8; bit++) b = (byte)((b << 1) | bits[i * 8 + bit]);
                bytes[i] = b;
            }
            return bytes;
        }
    }
}
