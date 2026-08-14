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

        private static bool IsContainerPath(string path)
            => path.EndsWith(".pvault", StringComparison.OrdinalIgnoreCase);

        // The string-passphrase WriteManifest/ReadManifest overloads have been
        // removed. Every caller now goes through the *Secure pair, so the manifest
        // API no longer accepts a GC-managed string that cannot be zeroed.
        public void WriteManifestSecure(VaultManifest manifest, string filePath, SecurePassword passphrase, string? keyfilePath = null, string? usbSerial = null, bool requireDualFactor = false)
            => WriteManifestSecure(manifest, filePath, passphrase, keyfilePath, usbSerial, requireDualFactor, overrideKdfParams: null);

        public void WriteManifestSecure(VaultManifest manifest, string filePath, SecurePassword passphrase, string? keyfilePath, string? usbSerial, bool requireDualFactor, ManifestKdfParams? overrideKdfParams)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("File path must be provided", nameof(filePath));
            if (passphrase == null) throw new ArgumentNullException(nameof(passphrase));

            // Container-embedded manifests live inside the .pvault file and must be
            // updated through the container service. Without this branch a .pvault
            // path would be overwritten with a standalone manifest payload, which
            // destroys the container. ReadManifestSecure and the legacy string-based
            // WriteManifest both have this branch; this overload was missing it.
            if (IsContainerPath(filePath))
            {
                if (_containerService == null)
                    throw new InvalidOperationException("PhantomContainerService is required for container-embedded manifests");

                // The container API takes a string; this is the interop boundary
                // where the pinned buffer has to be materialised.
                string? containerPassphrase = passphrase.IsEmpty ? null : new string(passphrase.AsSpan());
                _containerService.UpdateManifestInContainer(filePath, manifest, containerPassphrase, keyfilePath);
                return;
            }

            if (requireDualFactor)
            {
                if (passphrase.IsEmpty || string.IsNullOrEmpty(keyfilePath))
                {
                    throw new ArgumentException("Dual-factor authentication requires BOTH a passphrase AND a keyfile");
                }
            }
            else
            {

                if (passphrase.IsEmpty && string.IsNullOrEmpty(keyfilePath))
                {
                    throw new ArgumentException("Either a passphrase or keyfile must be provided");
                }
            }

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

            bool requireKeyfileMaterial = requireDualFactor || !string.IsNullOrEmpty(keyfilePath);
            using var combinedSecret = SecurePasswordCombiner.Combine(passphrase, keyfilePath, requireKeyfileMaterial);

            var effectiveKdf = overrideKdfParams ?? manifest.RuntimeKdfParams ?? ManifestKdfParams.Standard;
            byte[] key = _encryptionService.DeriveKey(
                combinedSecret.AsSpan(), salt,
                memoryCostKb: effectiveKdf.MemoryKb,
                iterations: effectiveKdf.Iterations,
                parallelism: effectiveKdf.Parallelism);
            try
            {

                SignManifest(manifest, key);

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

                    var payload = new
                    {
                        formatVersion = CurrentStandaloneManifestFormatVersion,
                        suite = CurrentStandaloneManifestSuite,
                        salt = manifest.SaltBase64,
                        nonce = Convert.ToBase64String(encResult.Nonce),
                        tag = Convert.ToBase64String(encResult.Tag),
                        ciphertext = Convert.ToBase64String(encResult.Ciphertext),
                        kdfParams = new
                        {
                            memoryKb = effectiveKdf.MemoryKb,
                            iterations = effectiveKdf.Iterations,
                            parallelism = effectiveKdf.Parallelism
                        }
                    };
                    string payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(filePath, payloadJson);

                    manifest.RuntimeKdfParams = effectiveKdf;
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

        public VaultManifest ReadManifestSecure(string filePath, SecurePassword passphrase, string? keyfilePath = null, string? usbSerial = null, bool requireDualFactor = false)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("File path must be provided", nameof(filePath));
            if (passphrase == null) throw new ArgumentNullException(nameof(passphrase));

            if (IsContainerPath(filePath))
            {
                if (_containerService == null)
                    throw new InvalidOperationException("PhantomContainerService is required for container-embedded manifests");
                if (!File.Exists(filePath))
                    throw new FileNotFoundException("Container not found", filePath);

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

            if (requireDualFactor)
            {
                if (passphrase.IsEmpty || string.IsNullOrEmpty(keyfilePath))
                {
                    throw new ArgumentException("Dual-factor authentication requires BOTH a passphrase AND a keyfile");
                }
            }
            else
            {

                if (passphrase.IsEmpty && string.IsNullOrEmpty(keyfilePath))
                {
                    throw new ArgumentException("Either a passphrase or keyfile must be provided");
                }
            }

            string payloadJson = File.ReadAllText(filePath);

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

                if (!string.IsNullOrEmpty(storedUsbSerial))
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(storedUsbSerial, @"^[A-Za-z0-9\-_]+$"))
                    {
                        throw new SecurityException($"Invalid USB serial number format in manifest: {storedUsbSerial}");
                    }
                }

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

                if (!string.IsNullOrEmpty(storedUsbSerial) && string.IsNullOrEmpty(usbSerial))
                {
                    throw new SecurityException($"Manifest is bound to USB device (serial: {storedUsbSerial}). USB device must be connected to read this manifest.");
                }

                byte[] salt = Convert.FromBase64String(saltBase64);
                byte[] nonce = Convert.FromBase64String(nonceBase64);
                byte[] tag = Convert.FromBase64String(tagBase64);
                byte[] ciphertext = Convert.FromBase64String(ciphertextBase64);

                // Parse KDF parameters from the outer envelope. Manifests written before this
                // field existed default to the historical Standard parameters (256 MiB / 6 / 0)
                // so they continue to unlock correctly.
                ManifestKdfParams envelopeKdf = ManifestKdfParams.Standard;
                if (root.TryGetProperty("kdfParams", out var kdfElement) && kdfElement.ValueKind == JsonValueKind.Object)
                {
                    int mem = kdfElement.TryGetProperty("memoryKb", out var mEl) && mEl.ValueKind == JsonValueKind.Number ? mEl.GetInt32() : ManifestKdfParams.Standard.MemoryKb;
                    int iters = kdfElement.TryGetProperty("iterations", out var iEl) && iEl.ValueKind == JsonValueKind.Number ? iEl.GetInt32() : ManifestKdfParams.Standard.Iterations;
                    int par = kdfElement.TryGetProperty("parallelism", out var pEl) && pEl.ValueKind == JsonValueKind.Number ? pEl.GetInt32() : ManifestKdfParams.Standard.Parallelism;
                    envelopeKdf = new ManifestKdfParams(mem, iters, par);
                }

                bool requireKeyfileMaterial = requireDualFactor || !string.IsNullOrEmpty(keyfilePath);
                using var combinedSecret = SecurePasswordCombiner.Combine(passphrase, keyfilePath, requireKeyfileMaterial);
                byte[] key = _encryptionService.DeriveKey(
                    combinedSecret.AsSpan(), salt,
                    memoryCostKb: envelopeKdf.MemoryKb,
                    iterations: envelopeKdf.Iterations,
                    parallelism: envelopeKdf.Parallelism);
                try
                {

                    if (!usesMinimalHeader && storedTimestamp > 0)
                    {
                        var currentTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        var ageSeconds = currentTimestamp - storedTimestamp;

                        const long MaxClockSkewSeconds = 300;
                        if (ageSeconds < -MaxClockSkewSeconds)
                        {
                            throw new SecurityException($"Manifest timestamp is in the future by {Math.Abs(ageSeconds)} seconds. Possible clock skew or replay attack.");
                        }

                        const long MaxAgeSeconds = 31536000;
                        if (ageSeconds > MaxAgeSeconds)
                        {
                            System.Diagnostics.Debug.WriteLine($"WARNING: Manifest timestamp is {ageSeconds / 86400} days old. Possible replay attack or very old manifest.");

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
                        manifest.RuntimeKdfParams = envelopeKdf;

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

        // CombineSecret (the plaintext string equivalent of SecurePasswordCombiner)
        // was removed along with the duplicated WriteManifest body it served. All
        // key derivation now goes through SecurePasswordCombiner's pinned buffers.

        public bool TryReadManifest(string filePath, string? passphrase, string? keyfilePath, out VaultManifest? manifest, out string? error, string? usbSerial = null, bool requireDualFactor = false)
        {
            manifest = null;
            error = null;
            try
            {
                using var sp = SecurePassword.FromString(passphrase);
                manifest = ReadManifestSecure(filePath, sp, keyfilePath, usbSerial, requireDualFactor);
                return true;
            }
            catch (Exception ex)
            {

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

        public static string ComputeIntegritySignature(VaultManifest manifest, byte[] key)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (key == null || key.Length < 32) throw new ArgumentException("Signing key must be at least 32 bytes", nameof(key));

            byte[] dataBytes = BuildCanonicalManifestBytes(manifest);
            byte[] hmac = HMACSHA256.HashData(key, dataBytes);
            return Convert.ToBase64String(hmac);
        }

        /// <summary>
        /// Builds the canonical, deterministic byte representation of a manifest's
        /// security-relevant fields. Used by both the HMAC integrity signature and
        /// the asymmetric Ed25519 manifest signature so the two schemes cover the
        /// exact same data. Signature fields themselves are excluded.
        /// </summary>
        private static byte[] BuildCanonicalManifestBytes(VaultManifest manifest)
        {
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
            canonicalData.Append('|');
            canonicalData.Append(manifest.PhantomKeyBridgeEnabled ? "1" : "0");
            canonicalData.Append('|');
            canonicalData.Append(manifest.PhantomKeyBridgeWorkspacePath ?? string.Empty);
            canonicalData.Append('|');
            canonicalData.Append(manifest.PhantomKeyBridgeReceiptPath ?? string.Empty);
            canonicalData.Append('|');
            canonicalData.Append(manifest.PhantomKeyBridgePolicyPath ?? string.Empty);
            canonicalData.Append('|');
            canonicalData.Append(manifest.PhantomKeyBridgeContinuityPath ?? string.Empty);
            canonicalData.Append('|');
            // Bind the signing public key so an attacker cannot swap in their own key.
            canonicalData.Append(manifest.ManifestSigningPublicKeyBase64 ?? string.Empty);

            return Encoding.UTF8.GetBytes(canonicalData.ToString());
        }

        public static bool VerifyIntegritySignature(VaultManifest manifest, byte[] key, bool requireSignature = false)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (key == null || key.Length < 32) throw new ArgumentException("Signing key must be at least 32 bytes", nameof(key));

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

        public static void SignManifest(VaultManifest manifest, byte[] key)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (key == null || key.Length < 32) throw new ArgumentException("Signing key must be at least 32 bytes", nameof(key));

            manifest.ManifestSequence++;

            manifest.IntegritySignatureBase64 = ComputeIntegritySignature(manifest, key);
            manifest.SignatureTimestamp = DateTimeOffset.UtcNow;
        }

        /// <summary>
        /// Signs the manifest asymmetrically with the vault's Ed25519 signing key.
        /// The public key is embedded in the manifest so any enrolled device can
        /// verify authenticity, while only a holder of <paramref name="signingPrivateKey"/>
        /// (an owner device) can mint a valid signature. Increments the manifest
        /// sequence to preserve anti-rollback semantics.
        /// </summary>
        public static void SignManifestEd25519(VaultManifest manifest, byte[] signingPrivateKey, byte[] signingPublicKey)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (signingPrivateKey == null || signingPrivateKey.Length == 0) throw new ArgumentException("Signing private key must not be empty", nameof(signingPrivateKey));
            if (signingPublicKey == null || signingPublicKey.Length == 0) throw new ArgumentException("Signing public key must not be empty", nameof(signingPublicKey));

            manifest.ManifestSequence++;
            manifest.ManifestSigningPublicKeyBase64 = Convert.ToBase64String(signingPublicKey);

            byte[] canonical = BuildCanonicalManifestBytes(manifest);
            byte[] signature = GiblexVault.Security.ZK.Keys.UserKeys.Sign(canonical, signingPrivateKey);
            manifest.ManifestEd25519SignatureBase64 = Convert.ToBase64String(signature);
            manifest.SignatureTimestamp = DateTimeOffset.UtcNow;
        }

        /// <summary>
        /// Verifies the manifest's Ed25519 signature against its embedded signing
        /// public key. Returns true (no asymmetric signature present) when
        /// <paramref name="requireSignature"/> is false, allowing legacy HMAC-only
        /// manifests to load. Throws on tamper.
        /// </summary>
        public static bool VerifyManifestEd25519(VaultManifest manifest, bool requireSignature = false)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));

            bool hasSig = !string.IsNullOrEmpty(manifest.ManifestEd25519SignatureBase64)
                          && !string.IsNullOrEmpty(manifest.ManifestSigningPublicKeyBase64);
            if (!hasSig)
            {
                if (requireSignature)
                    throw new SecurityException("Manifest asymmetric signature is required but missing.");
                return true;
            }

            byte[] canonical = BuildCanonicalManifestBytes(manifest);
            byte[] signature = Convert.FromBase64String(manifest.ManifestEd25519SignatureBase64!);
            byte[] publicKey = Convert.FromBase64String(manifest.ManifestSigningPublicKeyBase64!);

            if (!GiblexVault.Security.ZK.Keys.UserKeys.Verify(canonical, signature, publicKey))
                throw new SecurityException("Manifest asymmetric signature verification failed. The manifest may have been tampered with.");

            return true;
        }

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

        private static void ValidateUsbSerial(string? usbSerial)
        {
            if (string.IsNullOrEmpty(usbSerial)) return;

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


