using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhantomVault.Core.Models.Security;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Host-local persistence for <see cref="VolumeTrustAnchor"/> records. Anchors are
    /// stored under <c>%AppData%\PhantomVault\anchors\</c> and DPAPI-protected to the
    /// current user, so they live OFF the USB and cannot be rolled back alongside it.
    /// </summary>
    public sealed class VolumeTrustAnchorStore
    {
        private readonly string _anchorDirectory;

        public VolumeTrustAnchorStore(string? anchorDirectory = null)
        {
            _anchorDirectory = anchorDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhantomVault",
                "anchors");
        }

        public long? TryReadSequence(string vaultId)
        {
            if (string.IsNullOrWhiteSpace(vaultId)) return null;

            try
            {
                var path = GetAnchorPath(vaultId);
                if (!File.Exists(path)) return null;

                var protectedBytes = File.ReadAllBytes(path);
                var json = Unprotect(protectedBytes);
                var anchor = JsonSerializer.Deserialize<VolumeTrustAnchor>(json);
                return anchor?.SaveSequence;
            }
            catch
            {
                // A corrupt or undecryptable anchor must not block unlock; treat as absent.
                return null;
            }
        }

        public void Write(string vaultId, long sequence)
        {
            if (string.IsNullOrWhiteSpace(vaultId)) return;

            try
            {
                Directory.CreateDirectory(_anchorDirectory);

                var anchor = new VolumeTrustAnchor
                {
                    VaultId = vaultId,
                    SaveSequence = sequence,
                    UpdatedUtc = DateTimeOffset.UtcNow
                };

                var json = JsonSerializer.Serialize(anchor);
                var protectedBytes = Protect(json);
                File.WriteAllBytes(GetAnchorPath(vaultId), protectedBytes);
            }
            catch
            {
                // Best-effort: failing to persist the anchor degrades rollback detection
                // but must never break a save.
            }
        }

        private string GetAnchorPath(string vaultId)
        {
            // vaultId is already a SHA-256 hex string, safe as a filename.
            return Path.Combine(_anchorDirectory, vaultId + ".bin");
        }

        private static byte[] Protect(string json)
        {
            var plain = Encoding.UTF8.GetBytes(json);
            if (OperatingSystem.IsWindows())
            {
                return ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            }
            return plain;
        }

        private static string Unprotect(byte[] stored)
        {
            if (OperatingSystem.IsWindows())
            {
                var plain = ProtectedData.Unprotect(stored, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            return Encoding.UTF8.GetString(stored);
        }
    }
}
