using System;
using Xunit;
using PhantomVault.Core.Services;

namespace PhantomVault.Core.Tests
{
    /// <summary>
    /// Unit tests for TotpService verifying RFC 6238 compliance and correct OTP generation.
    /// </summary>
    public class TotpServiceTests
    {
        private readonly TotpService _sut = new TotpService();

        #region GenerateSecret Tests

        [Fact]
        public void GenerateSecret_ReturnsBase32String()
        {
            var secret = TotpService.GenerateSecret();

            Assert.NotNull(secret);
            Assert.True(secret.Length > 0);
            // Base32 characters only (uppercase letters A-Z and digits 2-7)
            Assert.Matches(@"^[A-Z2-7]+$", secret);
        }

        [Fact]
        public void GenerateSecret_DefaultLength_Returns16Bytes()
        {
            var secret = TotpService.GenerateSecret();

            // 16 bytes = 128 bits -> ceiling(128/5) * 8 = 26 * 8 / 8 = ~26 base32 chars
            // Base32 encodes 5 bits per character, 16 bytes = 128 bits
            // 128 / 5 = 25.6, so we need 26 characters (ceiling)
            Assert.True(secret.Length >= 25 && secret.Length <= 32);
        }

        [Fact]
        public void GenerateSecret_DifferentEachCall()
        {
            var secret1 = TotpService.GenerateSecret();
            var secret2 = TotpService.GenerateSecret();

            Assert.NotEqual(secret1, secret2);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void GenerateSecret_InvalidLength_Throws(int length)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TotpService.GenerateSecret(length));
        }

        #endregion

        #region GenerateCode Tests

        [Fact]
        public void GenerateCode_NullSecret_Throws()
        {
            Assert.Throws<ArgumentException>(() => _sut.GenerateCode(null!));
        }

        [Fact]
        public void GenerateCode_EmptySecret_Throws()
        {
            Assert.Throws<ArgumentException>(() => _sut.GenerateCode(""));
        }

        [Fact]
        public void GenerateCode_WhitespaceSecret_Throws()
        {
            Assert.Throws<ArgumentException>(() => _sut.GenerateCode("   "));
        }

        [Fact]
        public void GenerateCode_Returns6DigitsByDefault()
        {
            var secret = TotpService.GenerateSecret();
            var code = _sut.GenerateCode(secret);

            Assert.NotNull(code);
            Assert.Equal(6, code.Length);
            Assert.Matches(@"^\d{6}$", code);
        }

        [Theory]
        [InlineData(4)]
        [InlineData(6)]
        [InlineData(8)]
        public void GenerateCode_VariableDigits_ReturnsCorrectLength(int digits)
        {
            var secret = TotpService.GenerateSecret();
            var code = _sut.GenerateCode(secret, digits: digits);

            Assert.Equal(digits, code.Length);
            Assert.Matches($@"^\d{{{digits}}}$", code);
        }

        [Fact]
        public void GenerateCode_SameSecretAndTime_ReturnsSameCode()
        {
            var secret = TotpService.GenerateSecret();
            var timestamp = DateTimeOffset.UtcNow;

            var code1 = _sut.GenerateCode(secret, timestamp);
            var code2 = _sut.GenerateCode(secret, timestamp);

            Assert.Equal(code1, code2);
        }

        [Fact]
        public void GenerateCode_SameSecretDifferentTimeWindow_ReturnsDifferentCode()
        {
            var secret = TotpService.GenerateSecret();
            var now = DateTimeOffset.UtcNow;
            var future = now.AddSeconds(60); // Two time steps later

            var code1 = _sut.GenerateCode(secret, now);
            var code2 = _sut.GenerateCode(secret, future);

            // Different time windows should produce different codes
            // (with very high probability)
            Assert.NotEqual(code1, code2);
        }

        [Fact]
        public void GenerateCode_LeadingZerosPreserved()
        {
            // Use a known timestamp/secret that produces a code with leading zeros
            // We'll test that the length is always 6 even if the number is < 100000
            var secret = TotpService.GenerateSecret();
            
            // Generate many codes, all should be exactly 6 digits
            for (int i = 0; i < 100; i++)
            {
                var timestamp = DateTimeOffset.FromUnixTimeSeconds(i * 30);
                var code = _sut.GenerateCode(secret, timestamp);
                Assert.Equal(6, code.Length);
            }
        }

        #endregion

        #region RFC 6238 Test Vectors

