using System;
using System.Linq;
using Yubico.YubiKey;
using Yubico.YubiKey.Fido2;
using Yubico.YubiKey.Fido2.Commands;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Production implementation of YubiKey integration using the official
    /// Yubico.YubiKey SDK. Supports FIDO2 authentication for vault unlocking.
    /// Requires YubiKey firmware 5.0 or later for full FIDO2 support.
    /// </summary>
    public sealed class YubiKeyServiceImpl
    {
        /// <summary>
        /// Determines whether a YubiKey hardware token is currently connected.
        /// Enumerates all available YubiKey devices on the system.
        /// </summary>
        /// <returns>True if at least one YubiKey is present, false otherwise.</returns>
        public bool IsTokenPresent()
        {
            try
            {
                var devices = YubiKeyDevice.FindAll().ToList();
                return devices.Count > 0;
            }
            catch (Exception)
            {
                // If enumeration fails, assume no devices present
                return false;
            }
        }

        /// <summary>
        /// Gets information about the first connected YubiKey device.
        /// Useful for displaying device details to the user.
        /// </summary>
        /// <returns>Tuple of (serialNumber, firmwareVersion, hasOtp, hasFido2) or null if no device.</returns>
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

        /// <summary>
        /// Performs FIDO2 authentication using the YubiKey. The user must touch
        /// the YubiKey to complete the assertion. Returns concatenated
        /// authenticator data and signature bytes for caller verification.
        /// </summary>
        /// <param name="relyingPartyId">The relying party identifier (e.g., "phantomvault.local").</param>
        /// <param name="credentialId">The credential ID from registration (stored in manifest).</param>
        /// <param name="challenge">A 32-byte random challenge for this authentication attempt.</param>
        /// <returns>
        /// Encoded response: [authDataLen(4 bytes, big-endian)] + authData + signature,
        /// or null if no assertion was returned by the authenticator.
        /// </returns>
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
            // Return true for touch prompts; decline PIN prompts (no PIN UI at this layer).
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

            // Encode as [authDataLen (4 bytes, big-endian)] + authData + signature
            // so callers can split the two fields without extra framing.
            var result = new byte[4 + authDataBytes.Length + sigBytes.Length];
            result[0] = (byte)(authDataBytes.Length >> 24);
            result[1] = (byte)(authDataBytes.Length >> 16);
            result[2] = (byte)(authDataBytes.Length >> 8);
            result[3] = (byte)(authDataBytes.Length);
            Buffer.BlockCopy(authDataBytes, 0, result, 4, authDataBytes.Length);
            Buffer.BlockCopy(sigBytes, 0, result, 4 + authDataBytes.Length, sigBytes.Length);
            return result;
        }

        /// <summary>
        /// Registers a new FIDO2 credential on the YubiKey. This should be called
        /// during vault provisioning to bind the vault to this specific YubiKey.
        /// The user must touch the YubiKey to complete registration.
        /// The returned credential ID must be stored in the vault manifest.
        /// </summary>
        /// <param name="relyingPartyId">The relying party identifier.</param>
        /// <param name="userId">User identifier (can be the vault ID).</param>
        /// <param name="userName">User display name.</param>
        /// <param name="challenge">A 32-byte random challenge for registration.</param>
        /// <returns>The credential ID that must be stored for future authentication.</returns>
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
            // Return true for touch prompts; decline PIN prompts (no PIN UI at this layer).
            session.KeyCollector = data => data.Request == KeyEntryRequest.TouchRequest;

            var rp = new RelyingParty(relyingPartyId);
            var user = new UserEntity(new ReadOnlyMemory<byte>(userId))
            {
                Name = userName
            };

            var makeCredParams = new MakeCredentialParameters(rp, user);
            makeCredParams.ClientDataHash = new ReadOnlyMemory<byte>(challenge);

            var credData = session.MakeCredential(makeCredParams);
            // YubiKey SDK contract: a successful MakeCredential always returns a
            // populated AuthenticatorData with a non-empty CredentialId. The
            // nullable analyser can't see that — defensive null check turns a
            // crash into a clear error.
            var authData = credData.AuthenticatorData
                ?? throw new InvalidOperationException("YubiKey returned a credential with no AuthenticatorData.");
            var credentialId = authData.CredentialId
                ?? throw new InvalidOperationException("YubiKey credential had no CredentialId.");
            return credentialId.Id.ToArray();
        }

        /// <summary>
        /// Generates a TOTP code if the YubiKey has an OATH credential matching
        /// <paramref name="accountName"/>. Delegates to the real OATH implementation
        /// on <see cref="YubiKeyService"/>; returns null when no matching credential
        /// or OATH-capable device is present.
        /// </summary>
        /// <param name="accountName">The OATH account name (e.g., "PhantomVault:vault1").</param>
        /// <returns>A TOTP code, or null if not available.</returns>
        public string? GenerateTotpCode(string accountName)
        {
            if (string.IsNullOrEmpty(accountName))
                throw new ArgumentException("Account name cannot be empty.", nameof(accountName));

            // Account labels are conventionally "issuer:account"; split once if present.
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
                // Mirror previous contract: OATH TOTP is an optional path; never throw upstream.
                return null;
            }
        }

        /// <summary>
        /// Resets the FIDO2 application on the YubiKey. WARNING: This will delete
        /// all stored FIDO2 credentials and the FIDO2 PIN. Only use this when
        /// re-provisioning. Must be performed within 5 seconds of inserting the
        /// YubiKey; the user must touch the key to confirm.
        /// </summary>
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
