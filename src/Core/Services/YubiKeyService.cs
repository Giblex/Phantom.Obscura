using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Yubico.YubiKey;
using Yubico.YubiKey.Fido2;
using Yubico.YubiKey.Fido2.Commands;
using Yubico.YubiKey.Fido2.Cose;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Result from FIDO2 credential registration.
    /// </summary>
    public sealed class Fido2CredentialResult
    {
        /// <summary>
        /// FIDO2 credential identifier (opaque byte array).
        /// </summary>
        public byte[] CredentialId { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// COSE-encoded public key for signature verification.
        /// </summary>
        public byte[] PublicKey { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Attestation format (e.g., "packed", "none").
        /// </summary>
        public string AttestationFormat { get; set; } = string.Empty;

        /// <summary>
        /// YubiKey serial number for device binding.
        /// </summary>
        public int SerialNumber { get; set; }
    }

    /// <summary>
    /// Result from FIDO2 assertion (authentication).
    /// </summary>
    public sealed class Fido2AssertionResult
    {
        /// <summary>
        /// The assertion signature bytes.
        /// </summary>
        public byte[] Signature { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Raw authenticator data from the assertion.
        /// </summary>
        public byte[] AuthenticatorData { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// The credential ID that produced this assertion.
        /// </summary>
        public byte[] CredentialId { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Whether the assertion signature was verified successfully.
        /// </summary>
        public bool IsValid { get; set; }
    }

    /// <summary>
    /// Service for YubiKey hardware token integration using FIDO2.
    /// Provides hardware-based authentication using YubiKey's FIDO2 capabilities
    /// with challenge-response nonce-based authentication.
    ///
    /// <para>
    /// <b>Requirements:</b>
    /// - YubiKey firmware 5.0 or later for FIDO2 support
    /// - Yubico.YubiKey SDK 1.12.0
    /// - User must touch the YubiKey to complete authentication
    /// </para>
    /// </summary>
    public sealed class YubiKeyService
    {
        private const string DefaultRelyingPartyId = "phantomvault.local";
        private const string RelyingPartyName = "PhantomVault";

        /// <summary>
        /// Determines whether a YubiKey hardware token is currently connected.
        /// </summary>
        public bool IsTokenPresent()
        {
            try
            {
                var devices = YubiKeyDevice.FindAll().ToList();
                return devices.Count > 0;
            }
            catch (Exception ex) when (
                ex is PlatformNotSupportedException ||
                ex is System.IO.IOException ||
                ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Registers a new FIDO2 credential on the YubiKey for vault authentication.
        /// Creates a discoverable (resident) credential bound to this vault.
        /// </summary>
        /// <param name="userId">User identifier bytes (must be unique per vault).</param>
        /// <param name="userName">Display name for the user.</param>
        /// <param name="pin">PIN for YubiKey FIDO2 application (required if PIN is set).</param>
        /// <param name="relyingPartyId">Relying party identifier.</param>
        /// <returns>Credential result containing credential ID, public key, and serial number.</returns>
        public Fido2CredentialResult RegisterCredential(
            string userId,
            string userName,
            string? pin = null,
            string relyingPartyId = DefaultRelyingPartyId)
        {
            if (string.IsNullOrEmpty(userId))
                throw new ArgumentException("User ID cannot be empty", nameof(userId));
            if (string.IsNullOrEmpty(userName))
                throw new ArgumentException("User name cannot be empty", nameof(userName));

            var device = FindFido2Device();
            var serial = device.SerialNumber ?? 0;

            try
            {
                using var fido2Session = new Fido2Session(device);

                // Verify PIN if required
                VerifyPinIfNeeded(fido2Session, pin, PinUvAuthTokenPermissions.MakeCredential, relyingPartyId);

                // Build credential parameters
                var rp = new RelyingParty(relyingPartyId) { Name = RelyingPartyName };
                var userIdBytes = Encoding.UTF8.GetBytes(userId);
                var user = new UserEntity(new ReadOnlyMemory<byte>(userIdBytes))
                {
                    Name = userName,
                    DisplayName = userName
                };

                // Generate a fresh challenge for the client data hash
                var challenge = new byte[32];
                RandomNumberGenerator.Fill(challenge);

                // ClientDataHash is SHA-256 of the challenge (simulating WebAuthn clientDataJSON)
                var clientDataHash = SHA256.HashData(challenge);

                var mcParams = new MakeCredentialParameters(rp, user)
                {
                    ClientDataHash = new ReadOnlyMemory<byte>(clientDataHash)
                };

                // Make credential discoverable (resident key) for passwordless flows
                mcParams.AddOption(AuthenticatorOptions.rk, true);

                // Register the credential - user must touch YubiKey
                var mcData = fido2Session.MakeCredential(mcParams);

                // Extract credential ID and public key
                var credentialId = mcData.AuthenticatorData.CredentialId?.Id.ToArray()
                    ?? throw new InvalidOperationException("YubiKey returned no credential ID.");
                var publicKey = mcData.AuthenticatorData.CredentialPublicKey?.Encode()
                    ?? throw new InvalidOperationException("YubiKey returned no public key.");

                return new Fido2CredentialResult
                {
                    CredentialId = credentialId,
                    PublicKey = publicKey,
                    AttestationFormat = mcData.Format ?? "unknown",
                    SerialNumber = serial
                };
            }
            catch (TimeoutException)
            {
                throw new TimeoutException("YubiKey registration timed out. Please touch the device when prompted.");
            }
            catch (Fido2Exception ex) when (ex.Message.Contains("PIN", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"PIN verification failed: {ex.Message}", ex);
            }
            catch (Fido2Exception ex)
            {
                throw new InvalidOperationException($"YubiKey registration failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Performs FIDO2 challenge-response authentication with the YubiKey.
        /// Generates a fresh nonce, sends a GetAssertion command, and verifies
        /// the returned signature against the stored public key.
        /// </summary>
        /// <param name="credentialId">The FIDO2 credential ID from registration.</param>
        /// <param name="publicKeyBytes">COSE-encoded public key from registration.</param>
        /// <param name="pin">PIN for YubiKey (required if PIN is set).</param>
        /// <param name="relyingPartyId">Relying party identifier (must match registration).</param>
        /// <returns>Assertion result with verification status.</returns>
        public Fido2AssertionResult Authenticate(
            byte[] credentialId,
            byte[] publicKeyBytes,
            string? pin = null,
            string relyingPartyId = DefaultRelyingPartyId)
        {
            if (credentialId == null || credentialId.Length == 0)
                throw new ArgumentException("Credential ID cannot be empty", nameof(credentialId));
            if (publicKeyBytes == null || publicKeyBytes.Length == 0)
                throw new ArgumentException("Public key cannot be empty", nameof(publicKeyBytes));

            var device = FindFido2Device();

            try
            {
                using var fido2Session = new Fido2Session(device);

                // Verify PIN if required
                VerifyPinIfNeeded(fido2Session, pin, PinUvAuthTokenPermissions.GetAssertion, relyingPartyId);

                // Generate a fresh challenge nonce (32 bytes, per FIDO2 spec)
                var challenge = new byte[32];
                RandomNumberGenerator.Fill(challenge);

                // ClientDataHash = SHA-256(challenge) simulating WebAuthn clientDataJSON
                var clientDataHash = SHA256.HashData(challenge);

                var rp = new RelyingParty(relyingPartyId);
                var gaParams = new GetAssertionParameters(rp, new ReadOnlyMemory<byte>(clientDataHash));

                // Add the credential to the allow list
                var credId = new CredentialId(new ReadOnlyMemory<byte>(credentialId), out _);
                gaParams.AllowCredential(credId);

                // Get the assertion - user must touch YubiKey
                var assertions = fido2Session.GetAssertions(gaParams);

                if (assertions == null || assertions.Count == 0)
                    throw new InvalidOperationException("YubiKey returned no assertions.");

                var assertion = assertions[0];

                // Reconstruct the public key from stored COSE bytes
                var publicKey = CoseKey.Create(new ReadOnlyMemory<byte>(publicKeyBytes), out _);

                // Verify the assertion signature against the public key
                bool isValid = assertion.VerifyAssertion(publicKey, new ReadOnlyMemory<byte>(clientDataHash));

                return new Fido2AssertionResult
                {
                    Signature = assertion.Signature.ToArray(),
                    AuthenticatorData = assertion.AuthenticatorData.EncodedAuthenticatorData.ToArray(),
                    CredentialId = assertion.CredentialId?.Id.ToArray() ?? Array.Empty<byte>(),
                    IsValid = isValid
                };
            }
            catch (TimeoutException)
            {
                throw new TimeoutException("YubiKey authentication timed out. Please touch the device when prompted.");
            }
            catch (Fido2Exception ex) when (ex.Message.Contains("PIN", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"PIN verification failed: {ex.Message}", ex);
            }
            catch (Fido2Exception ex)
            {
                throw new InvalidOperationException($"YubiKey authentication failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Backward-compatible assertion verification for older call sites that pass
        /// the service assertion result type.
        /// </summary>
        public bool VerifyAssertion(Fido2AssertionResult assertion, byte[] challenge, byte[] publicKeyBytes)
        {
            if (challenge == null || challenge.Length == 0)
                throw new ArgumentException("Challenge cannot be null or empty", nameof(challenge));
            if (publicKeyBytes == null || publicKeyBytes.Length == 0)
                throw new ArgumentException("Public key cannot be null or empty", nameof(publicKeyBytes));
            if (assertion == null)
                throw new ArgumentNullException(nameof(assertion));

            // The assertion is already verified in Authenticate().
            return assertion.IsValid;
        }

        /// <summary>
        /// Backward-compatible assertion verification for callers that keep the raw
        /// Yubico SDK assertion object.
        /// </summary>
        public bool VerifyAssertion(GetAssertionData assertion, byte[] challenge, byte[] publicKeyBytes)
        {
            if (challenge == null || challenge.Length == 0)
                throw new ArgumentException("Challenge cannot be null or empty", nameof(challenge));
            if (publicKeyBytes == null || publicKeyBytes.Length == 0)
                throw new ArgumentException("Public key cannot be null or empty", nameof(publicKeyBytes));
            if (assertion == null)
                throw new ArgumentNullException(nameof(assertion));

            try
            {
                var publicKey = CoseKey.Create(new ReadOnlyMemory<byte>(publicKeyBytes), out _);
                var clientDataHash = SHA256.HashData(challenge);
                return assertion.VerifyAssertion(publicKey, new ReadOnlyMemory<byte>(clientDataHash));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Verifies that a YubiKey matches the expected serial number.
        /// </summary>
        public bool VerifyDeviceMatch(int expectedSerial)
        {
            try
            {
                var device = YubiKeyDevice.FindAll().FirstOrDefault();
                return device?.SerialNumber == expectedSerial;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Sets or changes the PIN on a YubiKey FIDO2 application.
        /// </summary>
        /// <param name="newPin">The new PIN to set (must be 4-63 bytes UTF-8).</param>
        /// <param name="currentPin">Current PIN (null if setting PIN for first time).</param>
        public void SetPin(string newPin, string? currentPin = null)
        {
            if (string.IsNullOrEmpty(newPin))
                throw new ArgumentException("PIN cannot be empty", nameof(newPin));

            var newPinBytes = Encoding.UTF8.GetBytes(newPin);
            if (newPinBytes.Length < 4 || newPinBytes.Length > 63)
                throw new ArgumentException("PIN must be 4-63 bytes (UTF-8 encoded)", nameof(newPin));

            var device = FindFido2Device();

            try
            {
                using var fido2Session = new Fido2Session(device);

                if (string.IsNullOrEmpty(currentPin))
                {
                    // Setting PIN for the first time
                    if (!fido2Session.TrySetPin(new ReadOnlyMemory<byte>(newPinBytes)))
                    {
                        throw new InvalidOperationException("Failed to set PIN. The YubiKey may already have a PIN configured.");
                    }
                }
                else
                {
                    // Changing existing PIN
                    var currentPinBytes = Encoding.UTF8.GetBytes(currentPin);
                    if (!fido2Session.TryChangePin(
                        new ReadOnlyMemory<byte>(currentPinBytes),
                        new ReadOnlyMemory<byte>(newPinBytes)))
                    {
                        throw new InvalidOperationException("Failed to change PIN. Current PIN may be incorrect.");
                    }
                    CryptographicOperations.ZeroMemory(currentPinBytes);
                }

                CryptographicOperations.ZeroMemory(newPinBytes);
            }
            catch (Fido2Exception ex)
            {
                throw new InvalidOperationException($"Failed to set PIN: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Gets the number of remaining PIN retry attempts before the YubiKey locks.
        /// </summary>
        /// <returns>Number of remaining attempts, or null if unavailable.</returns>
        public int? GetPinRetries()
        {
            try
            {
                var device = YubiKeyDevice.FindAll().FirstOrDefault();
                if (device == null || !device.HasFeature(YubiKeyFeature.Fido2Application))
                    return null;

                using var connection = device.Connect(YubiKeyApplication.Fido2);
                var cmd = new GetPinRetriesCommand();
                var response = connection.SendCommand(cmd);
                var (retriesRemaining, _) = response.GetData();
                return retriesRemaining;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Checks if the YubiKey has a PIN configured.
        /// </summary>
        public bool? IsPinSet()
        {
            try
            {
                var device = YubiKeyDevice.FindAll().FirstOrDefault();
                if (device == null || !device.HasFeature(YubiKeyFeature.Fido2Application))
                    return null;

                using var fido2Session = new Fido2Session(device);
                var pinOption = fido2Session.AuthenticatorInfo.GetOptionValue(AuthenticatorOptions.clientPin);
                return pinOption == OptionValue.True;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets information about the first detected YubiKey device.
        /// </summary>
        public string? GetDeviceInfo()
        {
            try
            {
                var device = YubiKeyDevice.FindAll().FirstOrDefault();
                if (device == null)
                    return null;

                var capabilities = new List<string>();
                if (device.HasFeature(YubiKeyFeature.Fido2Application))
                    capabilities.Add("FIDO2");
                if (device.HasFeature(YubiKeyFeature.OtpApplication))
                    capabilities.Add("OTP");
                if (device.HasFeature(YubiKeyFeature.OathApplication))
                    capabilities.Add("OATH");
                if (device.HasFeature(YubiKeyFeature.PivApplication))
                    capabilities.Add("PIV");

                return $"YubiKey {device.FirmwareVersion} (Serial: {device.SerialNumber}) " +
                       $"[{string.Join(", ", capabilities)}]";
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Validates that a YubiKey is present with FIDO2 support.
        /// </summary>
        public bool IsConfigured()
        {
            try
            {
                var device = YubiKeyDevice.FindAll().FirstOrDefault();
                if (device == null)
                    return false;
                return device.HasFeature(YubiKeyFeature.Fido2Application);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if the connected YubiKey supports FIDO2 authentication.
        /// </summary>
        public bool SupportsFido2()
        {
            try
            {
                var device = YubiKeyDevice.FindAll().FirstOrDefault();
                return device?.HasFeature(YubiKeyFeature.Fido2Application) ?? false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Finds the first connected YubiKey with FIDO2 support.
        /// Throws if none found.
        /// </summary>
        private static IYubiKeyDevice FindFido2Device()
        {
            var device = YubiKeyDevice.FindAll().FirstOrDefault();
            if (device == null)
                throw new InvalidOperationException("No YubiKey detected. Please insert your YubiKey.");

            if (!device.HasFeature(YubiKeyFeature.Fido2Application))
                throw new InvalidOperationException("YubiKey does not support FIDO2. Firmware 5.0+ required.");

            return device;
        }

        /// <summary>
        /// Verifies the PIN with the FIDO2 session if the YubiKey requires it.
        /// Uses the caller-supplied PIN directly (no KeyCollector delegate needed).
        /// </summary>
        private static void VerifyPinIfNeeded(
            Fido2Session fido2Session,
            string? pin,
            PinUvAuthTokenPermissions permissions,
            string? relyingPartyId)
        {
            var pinOption = fido2Session.AuthenticatorInfo.GetOptionValue(AuthenticatorOptions.clientPin);
            if (pinOption != OptionValue.True)
                return; // No PIN configured, nothing to verify

            if (string.IsNullOrEmpty(pin))
                throw new InvalidOperationException("YubiKey requires a PIN but none was provided.");

            var pinBytes = Encoding.UTF8.GetBytes(pin);
            try
            {
                if (!fido2Session.TryVerifyPin(
                    new ReadOnlyMemory<byte>(pinBytes),
                    permissions,
                    relyingPartyId,
                    out int? retriesRemaining,
                    out bool? rebootRequired))
                {
                    string message = "PIN verification failed.";
                    if (retriesRemaining.HasValue)
                        message += $" {retriesRemaining.Value} attempts remaining.";
                    if (rebootRequired == true)
                        message += " YubiKey must be removed and reinserted.";

                    throw new InvalidOperationException(message);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pinBytes);
            }
        }
    }
}
