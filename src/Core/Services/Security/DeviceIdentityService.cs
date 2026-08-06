using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using GiblexVault.Security.ZK.Keys;
using GiblexVault.Security.ZK.Signing;
using GiblexVault.Security.ZK.Util;
using PhantomVault.Core.Models.Security;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Default implementation of <see cref="IDeviceIdentityService"/>. Generates an
    /// Ed25519 signing keypair and an X25519 key-agreement keypair on first use,
    /// seals both private keys at rest with the platform key protector (DPAPI on
    /// Windows, file-sealed on Unix), and persists them under
    /// <c>%APPDATA%\PhantomVault\identity\</c>. Public keys form the device identity.
    /// </summary>
    public sealed class DeviceIdentityService : IDeviceIdentityService
    {
        private const string IdentityFolderName = "identity";
        private const string IdentityFileName = "device-identity.json";

        private readonly string _identityDirectory;
        private readonly object _gate = new();
        private StoredIdentity? _cached;

        public DeviceIdentityService(string? identityDirectory = null)
        {
            _identityDirectory = identityDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhantomVault",
                IdentityFolderName);
        }

        public DeviceIdentity GetOrCreateLocalIdentity(string? friendlyName = null)
        {
            var stored = EnsureLoaded(friendlyName);
            return new DeviceIdentity
            {
                DeviceId = stored.DeviceId,
                FriendlyName = stored.FriendlyName,
                SigningPublicKeyBase64 = stored.SigningPublicKeyBase64,
                AgreementPublicKeyBase64 = stored.AgreementPublicKeyBase64,
                Role = stored.Role,
                EnrolledAtUtc = stored.EnrolledAtUtc,
            };
        }

        public byte[] Sign(byte[] message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            var stored = EnsureLoaded(null);
            byte[] priv = SecurityTuning.KeyProtector.Unprotect(
                Convert.FromBase64String(stored.SigningPrivateKeySealedBase64));
            try
            {
                return UserKeys.Sign(message, priv);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(priv);
            }
        }

        public bool Verify(byte[] message, byte[] signature, string signingPublicKeyBase64)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (signature == null) throw new ArgumentNullException(nameof(signature));
            if (string.IsNullOrWhiteSpace(signingPublicKeyBase64)) return false;
            try
            {
                return UserKeys.Verify(message, signature, Convert.FromBase64String(signingPublicKeyBase64));
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public byte[] WrapVaultKeyToDevice(byte[] vaultKey, string targetAgreementPublicKeyBase64)
        {
            if (vaultKey == null || vaultKey.Length == 0) throw new ArgumentException("Vault key must not be empty", nameof(vaultKey));
            if (string.IsNullOrWhiteSpace(targetAgreementPublicKeyBase64)) throw new ArgumentException("Target agreement public key must be provided", nameof(targetAgreementPublicKeyBase64));
            return DeviceAgreementKeys.WrapVaultKey(vaultKey, Convert.FromBase64String(targetAgreementPublicKeyBase64));
        }

        public byte[] UnwrapVaultKey(byte[] wrappedVaultKey)
        {
            if (wrappedVaultKey == null || wrappedVaultKey.Length == 0) throw new ArgumentException("Wrapped vault key must not be empty", nameof(wrappedVaultKey));
            var stored = EnsureLoaded(null);
            return DeviceAgreementKeys.UnwrapVaultKey(
                wrappedVaultKey,
                Convert.FromBase64String(stored.AgreementPrivateKeySealedBase64));
        }

        private StoredIdentity EnsureLoaded(string? friendlyName)
        {
            lock (_gate)
            {
                if (_cached != null)
                    return _cached;

                string path = Path.Combine(_identityDirectory, IdentityFileName);
                if (File.Exists(path))
                {
                    try
                    {
                        var loaded = JsonSerializer.Deserialize<StoredIdentity>(File.ReadAllText(path));
                        if (loaded != null && loaded.IsComplete)
                        {
                            _cached = loaded;
                            return _cached;
                        }
                    }
                    catch (Exception ex) when (ex is JsonException or IOException)
                    {
                        // Fall through and regenerate a fresh identity.
                    }
                }

                _cached = CreateAndPersist(friendlyName, path);
                return _cached;
            }
        }

        private static StoredIdentity CreateAndPersist(string? friendlyName, string path)
        {
            var (signPub, signPriv) = UserKeys.Generate();
            byte[] signSealed = SecurityTuning.KeyProtector.Protect(signPriv);
            CryptographicOperations.ZeroMemory(signPriv);

            var (agreePub, agreeSealed) = DeviceAgreementKeys.CreateSealed();

            var identity = new StoredIdentity
            {
                DeviceId = Guid.NewGuid().ToString("N"),
                FriendlyName = string.IsNullOrWhiteSpace(friendlyName) ? Environment.MachineName : friendlyName,
                SigningPublicKeyBase64 = Convert.ToBase64String(signPub),
                AgreementPublicKeyBase64 = Convert.ToBase64String(agreePub),
                SigningPrivateKeySealedBase64 = Convert.ToBase64String(signSealed),
                AgreementPrivateKeySealedBase64 = Convert.ToBase64String(agreeSealed),
                Role = DeviceRole.Owner,
                EnrolledAtUtc = DateTimeOffset.UtcNow,
            };

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(identity));
            return identity;
        }

        /// <summary>On-disk identity record. Private keys are stored sealed, never raw.</summary>
        private sealed class StoredIdentity
        {
            public string DeviceId { get; set; } = string.Empty;
            public string? FriendlyName { get; set; }
            public string SigningPublicKeyBase64 { get; set; } = string.Empty;
            public string AgreementPublicKeyBase64 { get; set; } = string.Empty;
            public string SigningPrivateKeySealedBase64 { get; set; } = string.Empty;
            public string AgreementPrivateKeySealedBase64 { get; set; } = string.Empty;
            public DeviceRole Role { get; set; } = DeviceRole.Owner;
            public DateTimeOffset EnrolledAtUtc { get; set; } = DateTimeOffset.UtcNow;

            public bool IsComplete =>
                !string.IsNullOrWhiteSpace(DeviceId)
                && !string.IsNullOrWhiteSpace(SigningPublicKeyBase64)
                && !string.IsNullOrWhiteSpace(AgreementPublicKeyBase64)
                && !string.IsNullOrWhiteSpace(SigningPrivateKeySealedBase64)
                && !string.IsNullOrWhiteSpace(AgreementPrivateKeySealedBase64);
        }
    }
}
