using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.Services
{
    internal static class PinLockService
    {
        // Keep the PIN hashing logic centralized and consistent.
        private const int SaltSizeBytes = 16;
        private const int HashSizeBytes = 32;

        /// <summary>
        /// Check if a PIN is configured for this vault. Priority: manifest PIN > UserSettings PIN.
        /// </summary>
        public static bool HasPinConfigured(UserSettings settings, string? manifestPath = null)
        {
            // Check manifest first if available
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
                    // Fallback to UserSettings if manifest load fails
                }
            }

            // Fallback to UserSettings PIN
            if (settings == null) return false;
            return !string.IsNullOrWhiteSpace(settings.PinSaltBase64)
                   && !string.IsNullOrWhiteSpace(settings.PinHashBase64)
                   && settings.PinPbkdf2Iterations > 0;
        }

        private static bool HasManifestPinConfigured(VaultManifest manifest)
        {
            return !string.IsNullOrWhiteSpace(manifest.PinSaltBase64)
                   && !string.IsNullOrWhiteSpace(manifest.PinHashBase64)
                   && manifest.PinPbkdf2Iterations > 0;
        }

        /// <summary>
        /// Set PIN for the vault. If manifestPath is provided, PIN is stored in manifest.
        /// Otherwise, PIN is stored in UserSettings (global).
        /// </summary>
        public static void SetPin(string pin, string? manifestPath = null)
        {
            if (string.IsNullOrWhiteSpace(pin))
                throw new ArgumentException("PIN cannot be empty", nameof(pin));

            // Very small guardrail; UI can enforce stricter rules later.
            if (pin.Length < 4)
                throw new ArgumentException("PIN must be at least 4 digits/characters", nameof(pin));

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
            int iterations = 150_000;

            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                password: pin,
                salt: salt,
                iterations: iterations,
                hashAlgorithm: HashAlgorithmName.SHA256,
                outputLength: HashSizeBytes);

            string saltBase64 = Convert.ToBase64String(salt);
            string hashBase64 = Convert.ToBase64String(hash);

            // Store in manifest if path provided
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
                // Fallback to UserSettings (global)
                var settings = SettingsService.Load();
                settings.PinSaltBase64 = saltBase64;
                settings.PinHashBase64 = hashBase64;
                settings.EnablePinLock = true;
                settings.PinPbkdf2Iterations = iterations;
                SettingsService.Save(settings);
            }

            CryptographicOperations.ZeroMemory(hash);
            CryptographicOperations.ZeroMemory(salt);
        }

        /// <summary>
        /// Clear PIN from both manifest (if provided) and UserSettings.
        /// </summary>
        public static void ClearPin(string? manifestPath = null)
        {
            // Clear from manifest if provided
            if (!string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
            {
                try
                {
                    ClearManifestPin(manifestPath);
                }
                catch
                {
                    // Continue to clear UserSettings even if manifest fails
                }
            }

            // Also clear from UserSettings
            var settings = SettingsService.Load();
            settings.EnablePinLock = false;
            settings.PinSaltBase64 = null;
            settings.PinHashBase64 = null;
            SettingsService.Save(settings);
        }

        /// <summary>
        /// Verify PIN against manifest (if available) or UserSettings.
        /// </summary>
        public static bool VerifyPin(string pin, string? manifestPath = null)
        {
            if (string.IsNullOrEmpty(pin)) return false;

            // Try manifest first if available
            if (!string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
            {
                try
                {
                    var manifest = LoadManifestFromDisk(manifestPath);
                    if (manifest != null && HasManifestPinConfigured(manifest))
                    {
                        return VerifyPinAgainstData(pin, manifest.PinSaltBase64!, manifest.PinHashBase64!, manifest.PinPbkdf2Iterations);
                    }
                }
                catch
                {
                    // Fallback to UserSettings if manifest verification fails
                }
            }

            // Fallback to UserSettings
            var settings = SettingsService.Load();
            if (!settings.EnablePinLock) return false;
            if (!HasPinConfigured(settings)) return false;

            return VerifyPinAgainstData(pin, settings.PinSaltBase64!, settings.PinHashBase64!, settings.PinPbkdf2Iterations);
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

            if (iterations <= 0) iterations = 150_000;

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

        // Manifest persistence helpers (direct JSON access - bypasses encryption for simplicity)
        private static VaultManifest? LoadManifestFromDisk(string path)
        {
            if (!File.Exists(path)) return null;
            
            // For encrypted manifests, we can't easily decrypt without passphrase.
            // Instead, we'll use a simpler approach: store PIN in a separate unencrypted sidecar file
            // OR require the manifest to be decrypted first by VaultViewModel.
            // For now, let's use a sidecar file approach: {manifestPath}.pin.json
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
                PinSaltBase64 = saltBase64,
                PinHashBase64 = hashBase64,
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
