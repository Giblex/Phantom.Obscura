using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Yubico.YubiKey;
using Yubico.YubiKey.Oath;
using OathHashAlgorithm = Yubico.YubiKey.Oath.HashAlgorithm;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Lightweight description of an OATH credential stored on a YubiKey.
    /// Mirrors the bits of <see cref="Yubico.YubiKey.Oath.Credential"/> we expose
    /// to UI without leaking SDK types beyond Core.
    /// </summary>
    public sealed class YubiKeyOathCredential
    {
        public string Issuer { get; init; } = string.Empty;
        public string AccountName { get; init; } = string.Empty;
        public int Period { get; init; }
        public int Digits { get; init; }
        public string AlgorithmName { get; init; } = "SHA1";
        public bool RequiresTouch { get; init; }
        public bool IsTotp { get; init; }
    }

    /// <summary>
    /// OATH (TOTP/HOTP) support for YubiKey. Implemented against the
    /// Yubico.YubiKey.Oath namespace shipped with Yubico.YubiKey 1.12.0.
    ///
    /// <para>
    /// The OATH applet is independent from FIDO2 and has its own password
    /// (not the FIDO2 PIN). When the applet has a password set, the caller
    /// must pass it through the <c>password</c> parameter on each call so
    /// the session can verify before reading or writing credentials.
    /// </para>
    /// </summary>
    public sealed partial class YubiKeyService
    {
        /// <summary>
        /// Returns true if a connected YubiKey exposes the OATH application.
        /// </summary>
        public bool SupportsOath()
        {
            try
            {
                var device = YubiKeyDevice.FindAll().FirstOrDefault();
                return device?.HasFeature(YubiKeyFeature.OathApplication) ?? false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Reports whether the OATH applet currently requires a password to
        /// list or calculate credentials. Returns null when no OATH-capable
        /// YubiKey is present.
        /// </summary>
        public bool? IsOathPasswordSet()
        {
            var device = TryFindOathDevice();
            if (device is null) return null;

            try
            {
                using var session = new OathSession(device);
                return session.IsPasswordProtected;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Lists every OATH credential stored on the YubiKey. Returns an empty
        /// list when no OATH-capable YubiKey is connected.
        /// </summary>
        /// <param name="password">
        /// OATH applet password. Required when the applet is password-protected.
        /// </param>
        public IReadOnlyList<YubiKeyOathCredential> ListOathCredentials(string? password = null)
        {
            var device = TryFindOathDevice();
            if (device is null) return Array.Empty<YubiKeyOathCredential>();

            using var session = new OathSession(device);
            VerifyOathPassword(session, password);

            var creds = session.GetCredentials();
            var result = new List<YubiKeyOathCredential>(creds.Count);
            foreach (var c in creds)
            {
                result.Add(new YubiKeyOathCredential
                {
                    Issuer = c.Issuer ?? string.Empty,
                    AccountName = c.AccountName ?? string.Empty,
                    Period = (int)(c.Period ?? CredentialPeriod.Period30),
                    Digits = c.Digits ?? 6,
                    AlgorithmName = (c.Algorithm ?? OathHashAlgorithm.Sha1).ToString(),
                    RequiresTouch = c.RequiresTouch ?? false,
                    IsTotp = c.Type == CredentialType.Totp
                });
            }
            return result;
        }

        /// <summary>
        /// Provisions a TOTP credential on the YubiKey's OATH applet. The
        /// <paramref name="secret"/> is the raw TOTP shared secret (decoded
        /// from the Base32 form used by authenticator apps). The caller is
        /// responsible for zeroising the buffer after this method returns.
        /// </summary>
        /// <param name="issuer">Issuer label (e.g. "PhantomVault").</param>
        /// <param name="accountName">Account name (e.g. user email).</param>
        /// <param name="secret">Raw TOTP shared secret bytes.</param>
        /// <param name="period">TOTP step in seconds (15, 30 or 60).</param>
        /// <param name="digits">Number of code digits (6, 7 or 8).</param>
        /// <param name="algorithm">HMAC algorithm — SHA1, SHA256 or SHA512.</param>
        /// <param name="requireTouch">If true, code calculation requires a touch.</param>
        /// <param name="password">OATH applet password when one is set.</param>
        public void AddOathTotpCredential(
            string issuer,
            string accountName,
            byte[] secret,
            int period = 30,
            int digits = 6,
            OathHashAlgorithm algorithm = OathHashAlgorithm.Sha1,
            bool requireTouch = false,
            string? password = null)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                throw new ArgumentException("Account name cannot be empty.", nameof(accountName));
            if (secret == null || secret.Length == 0)
                throw new ArgumentException("Secret cannot be empty.", nameof(secret));
            if (digits is < 6 or > 8)
                throw new ArgumentOutOfRangeException(nameof(digits), "Digits must be 6, 7 or 8.");
            if (period is not (15 or 30 or 60))
                throw new ArgumentOutOfRangeException(nameof(period), "Period must be 15, 30 or 60 seconds.");

            var device = RequireOathDevice();
            using var session = new OathSession(device);
            VerifyOathPassword(session, password);

            var credential = new Credential
            {
                Issuer = issuer ?? string.Empty,
                AccountName = accountName,
                Type = CredentialType.Totp,
                Period = period switch
                {
                    15 => CredentialPeriod.Period15,
                    60 => CredentialPeriod.Period60,
                    _ => CredentialPeriod.Period30
                },
                Algorithm = algorithm,
                Secret = Convert.ToBase64String(secret),
                Digits = digits,
                RequiresTouch = requireTouch
            };

            session.AddCredential(credential);
        }

        /// <summary>
        /// Removes a TOTP credential from the YubiKey's OATH applet.
        /// </summary>
        public void RemoveOathCredential(string issuer, string accountName, int period = 30, string? password = null)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                throw new ArgumentException("Account name cannot be empty.", nameof(accountName));

            var device = RequireOathDevice();
            using var session = new OathSession(device);
            VerifyOathPassword(session, password);

            var match = session.GetCredentials().FirstOrDefault(c =>
                string.Equals(c.AccountName, accountName, StringComparison.Ordinal) &&
                string.Equals(c.Issuer ?? string.Empty, issuer ?? string.Empty, StringComparison.Ordinal) &&
                (int)(c.Period ?? CredentialPeriod.Period30) == period &&
                c.Type == CredentialType.Totp);

            if (match is null)
                throw new InvalidOperationException(
                    $"OATH credential not found: issuer='{issuer}', account='{accountName}'.");

            session.RemoveCredential(match);
        }

        /// <summary>
        /// Calculates the current TOTP code for the named credential on the
        /// YubiKey. Returns null when the OATH applet, the credential, or a
        /// required touch are not available.
        /// </summary>
        public string? GenerateOathTotpCode(string issuer, string accountName, int period = 30, string? password = null)
        {
            if (string.IsNullOrWhiteSpace(accountName)) return null;

            var device = TryFindOathDevice();
            if (device is null) return null;

            try
            {
                using var session = new OathSession(device);
                VerifyOathPassword(session, password);

                var match = session.GetCredentials().FirstOrDefault(c =>
                    string.Equals(c.AccountName, accountName, StringComparison.Ordinal) &&
                    string.Equals(c.Issuer ?? string.Empty, issuer ?? string.Empty, StringComparison.Ordinal) &&
                    (int)(c.Period ?? CredentialPeriod.Period30) == period &&
                    c.Type == CredentialType.Totp);

                if (match is null) return null;

                var code = session.CalculateCredential(match);
                return code?.Value;
            }
            catch (OperationCanceledException)
            {
                // User declined touch — surface as "no code" rather than crashing the UI.
                return null;
            }
        }

        /// <summary>
        /// Sets or changes the OATH applet password.
        /// </summary>
        /// <param name="newPassword">New password (empty string clears it).</param>
        /// <param name="currentPassword">Existing password, or null if none set.</param>
        public void SetOathPassword(string newPassword, string? currentPassword = null)
        {
            var device = RequireOathDevice();
            using var session = new OathSession(device)
            {
                KeyCollector = MakeOathKeyCollector(currentPassword, newPassword)
            };

            if (string.IsNullOrEmpty(newPassword))
            {
                session.UnsetPassword();
            }
            else
            {
                session.SetPassword();
            }
        }

        private static Func<KeyEntryData, bool> MakeOathKeyCollector(string? currentPassword, string? newPassword)
        {
            return data =>
            {
                switch (data.Request)
                {
                    case KeyEntryRequest.VerifyOathPassword:
                        data.SubmitValue(Encoding.UTF8.GetBytes(currentPassword ?? string.Empty));
                        return true;
                    case KeyEntryRequest.SetOathPassword:
                        data.SubmitValues(
                            Encoding.UTF8.GetBytes(currentPassword ?? string.Empty),
                            Encoding.UTF8.GetBytes(newPassword ?? string.Empty));
                        return true;
                    case KeyEntryRequest.Release:
                        return true;
                    default:
                        return false;
                }
            };
        }

        // ----------------------------------------------------------------
        // Internals
        // ----------------------------------------------------------------

        private static IYubiKeyDevice? TryFindOathDevice()
        {
            try
            {
                return YubiKeyDevice.FindAll()
                    .FirstOrDefault(d => d.HasFeature(YubiKeyFeature.OathApplication));
            }
            catch
            {
                return null;
            }
        }

        private static IYubiKeyDevice RequireOathDevice()
        {
            return TryFindOathDevice()
                ?? throw new InvalidOperationException(
                    "No YubiKey with OATH support found. Insert a YubiKey 5 or later and try again.");
        }

        private static void VerifyOathPassword(OathSession session, string? password)
        {
            if (!session.IsPasswordProtected) return;

            if (string.IsNullOrEmpty(password))
                throw new InvalidOperationException(
                    "The YubiKey OATH applet is password-protected. Provide the OATH password.");

            var pwBytes = Encoding.UTF8.GetBytes(password);
            try
            {
                if (!session.TryVerifyPassword(pwBytes))
                {
                    throw new InvalidOperationException(
                        "OATH password verification failed. Check the password and try again.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pwBytes);
            }
        }
    }
}
