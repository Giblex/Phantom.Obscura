using System;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.Json;
using PhantomVault.Core.Models;
using PhantomVault.Core.Models.Security;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Default <see cref="IDeviceEnrollmentService"/>. Transfer strings are
    /// versioned, magic-prefixed Base64URL of compact JSON so they round-trip
    /// cleanly through a QR code or a copy/paste box.
    /// </summary>
    public sealed class DeviceEnrollmentService : IDeviceEnrollmentService
    {
        private const string RequestMagic = "POENR1.";
        private const string GrantMagic = "POGRT1.";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly IDeviceIdentityService _identityService;

        public DeviceEnrollmentService(IDeviceIdentityService identityService)
        {
            _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
        }

        public DeviceEnrollmentRequest CreateEnrollmentRequest(string? friendlyName = null)
        {
            var identity = _identityService.GetOrCreateLocalIdentity(friendlyName);
            return new DeviceEnrollmentRequest
            {
                DeviceId = identity.DeviceId,
                FriendlyName = identity.FriendlyName,
                SigningPublicKeyBase64 = identity.SigningPublicKeyBase64,
                AgreementPublicKeyBase64 = identity.AgreementPublicKeyBase64,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
        }

        public string EncodeRequest(DeviceEnrollmentRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return RequestMagic + Encode(request);
        }

        public DeviceEnrollmentRequest DecodeRequest(string payload)
        {
            var request = Decode<DeviceEnrollmentRequest>(payload, RequestMagic);
            if (!request.IsComplete)
                throw new FormatException("Enrollment request is incomplete or malformed.");
            return request;
        }

        public string EncodeGrant(DeviceEnrollmentGrant grant)
        {
            if (grant == null) throw new ArgumentNullException(nameof(grant));
            return GrantMagic + Encode(grant);
        }

        public DeviceEnrollmentGrant DecodeGrant(string payload)
        {
            var grant = Decode<DeviceEnrollmentGrant>(payload, GrantMagic);
            if (!grant.IsComplete)
                throw new FormatException("Enrollment grant is incomplete or malformed.");
            return grant;
        }

        public DeviceEnrollmentGrant ApproveEnrollment(
            DeviceEnrollmentRequest request,
            byte[] vaultKey,
            VaultManifest manifest,
            byte[] ownerSigningPrivateKey,
            byte[] ownerSigningPublicKey,
            DeviceRole grantedRole = DeviceRole.Member)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (vaultKey == null || vaultKey.Length == 0) throw new ArgumentException("Vault key must not be empty", nameof(vaultKey));
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (!request.IsComplete) throw new SecurityException("Cannot approve an incomplete enrollment request.");

            byte[] wrapped = _identityService.WrapVaultKeyToDevice(vaultKey, request.AgreementPublicKeyBase64);
            string wrappedBase64 = Convert.ToBase64String(wrapped);

            var existing = manifest.TrustedDevices.FirstOrDefault(d =>
                string.Equals(d.DeviceId, request.DeviceId, StringComparison.Ordinal)
                || string.Equals(d.SigningPublicKeyBase64, request.SigningPublicKeyBase64, StringComparison.Ordinal));

            if (existing == null)
            {
                existing = new DeviceFingerprint();
                manifest.TrustedDevices.Add(existing);
            }

            existing.DeviceId = request.DeviceId;
            existing.FriendlyName = request.FriendlyName;
            existing.SigningPublicKeyBase64 = request.SigningPublicKeyBase64;
            existing.AgreementPublicKeyBase64 = request.AgreementPublicKeyBase64;
            existing.Role = grantedRole;
            existing.WrappedVaultKeyBase64 = wrappedBase64;
            existing.TrustedAt = DateTimeOffset.UtcNow;
            existing.LastAccessAt = DateTimeOffset.UtcNow;

            ManifestService.SignManifestEd25519(manifest, ownerSigningPrivateKey, ownerSigningPublicKey);

            return new DeviceEnrollmentGrant
            {
                VaultId = manifest.DeviceId,
                VaultLabel = request.FriendlyName,
                DeviceId = request.DeviceId,
                WrappedVaultKeyBase64 = wrappedBase64,
                ManifestSigningPublicKeyBase64 = manifest.ManifestSigningPublicKeyBase64,
                GrantedRole = grantedRole,
                GrantedAtUtc = DateTimeOffset.UtcNow,
            };
        }

        public byte[] AcceptGrant(DeviceEnrollmentGrant grant)
        {
            if (grant == null) throw new ArgumentNullException(nameof(grant));
            if (!grant.IsComplete) throw new SecurityException("Cannot accept an incomplete enrollment grant.");

            byte[] wrapped = Convert.FromBase64String(grant.WrappedVaultKeyBase64);
            return _identityService.UnwrapVaultKey(wrapped);
        }

        private static string Encode<T>(T value)
        {
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            return Base64UrlEncode(json);
        }

        private static T Decode<T>(string payload, string magic)
        {
            if (string.IsNullOrWhiteSpace(payload)) throw new FormatException("Payload is empty.");
            string trimmed = payload.Trim();
            if (!trimmed.StartsWith(magic, StringComparison.Ordinal))
                throw new FormatException("Payload has an unexpected or wrong format prefix.");

            byte[] json = Base64UrlDecode(trimmed.Substring(magic.Length));
            T? value = JsonSerializer.Deserialize<T>(json, JsonOptions);
            if (value == null) throw new FormatException("Payload could not be parsed.");
            return value;
        }

        private static string Base64UrlEncode(byte[] data)
        {
            return Convert.ToBase64String(data)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static byte[] Base64UrlDecode(string value)
        {
            string s = value.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }
    }
}
