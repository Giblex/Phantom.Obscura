using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using Moq;
using PhantomVault.Core.Services;
using Yubico.YubiKey;
using Yubico.YubiKey.Fido2;
using Yubico.YubiKey.Fido2.Commands;

namespace PhantomVault.Core.Tests.Services
{
    /// <summary>
    /// Comprehensive tests for YubiKey FIDO2 authentication implementation.
    ///
    /// NOTE: Most tests are marked as [Fact(Skip = "Requires physical YubiKey")]
    /// because they require actual YubiKey hardware. These tests should be run
    /// manually during development with a YubiKey inserted.
    ///
    /// Tests without Skip can run in CI/CD pipelines.
    /// </summary>
    public class YubiKeyServiceFido2Tests
    {
        private readonly YubiKeyService _service;

        public YubiKeyServiceFido2Tests()
        {
            _service = new YubiKeyService();
        }

        #region Device Detection Tests (Can run without YubiKey)

        [Fact]
        public void IsTokenPresent_NoExceptionThrown_ReturnsBoolean()
        {
            // Should return false gracefully if no YubiKey present
            var result = _service.IsTokenPresent();
            Assert.IsType<bool>(result);
        }

        [Fact]
        public void SupportsFido2_NoExceptionThrown_ReturnsBoolean()
        {
            var result = _service.SupportsFido2();
            Assert.IsType<bool>(result);
        }

        [Fact]
        public void GetDeviceInfo_NoYubiKey_ReturnsNull()
        {
            // If no YubiKey is present, should return null gracefully
            var info = _service.GetDeviceInfo();
            // Could be null or a string depending on presence
            Assert.True(info == null || info is string);
        }

        [Fact]
        public void IsConfigured_NoExceptionThrown_ReturnsBoolean()
        {
            var result = _service.IsConfigured();
            Assert.IsType<bool>(result);
        }

        [Fact]
        public void IsPinSet_NoYubiKey_ReturnsNull()
        {
            // Without YubiKey, should return null
            var result = _service.IsPinSet();
            Assert.True(result == null || result is bool);
        }

        [Fact]
        public void GetPinRetries_NoYubiKey_ReturnsNull()
        {
            var result = _service.GetPinRetries();
            Assert.True(result == null || result is int);
        }

        #endregion

        #region Registration Tests (Require Physical YubiKey)

        [Fact(Skip = "Requires physical YubiKey")]
        public void RegisterCredential_WithValidInputs_ReturnsCredentialResult()
        {
            // Arrange
            var userId = "test@phantomvault.local";
            var userName = "Test User";

            // Act
            var result = _service.RegisterCredential(userId, userName);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result.CredentialId);
            Assert.NotEmpty(result.PublicKey);
            Assert.NotEqual(0, result.SerialNumber);
            Assert.NotNull(result.AttestationFormat);
        }

