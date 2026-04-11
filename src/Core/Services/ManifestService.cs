using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhantomVault.Core.Models;
using PhantomVault.Core.Utils;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Handles persisting and retrieving encrypted manifest files. A manifest
    /// stores metadata about the vault (for example the container size and
    /// configuration) without revealing any secrets. The manifest is
    /// authenticated and encrypted with a passphrase‑derived key (KEK).
    /// 
    /// Phase 2: The manifest itself remains encrypted with traditional Argon2id.
    /// However, it now stores the encrypted ML-KEM private key and KEM ciphertext
    /// for post-quantum vault data encryption.
    /// </summary>
    public sealed class ManifestService
    {
        private const int CurrentStandaloneManifestFormatVersion = 2;
        private const string CurrentStandaloneManifestSuite = "manifest.aes256gcm.argon2id.v2";
        private readonly EncryptionService _encryptionService;
        private readonly PhantomContainerService? _containerService;

        public ManifestService(EncryptionService encryptionService, PhantomContainerService? containerService = null)
        {
            _encryptionService = encryptionService;
            _containerService = containerService;
        }

        /// <summary>
        /// Returns true when the given path points to a .pvault container
        /// (manifest embedded inside the container rather than a standalone file).
        /// </summary>
        private static bool IsContainerPath(string path)
            => path.EndsWith(".pvault", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Writes an encrypted manifest to disk. The manifest itself is
        /// serialized to JSON and then encrypted using a key derived from
        /// the provided passphrase and the generated salt. If a keyfile
        /// path is provided, its contents are concatenated with the
        /// passphrase prior to derivation.
        /// </summary>
        /// <param name="manifest">The manifest to persist.</param>
        /// <param name="filePath">The target file path.</param>
        /// <param name="passphrase">The user passphrase.</param>
        /// <param name="keyfilePath">Optional path to a keyfile.</param>
        /// <param name="usbSerial">Optional USB serial number for additional AAD binding.</param>
        /// <param name="requireDualFactor">When true, both passphrase AND keyfile are required (dual-factor authentication).</param>
        [Obsolete("Use WriteManifestSecure overload with SecurePassword for better memory security")]
        public void WriteManifest(VaultManifest manifest, string filePath, string? passphrase, string? keyfilePath = null, string? usbSerial = null, bool requireDualFactor = false)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("File path must be provided", nameof(filePath));

            // Route .pvault paths to the container service (v3 embedded manifest)
            if (IsContainerPath(filePath))
            {
                if (_containerService == null)
                    throw new InvalidOperationException("PhantomContainerService is required for container-embedded manifests");
                _containerService.UpdateManifestInContainer(filePath, manifest, passphrase, keyfilePath);
                return;
            }

            // Dual-factor authentication: BOTH passphrase AND keyfile required
            if (requireDualFactor)
            {
                if (string.IsNullOrEmpty(passphrase) || string.IsNullOrEmpty(keyfilePath))
                {
                    throw new ArgumentException("Dual-factor authentication requires BOTH a passphrase AND a keyfile");
                }
            }
            else
            {
                // Legacy mode: at least one authentication method (passphrase OR keyfile) must be provided
                if (string.IsNullOrEmpty(passphrase) && string.IsNullOrEmpty(keyfilePath))
                {
                    throw new ArgumentException("Either a passphrase or keyfile must be provided");
                }
            }

            // Generate or reuse the salt for this manifest. If the caller already
            // populated <see cref="VaultManifest.SaltBase64"/> we respect that so
            // additional data (such as the encrypted KEM private key) can reuse the
            // same KEK. This is critical for Phase 2 hybrid encryption where the
            // manifest salt must match the salt used to encrypt the ML-KEM secret.
            byte[] salt;
            if (!string.IsNullOrEmpty(manifest.SaltBase64))
            {
                try
                {
                    salt = Convert.FromBase64String(manifest.SaltBase64);
                }
                catch (FormatException ex)
                {
                    throw new FormatException("Manifest salt is not valid Base64", ex);
                }
            }
            else
            {
                salt = _encryptionService.GenerateSalt();
                manifest.SaltBase64 = Convert.ToBase64String(salt);
            }

            // Combine passphrase with keyfile contents if provided. This
            // increases entropy and allows multi‑factor authentication.
            // If no passphrase, use empty string as base.
            bool requireKeyfileMaterial = requireDualFactor || !string.IsNullOrEmpty(keyfilePath);
            string combinedSecret = CombineSecret(passphrase, keyfilePath, requireKeyfileMaterial);

            // Derive the encryption key (KEK). This operation is intentionally
            // expensive to thwart brute force attacks.
            // Phase 2 Note: Manifest itself always uses traditional KEK, not hybrid.
            byte[] key = _encryptionService.DeriveKey(combinedSecret.AsSpan(), salt);

            // Sign the manifest before serialization for integrity verification
            SignManifest(manifest, key);

            // Serialize the manifest to JSON. We use UTF8 because it is the
            // de‑facto encoding for JSON and ensures deterministic byte output.
            string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = false
            });
            byte[] plainBytes = Encoding.UTF8.GetBytes(json);

            string containerPath = NormalizeContainerObjectId(manifest.ContainerPath);
            manifest.ContainerPath = containerPath;
            ValidateUsbSerial(usbSerial);

            byte[] aad = BuildStandaloneManifestAad(filePath, usbSerial);
            var encResult = _encryptionService.Encrypt(plainBytes, key, aad);

            // Persist only the minimum public header material required to bootstrap
            // key derivation and detect the payload format. All binding metadata stays
            // inside the encrypted manifest body.
            var payload = new
            {
                formatVersion = CurrentStandaloneManifestFormatVersion,
                suite = CurrentStandaloneManifestSuite,
                salt = manifest.SaltBase64,
                nonce = Convert.ToBase64String(encResult.Nonce),
                tag = Convert.ToBase64String(encResult.Tag),
                ciphertext = Convert.ToBase64String(encResult.Ciphertext)
            };
            string payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, payloadJson);

            // Wipe sensitive material from memory as best we can.
            Array.Clear(key, 0, key.Length);
            Array.Clear(plainBytes, 0, plainBytes.Length);
        }

        /// <summary>
        /// Writes an encrypted manifest to disk using secure password handling.
        /// This method ensures that passwords are properly zeroed from memory after use.
        /// RECOMMENDED: Use this method instead of the string-based overload for better security.
        /// </summary>
        /// <param name="manifest">The manifest to persist.</param>
        /// <param name="filePath">The target file path.</param>
        /// <param name="passphrase">The user passphrase (will be securely zeroed after use).</param>
        /// <param name="keyfilePath">Optional path to a keyfile.</param>
        /// <param name="usbSerial">Optional USB serial number for additional AAD binding.</param>
        /// <param name="requireDualFactor">When true, both passphrase AND keyfile are required (dual-factor authentication).</param>
        public void WriteManifestSecure(VaultManifest manifest, string filePath, SecurePassword passphrase, string? keyfilePath = null, string? usbSerial = null, bool requireDualFactor = false)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("File path must be provided", nameof(filePath));
            if (passphrase == null) throw new ArgumentNullException(nameof(passphrase));

            // Dual-factor authentication: BOTH passphrase AND keyfile required
            if (requireDualFactor)
            {
                if (passphrase.IsEmpty || string.IsNullOrEmpty(keyfilePath))
                {
                    throw new ArgumentException("Dual-factor authentication requires BOTH a passphrase AND a keyfile");
                }
            }
            else
            {
                // Legacy mode: at least one authentication method (passphrase OR keyfile) must be provided
                if (passphrase.IsEmpty && string.IsNullOrEmpty(keyfilePath))
                {
                    throw new ArgumentException("Either a passphrase or keyfile must be provided");
                }
            }

            // Generate or reuse the salt for this manifest
            byte[] salt;
            if (!string.IsNullOrEmpty(manifest.SaltBase64))
            {
                try
                {
                    salt = Convert.FromBase64String(manifest.SaltBase64);
                }
                catch (FormatException ex)
                {
                    throw new FormatException("Manifest salt is not valid Base64", ex);
                }
            }
            else
            {
                salt = _encryptionService.GenerateSalt();
                manifest.SaltBase64 = Convert.ToBase64String(salt);
            }

            // Securely combine passphrase with keyfile contents
            bool requireKeyfileMaterial = requireDualFactor || !string.IsNullOrEmpty(keyfilePath);
            using var combinedSecret = SecurePasswordCombiner.Combine(passphrase, keyfilePath, requireKeyfileMaterial);

            // Derive the encryption key (KEK)
            byte[] key = _encryptionService.DeriveKey(combinedSecret.AsSpan(), salt);
            try
            {
                // Sign the manifest before serialization
                SignManifest(manifest, key);

                // Serialize the manifest to JSON
                string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
                {
                    WriteIndented = false
                });
                byte[] plainBytes = Encoding.UTF8.GetBytes(json);
                try
                {
                    string containerPath = NormalizeContainerObjectId(manifest.ContainerPath);
                    manifest.ContainerPath = containerPath;
                    ValidateUsbSerial(usbSerial);

                    byte[] aad = BuildStandaloneManifestAad(filePath, usbSerial);

                    var encResult = _encryptionService.Encrypt(plainBytes, key, aad);

                    // Persist only the minimum public header material required to
                    // bootstrap key derivation and payload format detection.
                    var payload = new
                    {
                        formatVersion = CurrentStandaloneManifestFormatVersion,
                        suite = CurrentStandaloneManifestSuite,
                        salt = manifest.SaltBase64,
                        nonce = Convert.ToBase64String(encResult.Nonce),
                        tag = Convert.ToBase64String(encResult.Tag),
                        ciphertext = Convert.ToBase64String(encResult.Ciphertext)
                    };
                    string payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(filePath, payloadJson);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plainBytes);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        /// <summary>
        /// Reads an encrypted manifest from disk and returns the
        /// deserialized <see cref="VaultManifest"/>. The caller must supply
        /// the correct passphrase and optional keyfile path. If the file is
        /// corrupted or the authentication fails, an exception is thrown.
        /// 
        /// Phase 2: Manifest is always decrypted with traditional KEK, not hybrid.
        /// The manifest contains encrypted KEM private key and KEM ciphertext
        /// that are used for vault data encryption.
        /// </summary>
        /// <param name="filePath">Path to the manifest on disk.</param>
        /// <param name="passphrase">User passphrase.</param>
        /// <param name="keyfilePath">Optional path to a keyfile.</param>
        /// <param name="usbSerial">Optional USB serial number for AAD validation.</param>
        /// <param name="requireDualFactor">When true, both passphrase AND keyfile are required (dual-factor authentication).</param>
        [Obsolete("Use ReadManifestSecure overload with SecurePassword for better memory security")]
        public VaultManifest ReadManifest(string filePath, string? passphrase, string? keyfilePath = null, string? usbSerial = null, bool requireDualFactor = false)
        {
            using var securePassphrase = string.IsNullOrEmpty(passphrase)
                ? SecurePassword.Empty()
                : SecurePassword.FromString(passphrase);
            return ReadManifestSecure(filePath, securePassphrase, keyfilePath, usbSerial, requireDualFactor);
        }

        /// <summary>
        /// Reads an encrypted manifest from disk using secure password handling.
        /// This method ensures password buffers can be wiped after key derivation.
        /// </summary>
        public VaultManifest ReadManifestSecure(string filePath, SecurePassword passphrase, string? keyfilePath = null, string? usbSerial = null, bool requireDualFactor = false)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("File path must be provided", nameof(filePath));
            if (passphrase == null) throw new ArgumentNullException(nameof(passphrase));

            // Route .pvault paths to the container service (v3 embedded manifest)
            if (IsContainerPath(filePath))
            {
                if (_containerService == null)
                    throw new InvalidOperationException("PhantomContainerService is required for container-embedded manifests");
                if (!File.Exists(filePath))
                    throw new FileNotFoundException("Container not found", filePath);

                // Container service still accepts string credentials today.
                string? containerPassphrase = passphrase.IsEmpty ? null : new string(passphrase.AsSpan());
                try
                {
                    return _containerService.ReadManifestFromContainer(filePath, containerPassphrase, keyfilePath)
                        ?? throw new FileNotFoundException("No embedded manifest found in container", filePath);
                }
                finally
                {
                    if (containerPassphrase != null)
                    {
                        containerPassphrase = new string('\0', containerPassphrase.Length);
                    }
                }
            }

            if (!File.Exists(filePath)) throw new FileNotFoundException("Manifest file not found", filePath);

            // Dual-factor authentication: BOTH passphrase AND keyfile required
            if (requireDualFactor)
            {
                if (passphrase.IsEmpty || string.IsNullOrEmpty(keyfilePath))
                {
                    throw new ArgumentException("Dual-factor authentication requires BOTH a passphrase AND a keyfile");
                }
            }
            else
            {
                // Legacy mode: at least one authentication method (passphrase OR keyfile) must be provided
                if (passphrase.IsEmpty && string.IsNullOrEmpty(keyfilePath))
                {
                    throw new ArgumentException("Either a passphrase or keyfile must be provided");
                }
            }

            string payloadJson = File.ReadAllText(filePath);

            // Defensive: remove common BOM and whitespace issues
            payloadJson = payloadJson.Trim();
            if (payloadJson.Length > 0 && payloadJson[0] == '\uFEFF')
            {
                payloadJson = payloadJson.Substring(1);
            }

            JsonDocument? doc;
            if (!JsonUtils.TryParseRecovering(payloadJson, out doc, out var parseError))
            {
                throw new FormatException($"Manifest JSON is malformed: {parseError}");
            }
            using (doc!)
            {
                var root = doc!.RootElement;

                string saltBase64 = root.GetProperty("salt").GetString() ?? throw new FormatException("Missing salt");
                string nonceBase64 = root.GetProperty("nonce").GetString() ?? throw new FormatException("Missing nonce");
                string tagBase64 = root.GetProperty("tag").GetString() ?? throw new FormatException("Missing tag");
                string ciphertextBase64 = root.GetProperty("ciphertext").GetString() ?? throw new FormatException("Missing ciphertext");

                byte[] aad;
                bool usesMinimalHeader = false;
                string? storedUsbSerial = null;
                long storedTimestamp = 0;

                if (root.TryGetProperty("formatVersion", out var formatVersionElement) &&
                    formatVersionElement.ValueKind == JsonValueKind.Number &&
                    formatVersionElement.GetInt32() >= CurrentStandaloneManifestFormatVersion)
                {
                    usesMinimalHeader = true;
                    aad = BuildStandaloneManifestAad(filePath, usbSerial);
                }
                else
                {
                    if (root.TryGetProperty("usbSerial", out var usbElement) && usbElement.ValueKind == JsonValueKind.String)
                    {
                        storedUsbSerial = usbElement.GetString();
                    }

                    if (root.TryGetProperty("timestamp", out var tsElement) && tsElement.ValueKind == JsonValueKind.Number)
                    {
                        storedTimestamp = tsElement.GetInt64();
                    }

                    if (root.TryGetProperty("aad", out var aadElement) && aadElement.ValueKind == JsonValueKind.String)
                    {
                        string? aadBase64 = aadElement.GetString();
                        aad = string.IsNullOrEmpty(aadBase64) ? Array.Empty<byte>() : Convert.FromBase64String(aadBase64);
                    }
                    else if (root.TryGetProperty("containerPath", out var containerElement) && containerElement.ValueKind == JsonValueKind.String)
                    {
                        string? pathValue = containerElement.GetString();
                        string aadString = string.IsNullOrEmpty(storedUsbSerial)
                            ? (pathValue ?? string.Empty)
                            : $"{pathValue}|USB:{storedUsbSerial}";
                        if (storedTimestamp > 0)
                        {
                            aadString = $"{aadString}|TS:{storedTimestamp}";
                        }
                        aad = Encoding.UTF8.GetBytes(aadString);
                    }
                    else
                    {
                        throw new FormatException("Manifest payload missing associated data binding (aad/containerPath). The file may be from an unsupported version.");
                    }
                }

                // Validate USB serial format if stored in manifest
                if (!string.IsNullOrEmpty(storedUsbSerial))
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(storedUsbSerial, @"^[A-Za-z0-9\-_]+$"))
                    {
                        throw new SecurityException($"Invalid USB serial number format in manifest: {storedUsbSerial}");
                    }
                }

                // Validate USB serial if provided
                if (!string.IsNullOrEmpty(usbSerial))
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(usbSerial, @"^[A-Za-z0-9\-_]+$"))
                    {
                        throw new ArgumentException($"Invalid USB serial number format: {usbSerial}");
                    }

                    if (!string.IsNullOrEmpty(storedUsbSerial) && !string.Equals(usbSerial, storedUsbSerial, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new SecurityException($"USB serial mismatch. Expected: {storedUsbSerial}, Got: {usbSerial}");
                    }
                }

                // If manifest was bound to a USB device, require USB serial on read
                // This prevents reading a USB-bound manifest without the USB device present
                if (!string.IsNullOrEmpty(storedUsbSerial) && string.IsNullOrEmpty(usbSerial))
                {
                    throw new SecurityException($"Manifest is bound to USB device (serial: {storedUsbSerial}). USB device must be connected to read this manifest.");
                }

                byte[] salt = Convert.FromBase64String(saltBase64);
                byte[] nonce = Convert.FromBase64String(nonceBase64);
                byte[] tag = Convert.FromBase64String(tagBase64);
                byte[] ciphertext = Convert.FromBase64String(ciphertextBase64);

                bool requireKeyfileMaterial = requireDualFactor || !string.IsNullOrEmpty(keyfilePath);
                using var combinedSecret = SecurePasswordCombiner.Combine(passphrase, keyfilePath, requireKeyfileMaterial);
                byte[] key = _encryptionService.DeriveKey(combinedSecret.AsSpan(), salt);
                try
                {
                    // Validate timestamp to detect replay attacks and clock skew
                    if (!usesMinimalHeader && storedTimestamp > 0)
                    {
                        var currentTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        var ageSeconds = currentTimestamp - storedTimestamp;

                        // Check for future timestamps (clock skew or tampering)
                        const long MaxClockSkewSeconds = 300; // 5 minutes tolerance
                        if (ageSeconds < -MaxClockSkewSeconds)
                        {
                            throw new SecurityException($"Manifest timestamp is in the future by {Math.Abs(ageSeconds)} seconds. Possible clock skew or replay attack.");
                        }

                        // Warn on old timestamps but allow (for legitimate old manifests)
                        const long MaxAgeSeconds = 31536000; // 1 year
                        if (ageSeconds > MaxAgeSeconds)
                        {
                            System.Diagnostics.Debug.WriteLine($"WARNING: Manifest timestamp is {ageSeconds / 86400} days old. Possible replay attack or very old manifest.");
                            // Don't throw - allow old manifests, but log for auditing
                        }
                    }

                    byte[] plaintext;
                    try
                    {
                        plaintext = _encryptionService.Decrypt(ciphertext, nonce, tag, key, aad);
                    }
                    catch (CryptographicException) when (usesMinimalHeader)
                    {
                        throw new SecurityException(string.IsNullOrEmpty(usbSerial)
                            ? "Manifest binding validation failed. Ensure the correct device context is present before opening this manifest."
                            : "Manifest binding validation failed. The supplied device context or credentials did not match this manifest.");
                    }
                    try
                    {
                        string json = Encoding.UTF8.GetString(plaintext);
                        var manifest = JsonSerializer.Deserialize<VaultManifest>(json) ?? throw new FormatException("Failed to parse manifest");
                        manifest.ContainerPath = NormalizeContainerObjectId(manifest.ContainerPath);

                        // Verify integrity signature if present (defense-in-depth)
                        VerifyIntegritySignature(manifest, key, requireSignature: false);

                        if (!usesMinimalHeader &&
                            root.TryGetProperty("containerPath", out var pathElement) &&
                            pathElement.ValueKind == JsonValueKind.String)
                        {
                            string boundPath = NormalizeContainerObjectId(pathElement.GetString());
                            if (!string.IsNullOrEmpty(boundPath) && !string.Equals(boundPath, manifest.ContainerPath, StringComparison.Ordinal))
                            {
                                throw new SecurityException("Manifest integrity check failed: container path binding mismatch.");
                            }
                        }

                        return manifest;
                    }
                    finally
                    {
                        Array.Clear(plaintext, 0, plaintext.Length);
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(key);
                }
            }
        }

        private static string CombineSecret(string? passphrase, string? keyfilePath, bool keyfileRequired)
        {
            string combined = passphrase ?? string.Empty;

            if (string.IsNullOrWhiteSpace(keyfilePath))
            {
                if (keyfileRequired)
                {
                    throw new SecurityException("Keyfile required but no keyfile path was provided.");
                }

                return combined;
            }

            if (!File.Exists(keyfilePath))
            {
                string failureReason = keyfileRequired
                    ? $"Keyfile required but not found at path '{keyfilePath}'."
                    : $"Keyfile path '{keyfilePath}' could not be resolved.";
                throw new SecurityException(failureReason);
            }

            byte[] keyfileBytes = File.ReadAllBytes(keyfilePath);
            try
            {
                return combined + Convert.ToBase64String(keyfileBytes);
            }
            finally
            {
                Array.Clear(keyfileBytes, 0, keyfileBytes.Length);
            }
        }

        /// <summary>
        /// Non-throwing variant of ReadManifest. Attempts to read and decrypt
        /// the manifest. On failure returns false and a human-friendly error
        /// message suitable for UI display.
        /// 
        /// Phase 2: Manifest is always decrypted with traditional KEK.
        /// </summary>
        public bool TryReadManifest(string filePath, string? passphrase, string? keyfilePath, out VaultManifest? manifest, out string? error, string? usbSerial = null, bool requireDualFactor = false)
        {
            manifest = null;
            error = null;
            try
            {
                manifest = ReadManifest(filePath, passphrase, keyfilePath, usbSerial, requireDualFactor);
                return true;
            }
            catch (Exception ex)
            {
                // Convert common errors into user-friendly messages
                if (ex is FileNotFoundException)
                {
                    error = "Manifest file not found. Ensure the USB drive contains the .phantom folder with a valid manifest.";
                }
                else if (ex is FormatException || ex is JsonException)
                {
                    error = "Vault manifest appears to be malformed or corrupted. If this is a VeraCrypt container, ensure it was created by PhantomVault and not modified.";
                }
                else if (ex is UnauthorizedAccessException || ex is IOException)
                {
                    error = $"Could not access manifest file: {ex.Message}";
                }
                else
                {
                    error = ex.Message;
                }
                return false;
            }
        }

        /// <summary>
        /// Computes an HMAC-SHA256 signature over critical manifest fields.
        /// The signature provides defense-in-depth integrity verification.
        /// </summary>
        /// <param name="manifest">The manifest to sign.</param>
        /// <param name="key">The KEK-derived signing key (32 bytes).</param>
        /// <returns>Base64-encoded HMAC signature.</returns>
        public static string ComputeIntegritySignature(VaultManifest manifest, byte[] key)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (key == null || key.Length < 32) throw new ArgumentException("Signing key must be at least 32 bytes", nameof(key));

            // Canonical representation of critical fields including anti-rollback counter and policy hash
            var canonicalData = new StringBuilder();
            canonicalData.Append(manifest.ContainerPath ?? string.Empty);
            canonicalData.Append('|');
            canonicalData.Append(manifest.SaltBase64 ?? string.Empty);
            canonicalData.Append('|');
            canonicalData.Append(manifest.DeviceId ?? string.Empty);
            canonicalData.Append('|');
            canonicalData.Append(manifest.KemPublicKeyBase64 ?? string.Empty);
            canonicalData.Append('|');
            canonicalData.Append(manifest.YubiKeySerial?.ToString() ?? string.Empty);
            canonicalData.Append('|');
            canonicalData.Append(manifest.Algorithm ?? "AES-256-GCM");
            canonicalData.Append('|');
            canonicalData.Append(manifest.Version.ToString());
            canonicalData.Append('|');
            canonicalData.Append(manifest.ManifestSequence.ToString());
            canonicalData.Append('|');
            canonicalData.Append(manifest.PolicyHashBase64 ?? string.Empty);

            byte[] dataBytes = Encoding.UTF8.GetBytes(canonicalData.ToString());
            byte[] hmac = HMACSHA256.HashData(key, dataBytes);
            return Convert.ToBase64String(hmac);
        }

        /// <summary>
        /// Verifies the integrity signature of a manifest. Returns true if the signature
        /// is valid or if no signature is present (for backwards compatibility).
        /// </summary>
        /// <param name="manifest">The manifest to verify.</param>
        /// <param name="key">The KEK-derived signing key.</param>
        /// <param name="requireSignature">When true, throws if no signature is present.</param>
        /// <returns>True if signature is valid or absent; throws SecurityException otherwise.</returns>
        public static bool VerifyIntegritySignature(VaultManifest manifest, byte[] key, bool requireSignature = false)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (key == null || key.Length < 32) throw new ArgumentException("Signing key must be at least 32 bytes", nameof(key));

            // No signature present - allow for backwards compatibility unless required
            if (string.IsNullOrEmpty(manifest.IntegritySignatureBase64))
            {
                if (requireSignature)
                {
                    throw new SecurityException("Manifest integrity signature is required but missing.");
                }
                return true;
            }

            string expectedSignature = ComputeIntegritySignature(manifest, key);
            if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(manifest.IntegritySignatureBase64),
                Convert.FromBase64String(expectedSignature)))
            {
                throw new SecurityException("Manifest integrity signature verification failed. The manifest may have been tampered with.");
            }

            return true;
        }

        /// <summary>
        /// Signs the manifest by computing and storing an HMAC-SHA256 signature
        /// over critical fields. Call this before serializing the manifest.
        /// </summary>
        /// <param name="manifest">The manifest to sign.</param>
        /// <param name="key">The KEK-derived signing key.</param>
        public static void SignManifest(VaultManifest manifest, byte[] key)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (key == null || key.Length < 32) throw new ArgumentException("Signing key must be at least 32 bytes", nameof(key));

            // Increment monotonic counter on every write to prevent rollback attacks
            manifest.ManifestSequence++;

            manifest.IntegritySignatureBase64 = ComputeIntegritySignature(manifest, key);
            manifest.SignatureTimestamp = DateTimeOffset.UtcNow;
        }

        /// <summary>
        /// Verifies that the manifest sequence counter has not been rolled back.
        /// Call this after decrypting a manifest and before trusting its contents.
        /// </summary>
        /// <param name="manifest">The decrypted manifest.</param>
        /// <param name="lastKnownSequence">The last known sequence number (from previous session or stored locally).</param>
        /// <exception cref="SecurityException">Thrown when rollback is detected.</exception>
        public static void VerifyAntiRollback(VaultManifest manifest, long lastKnownSequence)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));

            if (manifest.ManifestSequence < lastKnownSequence)
            {
                throw new SecurityException(
                    $"Anti-rollback violation: manifest sequence {manifest.ManifestSequence} is below " +
                    $"last known sequence {lastKnownSequence}. The manifest may have been replaced with an older version.");
            }
        }

        /// <summary>
        /// Validates and canonicalizes the container object identifier used inside
        /// the encrypted manifest. Object IDs must be relative and portable.
        /// </summary>
        private static string NormalizeContainerObjectId(string? containerPath)
        {
            if (string.IsNullOrWhiteSpace(containerPath))
            {
                return string.Empty;
            }

            try
            {
                if (containerPath.Contains('\0'))
                {
                    throw new ArgumentException("Container path contains null bytes");
                }

                string normalized = containerPath
                    .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                    .Trim();

                if (Path.IsPathRooted(normalized))
                {
                    throw new ArgumentException($"Container path must be relative to the vault layout: {containerPath}");
                }

                var pathSegments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                if (pathSegments.Any(segment => segment == "." || segment == ".."))
                {
                    throw new ArgumentException($"Container path contains path traversal sequences: {containerPath}");
                }

                if (normalized.StartsWith(Path.DirectorySeparatorChar))
                {
                    throw new ArgumentException($"Container path must not start with a directory separator: {containerPath}");
                }

                return string.Join("/", pathSegments);
            }
            catch (Exception ex) when (ex is not ArgumentException)
            {
                throw new ArgumentException($"Invalid container path: {containerPath}", ex);
            }
        }

        private static byte[] BuildStandaloneManifestAad(string filePath, string? usbSerial)
        {
            string objectId = Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(objectId))
            {
                throw new ArgumentException("Manifest file path must include a file name.", nameof(filePath));
            }

            string aadString = $"OBJ:{objectId}";
            if (!string.IsNullOrEmpty(usbSerial))
            {
                aadString = $"{aadString}|USB:{usbSerial}";
            }

            return Encoding.UTF8.GetBytes(aadString);
        }

        /// <summary>
        /// Validates USB serial number format.
        /// </summary>
        private static void ValidateUsbSerial(string? usbSerial)
        {
            if (string.IsNullOrEmpty(usbSerial)) return;

            // USB serial should be alphanumeric with limited special chars
            if (!System.Text.RegularExpressions.Regex.IsMatch(usbSerial, @"^[A-Za-z0-9\-_]+$"))
            {
                throw new ArgumentException(
                    $"Invalid USB serial number format: {usbSerial}. " +
                    "Must contain only alphanumeric characters, hyphens, and underscores.");
            }

            if (usbSerial.Length > 128)
            {
                throw new ArgumentException($"USB serial number too long: {usbSerial.Length} characters (max 128)");
            }
        }
    }
}
