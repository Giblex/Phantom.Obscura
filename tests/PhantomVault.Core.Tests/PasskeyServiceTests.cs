using System;
using System.Runtime.Versioning;
using Xunit;
using PhantomVault.Core.Services;

namespace PhantomVault.Core.Tests
{
    /// <summary>
    /// Unit tests for PasskeyService testing input validation and basic behavior.
    /// Note: Full passkey tests require Windows Hello, so these focus on validation logic.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class PasskeyServiceTests
    {
        private readonly PasskeyService _sut;

        public PasskeyServiceTests()
        {
            _sut = new PasskeyService();
        }

        #region Property Tests

        [Fact]
        [SupportedOSPlatform("windows")]
        public void AuthenticatorDescription_ReturnsNonEmptyString()
        {
            var description = _sut.AuthenticatorDescription;

            Assert.NotNull(description);
            Assert.True(description.Length > 0);
            Assert.DoesNotContain("not implemented", description, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public void IsSupported_ReturnsBoolean()
        {
            // IsSupported should return true or false without throwing
            var isSupported = _sut.IsSupported;
            
            // Just verify it doesn't throw - actual value depends on Windows Hello availability
            Assert.True(isSupported || !isSupported);
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public void IsBiometricAvailable_ReturnsBoolean()
        {
            // Should return true or false without throwing
            var isBiometric = _sut.IsBiometricAvailable;
            
            Assert.True(isBiometric || !isBiometric);
        }

        #endregion

        #region RegisterAsync Validation Tests

        [Fact]
        [SupportedOSPlatform("windows")]
        public async void RegisterAsync_NullUserId_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _sut.RegisterAsync(null!, "User Name", "rpId", new byte[] { 1, 2, 3 }));
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public async void RegisterAsync_EmptyUserId_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _sut.RegisterAsync("", "User Name", "rpId", new byte[] { 1, 2, 3 }));
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public async void RegisterAsync_WhitespaceUserId_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _sut.RegisterAsync("   ", "User Name", "rpId", new byte[] { 1, 2, 3 }));
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public async void RegisterAsync_NullUserName_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _sut.RegisterAsync("userId", null!, "rpId", new byte[] { 1, 2, 3 }));
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public async void RegisterAsync_EmptyUserName_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _sut.RegisterAsync("userId", "", "rpId", new byte[] { 1, 2, 3 }));
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public async void RegisterAsync_NullRpId_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _sut.RegisterAsync("userId", "User Name", null!, new byte[] { 1, 2, 3 }));
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public async void RegisterAsync_EmptyRpId_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _sut.RegisterAsync("userId", "User Name", "", new byte[] { 1, 2, 3 }));
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public async void RegisterAsync_NullChallenge_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _sut.RegisterAsync("userId", "User Name", "rpId", null!));
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public async void RegisterAsync_EmptyChallenge_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _sut.RegisterAsync("userId", "User Name", "rpId", Array.Empty<byte>()));
        }

        #endregion

        #region AuthenticateAsync Validation Tests

        [Fact]
        [SupportedOSPlatform("windows")]
        public async void AuthenticateAsync_NullCredentialId_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _sut.AuthenticateAsync(null!, "rpId", new byte[] { 1, 2, 3 }));
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public async void AuthenticateAsync_EmptyCredentialId_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _sut.AuthenticateAsync(Array.Empty<byte>(), "rpId", new byte[] { 1, 2, 3 }));
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public async void AuthenticateAsync_NullRpId_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _sut.AuthenticateAsync(new byte[] { 1, 2, 3 }, null!, new byte[] { 1, 2, 3 }));
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public async void AuthenticateAsync_EmptyRpId_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _sut.AuthenticateAsync(new byte[] { 1, 2, 3 }, "", new byte[] { 1, 2, 3 }));
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public async void AuthenticateAsync_WhitespaceRpId_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _sut.AuthenticateAsync(new byte[] { 1, 2, 3 }, "   ", new byte[] { 1, 2, 3 }));
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public async void AuthenticateAsync_NullChallenge_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _sut.AuthenticateAsync(new byte[] { 1, 2, 3 }, "rpId", null!));
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public async void AuthenticateAsync_EmptyChallenge_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _sut.AuthenticateAsync(new byte[] { 1, 2, 3 }, "rpId", Array.Empty<byte>()));
        }

        #endregion
    }
}
