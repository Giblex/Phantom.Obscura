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

    public sealed class Fido2CredentialResult
    {

        public byte[] CredentialId { get; set; } = Array.Empty<byte>();

        public byte[] PublicKey { get; set; } = Array.Empty<byte>();

        public string AttestationFormat { get; set; } = string.Empty;

        public int SerialNumber { get; set; }
    }

    public sealed class Fido2AssertionResult
    {

        public byte[] Signature { get; set; } = Array.Empty<byte>();

        public byte[] AuthenticatorData { get; set; } = Array.Empty<byte>();

        public byte[] CredentialId { get; set; } = Array.Empty<byte>();

        public bool IsValid { get; set; }
    }

    public sealed partial class YubiKeyService
    {
        private const string DefaultRelyingPartyId = "phantomvault.local";
        private const string RelyingPartyName = "PhantomVault";

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

                VerifyPinIfNeeded(fido2Session, pin, PinUvAuthTokenPermissions.MakeCredential, relyingPartyId);

                var rp = new RelyingParty(relyingPartyId) { Name = RelyingPartyName };
                var userIdBytes = Encoding.UTF8.GetBytes(userId);
                var user = new UserEntity(new ReadOnlyMemory<byte>(userIdBytes))
                {
                    Name = userName,
                    DisplayName = userName
                };

                var challenge = new byte[32];
                RandomNumberGenerator.Fill(challenge);

                var clientDataHash = SHA256.HashData(challenge);

                var mcParams = new MakeCredentialParameters(rp, user)
                {
                    ClientDataHash = new ReadOnlyMemory<byte>(clientDataHash)
                };

                mcParams.AddOption(AuthenticatorOptions.rk, true);

                var mcData = fido2Session.MakeCredential(mcParams);

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

                VerifyPinIfNeeded(fido2Session, pin, PinUvAuthTokenPermissions.GetAssertion, relyingPartyId);

                var challenge = new byte[32];
                RandomNumberGenerator.Fill(challenge);

                var clientDataHash = SHA256.HashData(challenge);

                var rp = new RelyingParty(relyingPartyId);
                var gaParams = new GetAssertionParameters(rp, new ReadOnlyMemory<byte>(clientDataHash));

                var credId = new CredentialId(new ReadOnlyMemory<byte>(credentialId), out _);
                gaParams.AllowCredential(credId);

                var assertions = fido2Session.GetAssertions(gaParams);

                if (assertions == null || assertions.Count == 0)
                    throw new InvalidOperationException("YubiKey returned no assertions.");

                var assertion = assertions[0];

                var publicKey = CoseKey.Create(new ReadOnlyMemory<byte>(publicKeyBytes), out _);

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

        public bool VerifyAssertion(Fido2AssertionResult assertion, byte[] challenge, byte[] publicKeyBytes)
        {
            if (challenge == null || challenge.Length == 0)
                throw new ArgumentException("Challenge cannot be null or empty", nameof(challenge));
            if (publicKeyBytes == null || publicKeyBytes.Length == 0)
                throw new ArgumentException("Public key cannot be null or empty", nameof(publicKeyBytes));
            if (assertion == null)
                throw new ArgumentNullException(nameof(assertion));

            return assertion.IsValid;
        }

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

                    if (!fido2Session.TrySetPin(new ReadOnlyMemory<byte>(newPinBytes)))
                    {
                        throw new InvalidOperationException("Failed to set PIN. The YubiKey may already have a PIN configured.");
                    }
                }
                else
                {

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

        private static IYubiKeyDevice FindFido2Device()
        {
            var device = YubiKeyDevice.FindAll().FirstOrDefault();
            if (device == null)
                throw new InvalidOperationException("No YubiKey detected. Please insert your YubiKey.");

            if (!device.HasFeature(YubiKeyFeature.Fido2Application))
                throw new InvalidOperationException("YubiKey does not support FIDO2. Firmware 5.0+ required.");

            return device;
        }

        private static void VerifyPinIfNeeded(
            Fido2Session fido2Session,
            string? pin,
            PinUvAuthTokenPermissions permissions,
            string? relyingPartyId)
        {
            var pinOption = fido2Session.AuthenticatorInfo.GetOptionValue(AuthenticatorOptions.clientPin);
            if (pinOption != OptionValue.True)
                return;

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

