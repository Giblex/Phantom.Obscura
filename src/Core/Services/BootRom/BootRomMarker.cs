using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Services.BootRom
{
    /// <summary>
    /// The on-USB record that says a vault is Boot ROM bound, and carries the public parameters
    /// needed before anything is decrypted: which ROM identity sealed the image, the lowest ROM
    /// version accepted (rollback floor), and the salts for key derivation and recovery escrow.
    ///
    /// Nothing here is secret, and nothing here is trusted on its own. Tampering with it cannot
    /// grant access — the derived key simply comes out wrong and the vault stays shut — so the
    /// worst an attacker achieves by editing it is denial of service. Its hash is recorded inside
    /// the encrypted manifest, so substitution is also detectable once a vault does open.
    /// </summary>
    public sealed class BootRomMarker
    {
        public const string FileName = "boot.marker.json";
        public const string RomFileName = "phantom.rom";
        public const int CurrentVersion = 1;

        [JsonPropertyName("version")]
        public int Version { get; set; } = CurrentVersion;

        /// <summary>Identifies which ROM identity sealed the image on this device.</summary>
        [JsonPropertyName("keyId")]
        public string KeyIdBase64 { get; set; } = string.Empty;

        /// <summary>Lowest ROM version this vault accepts; blocks rollback to a withdrawn image.</summary>
        [JsonPropertyName("minRomVersion")]
        public ulong MinRomVersion { get; set; }

        /// <summary>Salt for deriving the ROM decryption key from the vault's keyfile material.</summary>
        [JsonPropertyName("salt")]
        public string SaltBase64 { get; set; } = string.Empty;

        /// <summary>Ed25519 public key the ROM image is expected to be signed with.</summary>
        [JsonPropertyName("signingPublicKey")]
        public string SigningPublicKeyBase64 { get; set; } = string.Empty;

        /// <summary>Salt for the recovery-code escrow of the ROM contribution.</summary>
        [JsonPropertyName("escrowSalt")]
        public string EscrowSaltBase64 { get; set; } = string.Empty;

        /// <summary>The <c>.phantom/boot</c> folder on a vault drive.</summary>
        public static string BootDirectory(string usbRoot) => Path.Combine(usbRoot, ".phantom", "boot");

        public static string MarkerPath(string usbRoot) => Path.Combine(BootDirectory(usbRoot), FileName);

        public static string RomPath(string usbRoot) => Path.Combine(BootDirectory(usbRoot), RomFileName);

        /// <summary>The escrow blob, written beside the marker when binding is enabled.</summary>
        public static string EscrowPath(string usbRoot) => Path.Combine(BootDirectory(usbRoot), "boot.escrow");

        /// <summary>Reads the marker for a drive, or null when the vault is not ROM bound.</summary>
        public static BootRomMarker? TryLoad(string usbRoot)
        {
            if (string.IsNullOrWhiteSpace(usbRoot))
                return null;

            string path = MarkerPath(usbRoot);
            try
            {
                if (!File.Exists(path))
                    return null;

                var marker = JsonSerializer.Deserialize<BootRomMarker>(File.ReadAllBytes(path));
                if (marker is null || marker.Version != CurrentVersion)
                    return null;
                if (string.IsNullOrWhiteSpace(marker.KeyIdBase64) ||
                    string.IsNullOrWhiteSpace(marker.SaltBase64) ||
                    string.IsNullOrWhiteSpace(marker.SigningPublicKeyBase64))
                    return null;

                return marker;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Unreadable marker is treated as "not bound"; unlock then fails closed on the
                // derived key rather than on a parse error.
                return null;
            }
        }

        public void Save(string usbRoot)
        {
            Directory.CreateDirectory(BootDirectory(usbRoot));
            File.WriteAllBytes(MarkerPath(usbRoot), JsonSerializer.SerializeToUtf8Bytes(this));
        }

        /// <summary>Canonical bytes used for the hash pinned inside the encrypted manifest.</summary>
        public byte[] CanonicalBytes() => JsonSerializer.SerializeToUtf8Bytes(this);
    }
}
