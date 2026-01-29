using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhantomVault.Core;

/// <summary>
/// Represents the USB cryptographic key file (usb_key.json) stored on authorized USB devices.
/// SECURITY: Provides cryptographic authentication of USB devices beyond serial number validation.
/// </summary>
public class UsbKeyFile
{
    /// <summary>
    /// Unique identifier for this USB key. Must match one of the RequiredKeyIds in the policy.
    /// </summary>
    [JsonPropertyName("keyId")]
    public string KeyId { get; set; } = string.Empty;

    /// <summary>
    /// User-friendly label for this USB key (e.g., "Primary Key", "Backup Key #2").
    /// </summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>
    /// Ed25519 public key (base64-encoded) for verifying this USB key's authenticity.
    /// The private key should be held by the organization/administrator.
    /// </summary>
    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Digital signature of the key file, signed by the root certificate.
    /// This prevents unauthorized USB key creation.
    /// </summary>
    [JsonPropertyName("signature")]
    public string? Signature { get; set; }

    /// <summary>
    /// Timestamp when this key was issued (Unix seconds).
    /// </summary>
    [JsonPropertyName("issuedAt")]
    public long IssuedAt { get; set; }

    /// <summary>
    /// Optional expiration timestamp (Unix seconds). 0 = no expiration.
    /// </summary>
    [JsonPropertyName("expiresAt")]
    public long ExpiresAt { get; set; }

    /// <summary>
    /// Volume serial number this key is bound to (optional).
    /// Provides additional binding to specific physical USB device.
    /// </summary>
    [JsonPropertyName("volumeSerial")]
    public string? VolumeSerial { get; set; }

    /// <summary>
    /// Loads and verifies a USB key file from JSON.
    /// </summary>
    /// <param name="json">JSON content of usb_key.json</param>
    /// <param name="rootVerifier">ECDsa verifier from root certificate (optional)</param>
    /// <returns>Validated UsbKeyFile instance</returns>
    /// <exception cref="CryptographicException">If signature verification fails</exception>
    public static UsbKeyFile LoadAndVerify(string json, ECDsa? rootVerifier = null)
    {
        // Parse JSON
        var keyFile = JsonSerializer.Deserialize<UsbKeyFile>(json);
        if (keyFile == null)
            throw new InvalidOperationException("Failed to deserialize USB key file");

        // Validate required fields
        if (string.IsNullOrEmpty(keyFile.KeyId))
            throw new InvalidOperationException("USB key file missing required 'keyId' field");

        if (string.IsNullOrEmpty(keyFile.PublicKey))
            throw new InvalidOperationException("USB key file missing required 'publicKey' field");

        // Verify signature if present and verifier provided
        if (!string.IsNullOrEmpty(keyFile.Signature) && rootVerifier != null)
        {
            VerifyKeyFileSignature(keyFile, rootVerifier);
        }

        // Check expiration
        if (keyFile.ExpiresAt > 0)
        {
            var expirationTime = DateTimeOffset.FromUnixTimeSeconds(keyFile.ExpiresAt);
            if (DateTimeOffset.UtcNow > expirationTime)
            {
                throw new CryptographicException(
                    $"USB key '{keyFile.KeyId}' expired at {expirationTime:u}");
            }
        }

        return keyFile;
    }

    /// <summary>
    /// Verifies the digital signature on the USB key file.
    /// </summary>
    private static void VerifyKeyFileSignature(UsbKeyFile keyFile, ECDsa verifier)
    {
        if (string.IsNullOrEmpty(keyFile.Signature))
            throw new CryptographicException("USB key file signature is missing");

        try
        {
            // Create canonical JSON for signature verification (exclude signature field)
            var canonicalData = JsonSerializer.Serialize(new
            {
                keyId = keyFile.KeyId,
                label = keyFile.Label,
                publicKey = keyFile.PublicKey,
                issuedAt = keyFile.IssuedAt,
                expiresAt = keyFile.ExpiresAt,
                volumeSerial = keyFile.VolumeSerial
            }, new JsonSerializerOptions { WriteIndented = false });

            byte[] dataBytes = System.Text.Encoding.UTF8.GetBytes(canonicalData);
            byte[] signatureBytes = Convert.FromBase64String(keyFile.Signature);

            // Verify signature using SHA-512 (matching PolicyVerifier)
            bool isValid = verifier.VerifyData(dataBytes, signatureBytes, HashAlgorithmName.SHA512);

            if (!isValid)
            {
                throw new CryptographicException("USB key file signature verification failed");
            }
        }
        catch (Exception ex) when (ex is not CryptographicException)
        {
            throw new CryptographicException("USB key file signature verification failed", ex);
        }
    }

    /// <summary>
    /// Validates that this USB key matches the policy requirements.
    /// </summary>
    /// <param name="policy">Security policy to validate against</param>
    /// <param name="volumeSerial">Actual volume serial number of the USB device</param>
    /// <returns>True if validation passes</returns>
    public bool ValidateAgainstPolicy(ObscuraPolicy policy, string? volumeSerial)
    {
        // Check if keyId is in the required list
        if (policy.Usb.RequiredKeyIds.Any() &&
            !policy.Usb.RequiredKeyIds.Contains(KeyId, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        // Check volume serial binding (if specified in key file)
        if (!string.IsNullOrEmpty(VolumeSerial) &&
            !string.IsNullOrEmpty(volumeSerial) &&
            !VolumeSerial.Equals(volumeSerial, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates the Ed25519 public key format.
    /// </summary>
    /// <returns>True if public key is valid base64-encoded 32-byte Ed25519 key</returns>
    public bool ValidatePublicKey()
    {
        try
        {
            byte[] keyBytes = Convert.FromBase64String(PublicKey);
            // Ed25519 public keys are exactly 32 bytes
            return keyBytes.Length == 32;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Converts the USB key file back to JSON format.
    /// </summary>
    public string ToJson(bool includeSignature = true)
    {
        if (!includeSignature)
        {
            // Return JSON without signature (for signing)
            return JsonSerializer.Serialize(new
            {
                keyId = KeyId,
                label = Label,
                publicKey = PublicKey,
                issuedAt = IssuedAt,
                expiresAt = ExpiresAt,
                volumeSerial = VolumeSerial
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    }
}
