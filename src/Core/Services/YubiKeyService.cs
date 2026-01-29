using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Yubico.YubiKey;
using Yubico.YubiKey.Fido2;
using Yubico.YubiKey.Fido2.Commands;

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
    /// Service for YubiKey hardware token integration.
    /// Provides hardware-based authentication using YubiKey's FIDO2 capabilities.
    /// 
    /// <para>
    /// <b>Requirements:</b>
    /// - YubiKey firmware 5.0 or later for FIDO2 support
    /// - Yubico.YubiKey SDK (already included as NuGet package)
    /// - User must touch the YubiKey to complete authentication
    /// </para>
    /// 
    /// <para>
    /// <b>Note:</b> This is a foundational implementation. Full FIDO2 credential
    /// management requires the Yubico.YubiKey.Fido2 namespace which is available
    /// but not yet fully integrated. For now, this service provides device detection
    /// and presence validation, with FIDO2 authentication marked for future enhancement.
    /// </para>
    /// </summary>
    public sealed class YubiKeyService
    {
        /// <summary>
        /// Determines whether a YubiKey hardware token is currently connected.
        /// Enumerates all connected YubiKey devices via USB/NFC.
        /// </summary>
        /// <returns>True if at least one YubiKey is detected; false otherwise.</returns>
        /// <remarks>
        /// This method is safe to call even if no YubiKey is present or the SDK
        /// encounters driver issues. It returns false rather than throwing exceptions
        /// to enable graceful degradation in UI workflows.
        /// </remarks>
        public bool IsTokenPresent()
        {
            try
            {
                // Enumerate all connected YubiKey devices
                var devices = YubiKeyDevice.FindAll().ToList();
                return devices.Count > 0;
            }
            catch (Exception ex) when (
                ex is PlatformNotSupportedException ||
                ex is System.IO.IOException ||
                ex is UnauthorizedAccessException)
            {
                // Expected exceptions:
                // - PlatformNotSupportedException: YubiKey SDK not supported on this OS
                // - IOException: USB communication failure
                // - UnauthorizedAccessException: Insufficient permissions to access USB devices

                // Return false to indicate no token is available (graceful degradation)
                return false;
            }
        }

        /// <summary>
        /// Performs authentication with the YubiKey hardware token.
        /// 
        /// <para>
        /// <b>Current Implementation:</b> This is a placeholder that verifies YubiKey
        /// presence. Full FIDO2 challenge-response authentication requires additional
        /// implementation using the Yubico.YubiKey.Fido2 namespace.
        /// </para>
        /// 
        /// <para>
        /// <b>Future Enhancement:</b> Will implement proper FIDO2 assertion verification
        /// following the pattern:
        /// 1. Load credential ID from manifest
        /// 2. Generate cryptographic challenge
        /// 3. Send GetAssertion command to YubiKey
        /// 4. Verify signature against stored public key
        /// </para>
        /// </summary>
        /// <param name="challenge">
        /// A cryptographic challenge (typically 32 bytes). If null or empty,
        /// a random challenge will be generated.
        /// </param>
        /// <returns>
        /// A signed response from the YubiKey. Currently returns the SHA256 hash
        /// of the challenge as a placeholder. Will return actual FIDO2 assertion
        /// signature in future implementation.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no YubiKey is present or device communication fails.
        /// </exception>
        /// <remarks>
        /// <para>
        /// <b>Implementation Roadmap:</b>
        /// - Phase 1 (Current): Device detection and presence validation
        /// - Phase 2 (Planned): FIDO2 credential registration during vault creation
        /// - Phase 3 (Planned): FIDO2 assertion verification during vault unlock
        /// - Phase 4 (Planned): PIN management and timeout handling
        /// </para>
        /// 
        /// <para>
        /// For full FIDO2 implementation, see:
        /// https://docs.yubico.com/yesdk/users-manual/sdk-programming-guide/fido2.html
        /// </para>
        /// </remarks>
        /// <summary>
        /// Performs authentication with the YubiKey hardware token using FIDO2.
        /// This creates a cryptographic challenge and requires physical touch confirmation.
        /// </summary>
        /// <param name="challenge">Cryptographic challenge (32 bytes recommended).</param>
        /// <param name="credentialId">The FIDO2 credential ID from registration.</param>
        /// <param name="pin">Optional PIN for YubiKey (null to prompt user or if PIN not set).</param>
        /// <param name="relyingPartyId">Relying party identifier (default: phantomvault.local).</param>
        /// <returns>FIDO2 assertion containing signature and authenticator data.</returns>
        /// <exception cref="ArgumentException">If challenge or credentialId is null/empty.</exception>
        /// <exception cref="InvalidOperationException">If no YubiKey is present or FIDO2 is not supported.</exception>
        /// <exception cref="TimeoutException">If user doesn't touch YubiKey within timeout period.</exception>
        public GetAssertionData Authenticate(
            byte[] challenge,
            byte[] credentialId,
            string? pin = null,
            string relyingPartyId = "phantomvault.local")
        {
            if (challenge == null || challenge.Length == 0)
                throw new ArgumentException("Challenge cannot be empty", nameof(challenge));
            if (credentialId == null || credentialId.Length == 0)
                throw new ArgumentException("Credential ID cannot be empty", nameof(credentialId));

            // Step 1: Find YubiKey device
            var device = YubiKeyDevice.FindAll().FirstOrDefault();
            if (device == null)
                throw new InvalidOperationException("No YubiKey detected. Please insert your YubiKey.");

            if (!device.HasFeature(YubiKeyFeature.Fido2Application))
                throw new InvalidOperationException("YubiKey does not support FIDO2.");

            try
            {
                // Step 2: Open FIDO2 session
                using (var fido2Session = new Fido2Session(device))
                {
                    // Step 3: Verify PIN if required
                    if (fido2Session.AuthenticatorInfo.GetOptionValue(AuthenticatorOptions.clientPin) == OptionValue.True)
                    {
                        if (string.IsNullOrEmpty(pin))
                            throw new InvalidOperationException("YubiKey requires PIN for authentication.");

                        // TODO: API changed - VerifyPin signature updated
                        // fido2Session.VerifyPin(Encoding.UTF8.GetBytes(pin));
                    }

                    // TODO: YubiKey SDK API changed - GetAssertionParameters requires updated implementation
                    // This is a stub implementation until the SDK is properly integrated
                    throw new NotImplementedException("YubiKey FIDO2 authentication requires SDK update. This feature is temporarily disabled.");
                }
            }
            catch (TimeoutException)
            {
                throw new TimeoutException("YubiKey authentication timed out. Please touch the device when prompted.");
            }
            catch (Exception ex) when (ex.Message.Contains("PIN"))
            {
                throw new InvalidOperationException($"PIN verification failed: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"YubiKey authentication failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Registers a new FIDO2 credential on the YubiKey for vault authentication.
        /// This method creates a resident credential that can be used for passwordless authentication.
        /// </summary>
        /// <param name="userId">User identifier (must be unique).</param>
        /// <param name="userName">Display name for the user.</param>
        /// <param name="pin">Optional PIN for YubiKey (null if PIN not set).</param>
        /// <param name="relyingPartyId">Relying party identifier (default: phantomvault.local).</param>
        /// <returns>Credential result containing credential ID, public key, and serial number.</returns>
        /// <exception cref="ArgumentException">If userId or userName is null/empty.</exception>
        /// <exception cref="InvalidOperationException">If no YubiKey is present or FIDO2 is not supported.</exception>
        /// <exception cref="TimeoutException">If user doesn't touch YubiKey within timeout period.</exception>
        public Fido2CredentialResult RegisterCredential(
            string userId,
            string userName,
            string? pin = null,
            string relyingPartyId = "phantomvault.local")
        {
            if (string.IsNullOrEmpty(userId))
                throw new ArgumentException("User ID cannot be empty", nameof(userId));
            if (string.IsNullOrEmpty(userName))
                throw new ArgumentException("User name cannot be empty", nameof(userName));

            // Step 1: Find YubiKey device
            var device = YubiKeyDevice.FindAll().FirstOrDefault();
            if (device == null)
                throw new InvalidOperationException("No YubiKey detected. Please insert your YubiKey.");

            if (!device.HasFeature(YubiKeyFeature.Fido2Application))
                throw new InvalidOperationException("YubiKey does not support FIDO2.");

            var serial = device.SerialNumber ?? 0;

            try
            {
                // Step 2: Open FIDO2 session
                using (var fido2Session = new Fido2Session(device))
                {
                    // Step 3: Verify PIN if required for credential management
                    if (fido2Session.AuthenticatorInfo.GetOptionValue(AuthenticatorOptions.clientPin) == OptionValue.True)
                    {
                        if (string.IsNullOrEmpty(pin))
                            throw new InvalidOperationException("YubiKey requires PIN for credential registration.");

                        // TODO: API changed - VerifyPin signature updated
                        // fido2Session.VerifyPin(Encoding.UTF8.GetBytes(pin));
                    }

                    // TODO: YubiKey SDK API changed - MakeCredential parameters and response types require updated implementation
                    // This is a stub implementation until the SDK is properly integrated
                    throw new NotImplementedException("YubiKey FIDO2 credential registration requires SDK update. This feature is temporarily disabled.");
                }
            }
            catch (TimeoutException)
            {
                throw new TimeoutException("YubiKey registration timed out. Please touch the device when prompted.");
            }
            catch (Exception ex) when (ex.Message.Contains("PIN"))
            {
                throw new InvalidOperationException($"PIN verification failed: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"YubiKey registration failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Verifies a FIDO2 assertion signature against the stored public key.
        /// </summary>
        /// <param name="assertion">The assertion returned from Authenticate().</param>
        /// <param name="challenge">The original challenge used for authentication.</param>
        /// <param name="publicKeyBytes">COSE-encoded public key from registration.</param>
        /// <param name="relyingPartyId">Relying party ID (must match registration).</param>
        /// <returns>True if signature is valid; false otherwise.</returns>
        public bool VerifyAssertion(
            GetAssertionData assertion,
            byte[] challenge,
            byte[] publicKeyBytes,
            string relyingPartyId = "phantomvault.local")
        {
            if (assertion == null)
                throw new ArgumentNullException(nameof(assertion));
            if (challenge == null || challenge.Length == 0)
                throw new ArgumentException("Challenge cannot be empty", nameof(challenge));
            if (publicKeyBytes == null || publicKeyBytes.Length == 0)
                throw new ArgumentException("Public key cannot be empty", nameof(publicKeyBytes));

            try
            {
                // TODO: YubiKey SDK API changed - CoseKey and signature verification require updated implementation
                // This is a stub implementation until the SDK is properly integrated
                throw new NotImplementedException("YubiKey signature verification requires SDK update. This feature is temporarily disabled.");
            }
            catch (Exception ex)
            {
                // Log exception in production
                System.Diagnostics.Debug.WriteLine($"Signature verification failed: {ex.Message}");
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
        /// <param name="newPin">The new PIN to set (must be 4-8 characters for most YubiKeys).</param>
        /// <param name="currentPin">Current PIN (null if setting PIN for first time).</param>
        /// <exception cref="ArgumentException">If new PIN is invalid.</exception>
        /// <exception cref="InvalidOperationException">If no YubiKey is present or operation fails.</exception>
        public void SetPin(string newPin, string? currentPin = null)
        {
            if (string.IsNullOrEmpty(newPin))
                throw new ArgumentException("PIN cannot be empty", nameof(newPin));

            if (newPin.Length < 4 || newPin.Length > 8)
                throw new ArgumentException("PIN must be 4-8 characters", nameof(newPin));

            var device = YubiKeyDevice.FindAll().FirstOrDefault();
            if (device == null)
                throw new InvalidOperationException("No YubiKey detected.");

            if (!device.HasFeature(YubiKeyFeature.Fido2Application))
                throw new InvalidOperationException("YubiKey does not support FIDO2.");

            try
            {
                using (var fido2Session = new Fido2Session(device))
                {
                    // TODO: YubiKey SDK API changed - SetPin and ChangePin method signatures require updated implementation
                    // This is a stub implementation until the SDK is properly integrated
                    throw new NotImplementedException("YubiKey PIN management requires SDK update. This feature is temporarily disabled.");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to set PIN: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Gets the number of remaining PIN retry attempts before the YubiKey locks.
        /// </summary>
        /// <returns>Number of remaining attempts, or null if no YubiKey is present.</returns>
        public int? GetPinRetries()
        {
            try
            {
                var device = YubiKeyDevice.FindAll().FirstOrDefault();
                if (device == null)
                    return null;

                if (!device.HasFeature(YubiKeyFeature.Fido2Application))
                    return null;

                using (var fido2Session = new Fido2Session(device))
                {
                    // TODO: YubiKey SDK API changed - RemainingPinRetries property requires updated implementation
                    // Return null to indicate feature unavailable
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Checks if the YubiKey has a PIN configured.
        /// </summary>
        /// <returns>True if PIN is set; false otherwise; null if no YubiKey present.</returns>
        public bool? IsPinSet()
        {
            try
            {
                var device = YubiKeyDevice.FindAll().FirstOrDefault();
                if (device == null)
                    return null;

                if (!device.HasFeature(YubiKeyFeature.Fido2Application))
                    return null;

                using (var fido2Session = new Fido2Session(device))
                {
                    var pinOption = fido2Session.AuthenticatorInfo.GetOptionValue(AuthenticatorOptions.clientPin);
                    return pinOption == OptionValue.True;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets information about the first detected YubiKey device.
        /// Useful for displaying device details in the UI.
        /// </summary>
        /// <returns>
        /// A string containing device information (serial number, firmware version, capabilities),
        /// or null if no YubiKey is detected.
        /// </returns>
        public string? GetDeviceInfo()
        {
            try
            {
                var device = YubiKeyDevice.FindAll().FirstOrDefault();
                if (device == null)
                    return null;

                var capabilities = new System.Collections.Generic.List<string>();
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
                // Ignore exceptions during device enumeration
                return null;
            }
        }

        /// <summary>
        /// Validates that a YubiKey is properly configured and accessible.
        /// Checks for device presence and FIDO2 support.
        /// </summary>
        /// <returns>
        /// True if a YubiKey is present with FIDO2 support; false otherwise.
        /// </returns>
        public bool IsConfigured()
        {
            try
            {
                var device = YubiKeyDevice.FindAll().FirstOrDefault();
                if (device == null)
                    return false;

                // For now, just check FIDO2 capability
                // Future: Check for registered credentials in manifest
                return device.HasFeature(YubiKeyFeature.Fido2Application);
            }
            catch
            {
                // Any exception means the device is not properly configured
                return false;
            }
        }

        /// <summary>
        /// Checks if the connected YubiKey supports FIDO2 authentication.
        /// </summary>
        /// <returns>True if FIDO2 is supported; false otherwise.</returns>
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
    }
}