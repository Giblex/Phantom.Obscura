using System;
using System.Linq;
using Yubico.YubiKey;
using Yubico.YubiKey.Fido2;
using Yubico.YubiKey.Fido2.Commands;

namespace PhantomVault.Core.Services
{

    public sealed class YubiKeyServiceImpl
    {

        public bool IsTokenPresent()
        {
            try
            {
                var devices = YubiKeyDevice.FindAll().ToList();
                return devices.Count > 0;
            }
            catch (Exception)
            {

                return false;
            }
        }

        public (int serialNumber, string firmwareVersion, bool hasOtp, bool hasFido2)? GetDeviceInfo()
        {
            try
            {
                var device = YubiKeyDevice.FindAll().FirstOrDefault();
                if (device == null) return null;

                return (
                    device.SerialNumber ?? 0,
                    device.FirmwareVersion.ToString(),
                    device.HasFeature(YubiKeyFeature.OtpApplication),
                    device.HasFeature(YubiKeyFeature.Fido2Application)
                );
            }
            catch (Exception)
            {
                return null;
            }
        }

        public byte[]? Authenticate(string relyingPartyId, byte[] credentialId, byte[] challenge)
        {
            if (string.IsNullOrEmpty(relyingPartyId))
                throw new ArgumentException("Relying party ID cannot be empty.", nameof(relyingPartyId));
            if (credentialId == null || credentialId.Length == 0)
                throw new ArgumentException("Credential ID cannot be empty.", nameof(credentialId));
            if (challenge == null || challenge.Length != 32)
                throw new ArgumentException("Challenge must be exactly 32 bytes.", nameof(challenge));

            var device = YubiKeyDevice.FindAll()
                .FirstOrDefault(d => d.HasFeature(YubiKeyFeature.Fido2Application))
                ?? throw new InvalidOperationException("No YubiKey with FIDO2 support found.");

            using var session = new Fido2Session(device);

            session.KeyCollector = data => data.Request == KeyEntryRequest.TouchRequest;

            var rp = new RelyingParty(relyingPartyId);
            var assertParams = new GetAssertionParameters(rp, new ReadOnlyMemory<byte>(challenge));

            var cred = new CredentialId
            {
                Id = new ReadOnlyMemory<byte>(credentialId),
                Type = "public-key"
            };
            assertParams.AllowCredential(cred);

            var assertions = session.GetAssertions(assertParams);
            if (assertions.Count == 0)
                return null;

            var assertion = assertions[0];
            var authDataBytes = assertion.AuthenticatorData.EncodedAuthenticatorData.ToArray();
            var sigBytes = assertion.Signature.ToArray();

            var result = new byte[4 + authDataBytes.Length + sigBytes.Length];
            result[0] = (byte)(authDataBytes.Length >> 24);
            result[1] = (byte)(authDataBytes.Length >> 16);
            result[2] = (byte)(authDataBytes.Length >> 8);
            result[3] = (byte)(authDataBytes.Length);
            Buffer.BlockCopy(authDataBytes, 0, result, 4, authDataBytes.Length);
            Buffer.BlockCopy(sigBytes, 0, result, 4 + authDataBytes.Length, sigBytes.Length);
            return result;
        }

        public byte[] RegisterCredential(string relyingPartyId, byte[] userId, string userName, byte[] challenge)
        {
            if (string.IsNullOrEmpty(relyingPartyId))
                throw new ArgumentException("Relying party ID cannot be empty.", nameof(relyingPartyId));
            if (userId == null || userId.Length == 0)
                throw new ArgumentException("User ID cannot be empty.", nameof(userId));
            if (string.IsNullOrEmpty(userName))
                throw new ArgumentException("User name cannot be empty.", nameof(userName));
            if (challenge == null || challenge.Length != 32)
                throw new ArgumentException("Challenge must be exactly 32 bytes.", nameof(challenge));

            var device = YubiKeyDevice.FindAll()
                .FirstOrDefault(d => d.HasFeature(YubiKeyFeature.Fido2Application))
                ?? throw new InvalidOperationException("No YubiKey with FIDO2 support found.");

            using var session = new Fido2Session(device);

            session.KeyCollector = data => data.Request == KeyEntryRequest.TouchRequest;

            var rp = new RelyingParty(relyingPartyId);
            var user = new UserEntity(new ReadOnlyMemory<byte>(userId))
            {
                Name = userName
            };

            var makeCredParams = new MakeCredentialParameters(rp, user);
            makeCredParams.ClientDataHash = new ReadOnlyMemory<byte>(challenge);

            var credData = session.MakeCredential(makeCredParams);

            var authData = credData.AuthenticatorData
                ?? throw new InvalidOperationException("YubiKey returned a credential with no AuthenticatorData.");
            var credentialId = authData.CredentialId
                ?? throw new InvalidOperationException("YubiKey credential had no CredentialId.");
            return credentialId.Id.ToArray();
        }

        public string? GenerateTotpCode(string accountName)
        {
            if (string.IsNullOrEmpty(accountName))
                throw new ArgumentException("Account name cannot be empty.", nameof(accountName));

            string issuer = string.Empty;
            string account = accountName;
            var colon = accountName.IndexOf(':');
            if (colon > 0 && colon < accountName.Length - 1)
            {
                issuer = accountName.Substring(0, colon);
                account = accountName.Substring(colon + 1);
            }

            try
            {
                return new YubiKeyService().GenerateOathTotpCode(issuer, account);
            }
            catch
            {

                return null;
            }
        }

        public void ResetFido2Application()
        {
            var device = YubiKeyDevice.FindAll()
                .FirstOrDefault(d => d.HasFeature(YubiKeyFeature.Fido2Application))
                ?? throw new InvalidOperationException("No YubiKey with FIDO2 support found.");

            using var connection = device.Connect(YubiKeyApplication.Fido2);
            var response = connection.SendCommand(new ResetCommand());
            if (response.Status != ResponseStatus.Success)
                throw new InvalidOperationException(
                    $"FIDO2 reset failed with status {response.Status}: {response.StatusMessage}");
        }
    }
}

