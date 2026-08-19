using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.Services
{
    internal static class PinLockService
    {

        private const int SaltSizeBytes = 16;
        private const int HashSizeBytes = 32;

        /// <summary>
        /// Work factor for new PINs, taken from the manifest model so the value lives
        /// in exactly one place. Verification always uses the iteration count stored
        /// alongside the hash, so changing this never invalidates an existing PIN.
        /// </summary>
        private static int DefaultPinIterations => new VaultManifest().PinPbkdf2Iterations;

        // PIN salt/hash at rest are DPAPI-sealed (CurrentUser) and stored with this
        // prefix. Values without the prefix are legacy plaintext and are migrated
        // opportunistically on first successful load.
        private const string DpapiPrefix = "dpapi:";
        private static readonly byte[] PinEntropy = Encoding.UTF8.GetBytes("PhantomVault.pinlock.v1");

        private static string ProtectStored(string base64Value)
        {
            if (!OperatingSystem.IsWindows()) return base64Value;
            var plain = Convert.FromBase64String(base64Value);
            try
            {
                var sealedBytes = ProtectedData.Protect(plain, PinEntropy, DataProtectionScope.CurrentUser);
                return DpapiPrefix + Convert.ToBase64String(sealedBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }

        private static string? UnprotectStored(string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return null;
            if (!stored.StartsWith(DpapiPrefix, StringComparison.Ordinal)) return stored; // legacy plaintext
            if (!OperatingSystem.IsWindows()) return null;

            try
            {
                var sealedBytes = Convert.FromBase64String(stored.Substring(DpapiPrefix.Length));
                var plain = ProtectedData.Unprotect(sealedBytes, PinEntropy, DataProtectionScope.CurrentUser);
                try { return Convert.ToBase64String(plain); }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
            catch
            {
                // Different machine/user profile or corrupt blob — PIN unlock is
                // unavailable; the user falls back to the master password.
                return null;
            }
        }

        private static bool IsProtected(string? stored) =>
            stored != null && stored.StartsWith(DpapiPrefix, StringComparison.Ordinal);

        public static bool HasPinConfigured(UserSettings settings, string? manifestPath = null)
        {

            if (!string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
            {
                try
                {
                    var manifest = LoadManifestFromDisk(manifestPath);
                    if (manifest != null && HasManifestPinConfigured(manifest))
                    {
                        return true;
                    }
                }
                catch
                {

                }
            }

            if (settings == null) return false;
            return !string.IsNullOrWhiteSpace(settings.PinSaltBase64)
                   && !string.IsNullOrWhiteSpace(settings.PinHashBase64)
                   && settings.PinPbkdf2Iterations > 0;
        }

        /// <summary>
        /// The EnablePinLock / UsePinLockForAutoLock flags are only meaningful when
        /// a PIN actually exists. Older builds could persist them without ever
        /// writing a salt+hash (neither in settings nor in the manifest), which left
        /// auto-lock demanding a PIN the user had never set. Clear the flags whenever
        /// no PIN material is stored, and persist the correction.
        /// Returns true when a PIN is genuinely configured.
        /// </summary>
        public static bool SyncPinFlags(string? manifestPath = null)
        {
            UserSettings settings;
            try { settings = SettingsService.Load(); }
            catch { return false; }

            bool configured;
            try { configured = HasPinConfigured(settings, manifestPath); }
            catch { configured = false; }

            if (!configured && (settings.EnablePinLock || settings.UsePinLockForAutoLock))
            {
                settings.EnablePinLock = false;
                settings.UsePinLockForAutoLock = false;
                try { SettingsService.Save(settings); } catch { /* best-effort */ }
            }

            return configured;
        }

        private static bool HasManifestPinConfigured(VaultManifest manifest)
        {
            return !string.IsNullOrWhiteSpace(manifest.PinSaltBase64)
                   && !string.IsNullOrWhiteSpace(manifest.PinHashBase64)
                   && manifest.PinPbkdf2Iterations > 0;
        }

        /// <summary>
        /// Minimum length for the vault's own unlock PIN.
        ///
        /// This is an authentication factor — it is verified against a stored hash, so its
        /// length is the only thing bounding an offline search, and six is the floor used
        /// by platform authenticators. It is deliberately NOT shared with
        /// <see cref="PinLengthRange"/>, which describes PINs stored as credential data
        /// (a card PIN, a door code) and has no security role.
        /// </summary>
        public const int MinVaultPinLength = 6;
        public const int MaxVaultPinLength = 8;

        public static void SetPin(string pin, string? manifestPath = null)
        {
            if (string.IsNullOrWhiteSpace(pin))
                throw new ArgumentException("PIN cannot be empty", nameof(pin));

            if (pin.Length < MinVaultPinLength)
                throw new ArgumentException(
                    $"PIN must be at least {MinVaultPinLength} digits/characters", nameof(pin));

            if (pin.Length > MaxVaultPinLength || pin.Any(c => !char.IsDigit(c)))
                throw new ArgumentException(
                    $"PIN must contain {MinVaultPinLength}-{MaxVaultPinLength} digits", nameof(pin));

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);

            // Read the work factor from the manifest model's default rather than
            // inlining it. This literal was 150_000 and silently overrode the
            // VaultManifest default, so raising that default alone changed nothing.
            int iterations = DefaultPinIterations;

            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                password: pin,
                salt: salt,
                iterations: iterations,
                hashAlgorithm: HashAlgorithmName.SHA256,
                outputLength: HashSizeBytes);

            string saltBase64 = Convert.ToBase64String(salt);
            string hashBase64 = Convert.ToBase64String(hash);

            if (!string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
            {
                try
                {
                    SetManifestPin(manifestPath, saltBase64, hashBase64, iterations);
                }
                catch (Exception ex)
                {
                    CryptographicOperations.ZeroMemory(hash);
                    CryptographicOperations.ZeroMemory(salt);
                    throw new InvalidOperationException($"Failed to save PIN to manifest: {ex.Message}", ex);
                }
            }
            else
            {

                var settings = SettingsService.Load();
                settings.PinSaltBase64 = ProtectStored(saltBase64);
                settings.PinHashBase64 = ProtectStored(hashBase64);
                settings.EnablePinLock = true;
                settings.PinPbkdf2Iterations = iterations;
                SettingsService.Save(settings);
            }

            CryptographicOperations.ZeroMemory(hash);
            CryptographicOperations.ZeroMemory(salt);
        }

        public static void ClearPin(string? manifestPath = null)
        {

            if (!string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
            {
                try
                {
                    ClearManifestPin(manifestPath);
                }
                catch
                {

                }
            }

            var settings = SettingsService.Load();
            settings.EnablePinLock = false;
            settings.PinSaltBase64 = null;
            settings.PinHashBase64 = null;
            SettingsService.Save(settings);
        }

        public static bool VerifyPin(string pin, string? manifestPath = null)
        {
            if (string.IsNullOrEmpty(pin)) return false;

            if (!string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
            {
                try
                {
                    var manifest = LoadManifestFromDisk(manifestPath);
                    if (manifest != null && HasManifestPinConfigured(manifest))
                    {
                        var manifestSalt = UnprotectStored(manifest.PinSaltBase64);
                        var manifestHash = UnprotectStored(manifest.PinHashBase64);
                        if (manifestSalt == null || manifestHash == null) return false;

                        if (!IsProtected(manifest.PinSaltBase64) || !IsProtected(manifest.PinHashBase64))
                        {
                            try { SetManifestPin(manifestPath, manifestSalt, manifestHash, manifest.PinPbkdf2Iterations); }
                            catch { /* best-effort migration */ }
                        }

                        return VerifyPinAgainstData(pin, manifestSalt, manifestHash, manifest.PinPbkdf2Iterations);
                    }
                }
                catch
                {

                }
            }

            var settings = SettingsService.Load();
            if (!settings.EnablePinLock) return false;
            if (!HasPinConfigured(settings)) return false;

            var salt = UnprotectStored(settings.PinSaltBase64);
            var hash = UnprotectStored(settings.PinHashBase64);
            if (salt == null || hash == null) return false;

            if (!IsProtected(settings.PinSaltBase64) || !IsProtected(settings.PinHashBase64))
            {
                try
                {
                    settings.PinSaltBase64 = ProtectStored(salt);
                    settings.PinHashBase64 = ProtectStored(hash);
                    SettingsService.Save(settings);
                }
                catch { /* best-effort migration */ }
            }

            return VerifyPinAgainstData(pin, salt, hash, settings.PinPbkdf2Iterations);
        }

        private static bool VerifyPinAgainstData(string pin, string saltBase64, string hashBase64, int iterations)
        {
            byte[] salt;
            byte[] expectedHash;
            try
            {
                salt = Convert.FromBase64String(saltBase64);
                expectedHash = Convert.FromBase64String(hashBase64);
            }
            catch
            {
                return false;
            }

            if (iterations <= 0) iterations = DefaultPinIterations;

            byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
                password: pin,
                salt: salt,
                iterations: iterations,
                hashAlgorithm: HashAlgorithmName.SHA256,
                outputLength: expectedHash.Length);

            bool ok = CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);

            CryptographicOperations.ZeroMemory(actualHash);
            CryptographicOperations.ZeroMemory(expectedHash);
            CryptographicOperations.ZeroMemory(salt);

            return ok;
        }

        private static VaultManifest? LoadManifestFromDisk(string path)
        {
            if (!File.Exists(path)) return null;

            string pinFile = path + ".pin.json";
            if (!File.Exists(pinFile)) return null;

            string json = File.ReadAllText(pinFile);
            return JsonSerializer.Deserialize<VaultManifest>(json);
        }

        private static void SetManifestPin(string manifestPath, string saltBase64, string hashBase64, int iterations)
        {
            string pinFile = manifestPath + ".pin.json";

            var pinData = new VaultManifest
            {
                PinSaltBase64 = ProtectStored(saltBase64),
                PinHashBase64 = ProtectStored(hashBase64),
                PinPbkdf2Iterations = iterations
            };

            string json = JsonSerializer.Serialize(pinData, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(pinFile, json);
        }

        private static void ClearManifestPin(string manifestPath)
        {
            string pinFile = manifestPath + ".pin.json";
            if (File.Exists(pinFile))
            {
                File.Delete(pinFile);
            }
        }
    }
}

