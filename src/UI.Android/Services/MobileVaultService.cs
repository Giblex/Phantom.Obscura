using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;

namespace PhantomVault.Android.Services
{
    /// <summary>
    /// Lightweight, mobile-optimised vault service.
    /// Stores a <see cref="VaultDatabase"/> as AES-256-GCM encrypted JSON on disk.
    /// File layout: [4B salt-len][salt][12B nonce][16B tag][ciphertext]
    /// </summary>
    public sealed class MobileVaultService
    {
        private readonly EncryptionService _enc;
        private const string VaultFileName = "phantom.pvmobile";
        private const string PhantomDirName = ".phantom";

        /// <summary>
        /// Drive root currently bound to this vault session (e.g. the USB OTG mount
        /// point selected by <see cref="UsbVaultLocator"/>). When null, the service
        /// has no target drive and all operations fail fast — this is intentional:
        /// Phantom Obscura's mobile model requires a removable drive.
        /// </summary>
        public string? CurrentDriveRoot { get; private set; }

        /// <summary>Path to the encrypted vault file on the currently-bound drive (or null).</summary>
        public string? VaultFilePath =>
            CurrentDriveRoot is null ? null
                : Path.Combine(CurrentDriveRoot, PhantomDirName, VaultFileName);

        public MobileVaultService(EncryptionService enc) => _enc = enc;

        /// <summary>Switches the active drive. Pass null to detach.</summary>
        public void SetDriveRoot(string? driveRoot) => CurrentDriveRoot = driveRoot;

        /// <summary>Returns true when a vault file exists on the currently-bound drive.</summary>
        public bool VaultExists() => VaultFilePath is { } p && File.Exists(p);

        /// <summary>Returns true when a vault exists at the given drive root (no state change).</summary>
        public static bool VaultExistsOn(string driveRoot) =>
            File.Exists(Path.Combine(driveRoot, PhantomDirName, VaultFileName));

        /// <summary>Decrypts and deserialises the vault file.</summary>
        public async Task<VaultDatabase> OpenVaultAsync(string masterPassword, CancellationToken ct = default)
        {
            if (VaultFilePath is null)
                throw new InvalidOperationException("No USB drive bound — connect the Phantom drive and try again.");
            if (!File.Exists(VaultFilePath))
                throw new FileNotFoundException("No vault file found on the connected drive.");

            var raw = await File.ReadAllBytesAsync(VaultFilePath, ct);
            return Deserialize(raw, masterPassword);
        }

        /// <summary>Serialises and encrypts the vault database to disk.</summary>
        public async Task SaveVaultAsync(VaultDatabase db, string masterPassword, CancellationToken ct = default)
        {
            if (VaultFilePath is null)
                throw new InvalidOperationException("No USB drive bound — cannot save vault.");
            Directory.CreateDirectory(Path.GetDirectoryName(VaultFilePath)!);
            var bytes = Serialize(db, masterPassword);
            var tmp = VaultFilePath + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes, ct);
            File.Move(tmp, VaultFilePath, overwrite: true);
        }

        /// <summary>Creates a brand-new empty vault and saves it to disk.</summary>
        public Task CreateVaultAsync(string vaultName, string masterPassword, CancellationToken ct = default)
        {
            var db = new VaultDatabase
            {
                VaultName = vaultName,
                Created = DateTime.UtcNow,
                Groups = new List<VaultGroup>
                {
                    new() { Id = Guid.NewGuid().ToString(), Name = "General", Entries = new List<Credential>() }
                }
            };
            return SaveVaultAsync(db, masterPassword, ct);
        }

        private byte[] Serialize(VaultDatabase db, string password)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(db);
            var salt = _enc.GenerateSalt(32);
            var key = _enc.DeriveKey(password.AsSpan(), salt);
            var result = _enc.Encrypt(json, key, ReadOnlySpan<byte>.Empty);

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(salt.Length);
            bw.Write(salt);
            bw.Write(result.Nonce);
            bw.Write(result.Tag);
            bw.Write(result.Ciphertext);
            return ms.ToArray();
        }

        private VaultDatabase Deserialize(byte[] raw, string password)
        {
            using var ms = new MemoryStream(raw);
            using var br = new BinaryReader(ms);

            var saltLen = br.ReadInt32();
            var salt = br.ReadBytes(saltLen);
            var nonce = br.ReadBytes(12);
            var tag = br.ReadBytes(16);
            var ciphertext = br.ReadBytes((int)(ms.Length - ms.Position));

            var key = _enc.DeriveKey(password.AsSpan(), salt);
            var json = _enc.Decrypt(ciphertext, nonce, tag, key, ReadOnlySpan<byte>.Empty);

            return JsonSerializer.Deserialize<VaultDatabase>(json)
                   ?? throw new InvalidDataException("Vault file is corrupted.");
        }
    }
}