        /// <summary>
        /// Test vectors from RFC 6238 Appendix B using SHA1.
        /// Note: The RFC uses a specific test secret in ASCII hex.
        /// </summary>
        [Theory]
        [InlineData(59L, "94287082")]         // Time step 1
        [InlineData(1111111109L, "07081804")] // Time step 37037037
        [InlineData(1111111111L, "14050471")] // Time step 37037037
        [InlineData(1234567890L, "89005924")] // Time step 41152263
        [InlineData(2000000000L, "69279037")] // Time step 66666666
        [InlineData(20000000000L, "65353130")] // Time step 666666666
        public void GenerateCode_RFC6238TestVectors_SHA1(long unixTime, string expectedCode)
        {
            // RFC 6238 test secret: "12345678901234567890" in ASCII
            // Hex: 3132333435363738393031323334353637383930
            // We need to encode this as Base32 for our service
            // Direct ASCII -> Base32: "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"
            string base32Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
            var timestamp = DateTimeOffset.FromUnixTimeSeconds(unixTime);

            var code = _sut.GenerateCode(base32Secret, TotpAlgorithm.SHA1, timestamp, digits: 8, timeStepSeconds: 30);

            Assert.Equal(expectedCode, code);
        }

        #endregion

        #region Algorithm Tests

        [Fact]
        public void GenerateCode_SHA256_ProducesDifferentCodeThanSHA1()
        {
            var secret = TotpService.GenerateSecret(32); // Longer secret for SHA256
            var timestamp = DateTimeOffset.UtcNow;

            var sha1Code = _sut.GenerateCode(secret, TotpAlgorithm.SHA1, timestamp);
            var sha256Code = _sut.GenerateCode(secret, TotpAlgorithm.SHA256, timestamp);

            Assert.NotEqual(sha1Code, sha256Code);
        }

        [Fact]
        public void GenerateCode_SHA512_ProducesDifferentCodeThanSHA1()
        {
            var secret = TotpService.GenerateSecret(64); // Longer secret for SHA512
            var timestamp = DateTimeOffset.UtcNow;

            var sha1Code = _sut.GenerateCode(secret, TotpAlgorithm.SHA1, timestamp);
            var sha512Code = _sut.GenerateCode(secret, TotpAlgorithm.SHA512, timestamp);

            Assert.NotEqual(sha1Code, sha512Code);
        }

        [Fact]
        public void GenerateCode_SHA256_SameTimestampSameCode()
        {
            var secret = TotpService.GenerateSecret(32);
            var timestamp = DateTimeOffset.UtcNow;

            var code1 = _sut.GenerateCode(secret, TotpAlgorithm.SHA256, timestamp);
            var code2 = _sut.GenerateCode(secret, TotpAlgorithm.SHA256, timestamp);

            Assert.Equal(code1, code2);
        }

        #endregion

        #region Time Step Tests

        [Fact]
        public void GenerateCode_CustomTimeStep_CodesChangeLessFrequently()
        {
            var secret = TotpService.GenerateSecret();
            
            // Use a timestamp at the start of a time window to ensure we're well within the window
            // Unix time 0 is at the start of epoch, and 0/60 = 0, 30/60 = 0 (same window)
            var startOfWindow = DateTimeOffset.FromUnixTimeSeconds(0);

            // With 60s time step, codes 30s apart should be the same (both in window 0)
            var code1 = _sut.GenerateCode(secret, timestamp: startOfWindow, timeStepSeconds: 60);
            var code2 = _sut.GenerateCode(secret, timestamp: startOfWindow.AddSeconds(30), timeStepSeconds: 60);

            Assert.Equal(code1, code2);
        }

        #endregion

        #region Base32 Edge Cases

        [Fact]
        public void GenerateCode_SecretWithSpaces_HandledCorrectly()
        {
            var secretNoSpaces = "JBSWY3DPEHPK3PXP";
            var secretWithSpaces = "JBSW Y3DP EHPK 3PXP";
            var timestamp = DateTimeOffset.UtcNow;

            var code1 = _sut.GenerateCode(secretNoSpaces, timestamp);
            var code2 = _sut.GenerateCode(secretWithSpaces, timestamp);

            Assert.Equal(code1, code2);
        }

        [Fact]
        public void GenerateCode_SecretWithPadding_HandledCorrectly()
        {
            var secretNoPadding = "JBSWY3DPEHPK3PXP";
            var secretWithPadding = "JBSWY3DPEHPK3PXP====";
            var timestamp = DateTimeOffset.UtcNow;

            var code1 = _sut.GenerateCode(secretNoPadding, timestamp);
            var code2 = _sut.GenerateCode(secretWithPadding, timestamp);

            Assert.Equal(code1, code2);
        }

        [Fact]
        public void GenerateCode_LowercaseSecret_HandledCorrectly()
        {
            var secretUpper = "JBSWY3DPEHPK3PXP";
            var secretLower = "jbswy3dpehpk3pxp";
            var timestamp = DateTimeOffset.UtcNow;

            var code1 = _sut.GenerateCode(secretUpper, timestamp);
            var code2 = _sut.GenerateCode(secretLower, timestamp);

            Assert.Equal(code1, code2);
        }

        [Fact]
        public void GenerateCode_InvalidBase32Character_Throws()
        {
            Assert.Throws<FormatException>(() => _sut.GenerateCode("INVALID_CHAR_1"));
        }

        #endregion
    }
}