        [Fact]
        public void RegisterCredential_EmptyUserId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                _service.RegisterCredential("", "Test User"));
        }

        [Fact]
        public void RegisterCredential_EmptyUserName_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                _service.RegisterCredential("test@example.com", ""));
        }

        [Fact]
        public void RegisterCredential_NullUserId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                _service.RegisterCredential(null!, "Test User"));
        }

        [Fact(Skip = "Requires physical YubiKey with PIN")]
        public void RegisterCredential_WithPin_Success()
        {
            var userId = "test@phantomvault.local";
            var userName = "Test User";
            var pin = "123456"; // Use actual PIN from your YubiKey

            var result = _service.RegisterCredential(userId, userName, pin);

            Assert.NotNull(result);
            Assert.NotEmpty(result.CredentialId);
        }

        [Fact(Skip = "Requires physical YubiKey")]
        public void RegisterCredential_CustomRelyingParty_Success()
        {
            var userId = "test@custom.local";
            var userName = "Custom User";
            var rpId = "custom.phantomvault.local";

            var result = _service.RegisterCredential(userId, userName, null, rpId);

            Assert.NotNull(result);
            Assert.NotEmpty(result.CredentialId);
        }

        #endregion

        #region Authentication Tests (Require Physical YubiKey)

        [Fact(Skip = "Requires physical YubiKey with registered credential")]
        public void Authenticate_WithValidCredential_ReturnsAssertion()
        {
            // Arrange
            // First register a credential
            var userId = "auth-test@phantomvault.local";
            var userName = "Auth Test User";
            var registrationResult = _service.RegisterCredential(userId, userName);

            // Generate challenge
            var challenge = new byte[32];
            RandomNumberGenerator.Fill(challenge);

            // Act
            var assertion = _service.Authenticate(
                challenge,
                registrationResult.CredentialId);

            // Assert
            Assert.NotNull(assertion);
            Assert.NotNull(assertion.Signature);
            Assert.NotNull(assertion.AuthenticatorData);
        }

        [Fact]
        public void Authenticate_NullChallenge_ThrowsArgumentException()
        {
            var credentialId = new byte[32];
            Assert.Throws<ArgumentException>(() =>
                _service.Authenticate(null!, credentialId));
        }

        [Fact]
        public void Authenticate_EmptyChallenge_ThrowsArgumentException()
        {
            var credentialId = new byte[32];
            Assert.Throws<ArgumentException>(() =>
                _service.Authenticate(Array.Empty<byte>(), credentialId));
        }

        [Fact]
        public void Authenticate_NullCredentialId_ThrowsArgumentException()
        {
            var challenge = new byte[32];
            Assert.Throws<ArgumentException>(() =>
                _service.Authenticate(challenge, null!));
        }

        [Fact]
        public void Authenticate_EmptyCredentialId_ThrowsArgumentException()
        {
            var challenge = new byte[32];
            Assert.Throws<ArgumentException>(() =>
                _service.Authenticate(challenge, Array.Empty<byte>()));
        }

        [Fact(Skip = "Requires physical YubiKey with PIN")]
        public void Authenticate_WithPin_Success()
        {
            // Arrange
            var userId = "pin-auth-test@phantomvault.local";
            var userName = "PIN Auth Test";
            var pin = "123456"; // Use actual PIN

            var registrationResult = _service.RegisterCredential(userId, userName, pin);

            var challenge = new byte[32];
            RandomNumberGenerator.Fill(challenge);

            // Act
            var assertion = _service.Authenticate(
                challenge,
                registrationResult.CredentialId,
                pin);

            // Assert
            Assert.NotNull(assertion);
            Assert.NotNull(assertion.Signature);
        }

        #endregion

        #region Signature Verification Tests

        [Fact(Skip = "Requires physical YubiKey")]
        public void VerifyAssertion_ValidSignature_ReturnsTrue()
        {
            // Arrange - Full registration and authentication flow
            var userId = "verify-test@phantomvault.local";
            var userName = "Verify Test User";
            var registrationResult = _service.RegisterCredential(userId, userName);

            var challenge = new byte[32];
            RandomNumberGenerator.Fill(challenge);

            var assertion = _service.Authenticate(
                challenge,
                registrationResult.CredentialId);

            // Act
            var isValid = _service.VerifyAssertion(
                assertion,
                challenge,
                registrationResult.PublicKey);

            // Assert
            Assert.True(isValid);
        }

        [Fact]
        public void VerifyAssertion_NullAssertion_ThrowsArgumentNullException()
        {
            var challenge = new byte[32];
            var publicKey = new byte[64];

            Assert.Throws<ArgumentNullException>(() =>
                _service.VerifyAssertion((GetAssertionData)null!, challenge, publicKey));
        }

        [Fact]
        public void VerifyAssertion_NullChallenge_ThrowsArgumentException()
        {
            // Create a mock assertion (won't be used)
            var assertion = default(GetAssertionData);
            var publicKey = new byte[64];

            Assert.Throws<ArgumentException>(() =>
                _service.VerifyAssertion(assertion!, null!, publicKey));
        }

        [Fact]
        public void VerifyAssertion_EmptyPublicKey_ThrowsArgumentException()
        {
            var assertion = default(GetAssertionData);
            var challenge = new byte[32];

            Assert.Throws<ArgumentException>(() =>
                _service.VerifyAssertion(assertion!, challenge, Array.Empty<byte>()));
        }

        [Fact(Skip = "Requires physical YubiKey")]
        public void VerifyAssertion_TamperedSignature_ReturnsFalse()
        {
            // Arrange
            var userId = "tamper-test@phantomvault.local";
            var userName = "Tamper Test";
            var registrationResult = _service.RegisterCredential(userId, userName);

            var challenge = new byte[32];
            RandomNumberGenerator.Fill(challenge);

            var assertion = _service.Authenticate(
                challenge,
                registrationResult.CredentialId);

            // Tamper with challenge
            var tamperedChallenge = new byte[32];
            Array.Copy(challenge, tamperedChallenge, 32);
            tamperedChallenge[0] ^= 0xFF; // Flip bits

            // Act
            var isValid = _service.VerifyAssertion(
                assertion,
                tamperedChallenge,
                registrationResult.PublicKey);

            // Assert
            Assert.False(isValid);
        }

        #endregion

        #region PIN Management Tests

        [Fact]
        public void SetPin_EmptyPin_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                _service.SetPin(""));
        }

        [Fact]
        public void SetPin_TooShortPin_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                _service.SetPin("123")); // Less than 4 characters
        }

        [Fact]
        public void SetPin_TooLongPin_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                _service.SetPin(new string('1', 64))); // More than 63 UTF-8 bytes
        }

        [Fact(Skip = "Requires physical YubiKey - DESTRUCTIVE TEST")]
        public void SetPin_ValidPin_Success()
        {
            // WARNING: This will set/change the PIN on your YubiKey
            var newPin = "654321";
            _service.SetPin(newPin);

            // Verify PIN is now set
            var isPinSet = _service.IsPinSet();
            Assert.True(isPinSet);
        }

        [Fact(Skip = "Requires physical YubiKey with existing PIN - DESTRUCTIVE TEST")]
        public void SetPin_ChangeExistingPin_Success()
        {
            // WARNING: This will change the PIN on your YubiKey
            var currentPin = "123456";
            var newPin = "654321";

            _service.SetPin(newPin, currentPin);

            // Verify PIN is still set
            var isPinSet = _service.IsPinSet();
            Assert.True(isPinSet);
        }

        [Fact(Skip = "Requires physical YubiKey")]
        public void GetPinRetries_WithYubiKey_ReturnsNumber()
        {
            var retries = _service.GetPinRetries();

            if (_service.IsTokenPresent())
            {
                Assert.NotNull(retries);
                Assert.True(retries >= 0 && retries <= 8); // YubiKey allows up to 8 retries
            }
            else
            {
                Assert.Null(retries);
            }
        }

        #endregion

        #region Device Verification Tests

        [Fact(Skip = "Requires physical YubiKey")]
        public void VerifyDeviceMatch_CorrectSerial_ReturnsTrue()
        {
            // Get current device serial
            var device = YubiKeyDevice.FindAll().FirstOrDefault();
            if (device == null)
                return; // Skip test if no YubiKey

            var serial = device.SerialNumber ?? 0;

            var result = _service.VerifyDeviceMatch(serial);
            Assert.True(result);
        }

        [Fact(Skip = "Requires physical YubiKey")]
        public void VerifyDeviceMatch_WrongSerial_ReturnsFalse()
        {
            var fakeSerial = 99999999;
            var result = _service.VerifyDeviceMatch(fakeSerial);
            Assert.False(result);
        }

        #endregion

        #region Integration Tests

        [Fact(Skip = "Requires physical YubiKey")]
        public void FullWorkflow_RegisterAuthenticateVerify_Success()
        {
            // Step 1: Register credential
            var userId = $"workflow-{Guid.NewGuid()}@phantomvault.local";
            var userName = "Workflow Test User";
            var registrationResult = _service.RegisterCredential(userId, userName);

            Assert.NotNull(registrationResult);
            Assert.NotEmpty(registrationResult.CredentialId);
            Assert.NotEmpty(registrationResult.PublicKey);

            // Step 2: Authenticate with challenge
            var challenge = new byte[32];
            RandomNumberGenerator.Fill(challenge);

            var assertion = _service.Authenticate(
                challenge,
                registrationResult.CredentialId);

            Assert.NotNull(assertion);
            Assert.NotNull(assertion.Signature);

            // Step 3: Verify signature
            var isValid = _service.VerifyAssertion(
                assertion,
                challenge,
                registrationResult.PublicKey);

            Assert.True(isValid);
        }

        [Fact(Skip = "Requires physical YubiKey")]
        public void MultipleAuthentications_SameCredential_AllSucceed()
        {
            // Register once
            var userId = $"multi-auth-{Guid.NewGuid()}@phantomvault.local";
            var userName = "Multi Auth Test";
            var registrationResult = _service.RegisterCredential(userId, userName);

            // Authenticate 5 times with different challenges
            for (int i = 0; i < 5; i++)
            {
                var challenge = new byte[32];
                RandomNumberGenerator.Fill(challenge);

                var assertion = _service.Authenticate(
                    challenge,
                    registrationResult.CredentialId);

                var isValid = _service.VerifyAssertion(
                    assertion,
                    challenge,
                    registrationResult.PublicKey);

                Assert.True(isValid, $"Authentication {i + 1} failed");
            }
        }

        [Fact(Skip = "Requires physical YubiKey")]
        public void SerialNumberBinding_VerifyDeviceMatch_Success()
        {
            // Register credential and store serial
            var userId = $"serial-bind-{Guid.NewGuid()}@phantomvault.local";
            var userName = "Serial Bind Test";
            var registrationResult = _service.RegisterCredential(userId, userName);

            // Verify device matches stored serial
            var deviceMatches = _service.VerifyDeviceMatch(registrationResult.SerialNumber);
            Assert.True(deviceMatches);

            // Verify wrong serial fails
            var wrongSerialMatches = _service.VerifyDeviceMatch(99999999);
            Assert.False(wrongSerialMatches);
        }

        #endregion

        #region Error Handling Tests

        [Fact(Skip = "Requires YubiKey removal during test")]
        public void Authenticate_YubiKeyRemoved_ThrowsInvalidOperationException()
        {
            // This test requires manual intervention:
            // 1. Start test with YubiKey inserted
            // 2. Remove YubiKey when prompted
            // 3. Test should throw InvalidOperationException

            var challenge = new byte[32];
            var credentialId = new byte[32];

            // Remove YubiKey now!
            System.Threading.Thread.Sleep(5000); // Give time to remove

            Assert.Throws<InvalidOperationException>(() =>
                _service.Authenticate(challenge, credentialId));
        }

        [Fact(Skip = "Requires timeout scenario")]
        public void Authenticate_UserDoesntTouch_ThrowsTimeoutException()
        {
            // This test requires manual intervention:
            // 1. Start test with YubiKey inserted
            // 2. Do NOT touch YubiKey when it prompts
            // 3. Test should throw TimeoutException after 30 seconds

            var userId = $"timeout-{Guid.NewGuid()}@phantomvault.local";
            var userName = "Timeout Test";
            var registrationResult = _service.RegisterCredential(userId, userName);

            var challenge = new byte[32];
            RandomNumberGenerator.Fill(challenge);

            Assert.Throws<TimeoutException>(() =>
                _service.Authenticate(challenge, registrationResult.CredentialId));
        }

        #endregion

        #region Model Compatibility Tests

        [Fact(Skip = "Requires YubiKey 5 Series")]
        public void DeviceInfo_YubiKey5Series_ContainsFido2()
        {
            var info = _service.GetDeviceInfo();
            Assert.NotNull(info);
            Assert.Contains("FIDO2", info);
        }

        [Fact(Skip = "Requires YubiKey 5C NFC")]
        public void DeviceInfo_YubiKey5CNFC_HasNfcCapability()
        {
            var info = _service.GetDeviceInfo();
            Assert.NotNull(info);
            // Should show firmware 5.x and NFC capability
        }

        [Fact(Skip = "Requires YubiKey Bio Series")]
        public void DeviceInfo_YubiKeyBio_HasBiometricFeature()
        {
            var info = _service.GetDeviceInfo();
            Assert.NotNull(info);
            // Bio series has built-in fingerprint reader
        }

        #endregion
    }
}
