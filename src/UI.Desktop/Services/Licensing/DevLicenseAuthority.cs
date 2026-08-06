using System;
using System.IO;
using System.Security.Cryptography;
using PhantomVault.Core.Models.Licensing;
using PhantomVault.Core.Services.Licensing;

namespace PhantomVault.UI.Services.Licensing
{
    /// <summary>
    /// DEV-ONLY local signing authority. Lets the full purchase/unlock flow be
    /// exercised end-to-end without a real licensing server or shipped key.
    ///
    /// <para>SECURITY: hard-disabled unless the <c>PHANTOM_DEV_LICENSING</c>
    /// environment variable is set to <c>1</c>. In a normal/production build it
    /// is inert — <see cref="IsEnabled"/> is false, no key exists, and the
    /// embedded (placeholder) <see cref="LicensePublicKey"/> keeps every vault
    /// on the Free tier. This is analogous to the existing dev-policy env flag
    /// and must never be enabled in a shipped build.</para>
    /// </summary>
    public sealed class DevLicenseAuthority
    {
        private const string EnableVar = "PHANTOM_DEV_LICENSING";

        private readonly object _gate = new();
        private byte[]? _publicKey;
        private byte[]? _privateKey;

        public static bool IsEnabled =>
            string.Equals(Environment.GetEnvironmentVariable(EnableVar), "1", StringComparison.Ordinal);

        /// <summary>The dev public key, or null when disabled. Used to seed a trusting verifier.</summary>
        public byte[]? PublicKey
        {
            get
            {
                if (!IsEnabled) return null;
                EnsureKeys();
                return _publicKey is null ? null : (byte[])_publicKey.Clone();
            }
        }

        /// <summary>Issues a signed dev token. Throws if dev licensing is disabled.</summary>
        public string IssueToken(PremiumTier tier, string? usbBindingId, TimeSpan duration)
        {
            if (!IsEnabled)
                throw new InvalidOperationException("Dev licensing is disabled.");

            EnsureKeys();
            var now = DateTimeOffset.UtcNow;
            var claims = new LicenseClaims
            {
                LicenseId = "DEV-" + Guid.NewGuid().ToString("N")[..12],
                Tier = tier,
                UsbBindingId = usbBindingId,
                IssuedUtc = now,
                ExpiresUtc = now + duration,
                Features = new() // empty = all features for the tier
            };
            return LicenseTokenCodec.CreateToken(claims, _privateKey!);
        }

        private void EnsureKeys()
        {
            if (_publicKey is not null && _privateKey is not null) return;
            lock (_gate)
            {
                if (_publicKey is not null && _privateKey is not null) return;

                string path = KeyFilePath();
                try
                {
                    if (File.Exists(path))
                    {
                        byte[] blob = File.ReadAllBytes(path);
                        if (blob.Length == 64)
                        {
                            _publicKey = blob[..32];
                            _privateKey = blob[32..];
                            return;
                        }
                    }
                }
                catch (IOException) { /* fall through to regenerate */ }

                var (pub, priv) = LicenseTokenCodec.GenerateKeyPair();
                _publicKey = pub;
                _privateKey = priv;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    byte[] blob = new byte[64];
                    Array.Copy(pub, 0, blob, 0, 32);
                    Array.Copy(priv, 0, blob, 32, 32);
                    File.WriteAllBytes(path, blob);
                    CryptographicOperations.ZeroMemory(blob);
                }
                catch (IOException) { /* in-memory only is fine for dev */ }
            }
        }

        private static string KeyFilePath()
        {
            string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(baseDir, "PhantomVault", "dev-license-key.bin");
        }
    }
}
