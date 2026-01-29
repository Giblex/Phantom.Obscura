using System;
using System.IO;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services.Security;
using Serilog;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Manages intrusion detection and response for the vault. Tracks
    /// failed unlock attempts, enforces cooldown periods and performs
    /// self‑destruct when enabled. This service operates on the
    /// encrypted manifest; callers must supply the correct
    /// passphrase when writing changes.
    /// </summary>
    public sealed class IntrusionService
    {
        private readonly ManifestService _manifestService;
        private readonly VaultService _vaultService;
        private readonly IDefenceEngine? _defenceEngine;

        public IntrusionService(ManifestService manifestService, VaultService vaultService, IDefenceEngine? defenceEngine = null)
        {
            _manifestService = manifestService;
            _vaultService = vaultService;
            _defenceEngine = defenceEngine;
        }

        /// <summary>
        /// Should be called after an unsuccessful unlock attempt. It
        /// increments the failed attempt counter and either enforces a
        /// cooldown or triggers self‑destruct when thresholds are
        /// reached. If a cooldown is applied the manifest is updated.
        /// </summary>
        /// <param name="manifest">The decrypted manifest instance.</param>
        /// <param name="manifestPath">Absolute path to the encrypted manifest file.</param>
        /// <param name="passphrase">User's passphrase.</param>
        /// <param name="keyfilePath">Optional keyfile used in encryption.</param>
        /// <param name="driveRoot">Root of the removable drive, needed for self‑destruct.</param>
        public void RegisterFailedAttempt(VaultManifest manifest, string manifestPath, string passphrase, string? keyfilePath, string driveRoot)
        {
            manifest.FailedAttempts++;

            // Raise Warning-level threat to Defence Engine for failed login burst
            _defenceEngine?.RaiseThreat(new ThreatEvent(
                ThreatType.FailedLoginBurst,
                ThreatLevel.Warning,
                $"Failed attempt #{manifest.FailedAttempts} detected for vault '{manifest.VaultName}'"
            ));

            if (manifest.FailedAttempts >= manifest.MaxFailedAttempts)
            {
                if (manifest.SelfDestructEnabled)
                {
                    // Raise Critical-level threat before self-destruct
                    _defenceEngine?.RaiseThreat(new ThreatEvent(
                        ThreatType.FailedLoginBurst,
                        ThreatLevel.Critical,
                        $"Maximum failed attempts reached ({manifest.FailedAttempts}/{manifest.MaxFailedAttempts}). Self-destruct triggered for vault '{manifest.VaultName}'"
                    ));

                    // Destroy both manifest and container
                    SelfDestruct(manifest, manifestPath, passphrase, keyfilePath, driveRoot);
                    return;
                }
                else
                {
                    // Raise Critical-level threat for lockout threshold reached
                    _defenceEngine?.RaiseThreat(new ThreatEvent(
                        ThreatType.FailedLoginBurst,
                        ThreatLevel.Critical,
                        $"Maximum failed attempts reached ({manifest.FailedAttempts}/{manifest.MaxFailedAttempts}). Enforcing lockout for vault '{manifest.VaultName}'"
                    ));

                    // Impose a cooldown: lock for 10 minutes per threshold reached
                    int multiplier = manifest.FailedAttempts - manifest.MaxFailedAttempts + 1;
                    TimeSpan lockDuration = TimeSpan.FromMinutes(10 * multiplier);
                    manifest.LockedUntilUtc = DateTimeOffset.UtcNow.Add(lockDuration);
                }
            }
            // Persist updated manifest with incremented counter and optional lockout
            _manifestService.WriteManifest(manifest, manifestPath, passphrase, keyfilePath);
        }

        /// <summary>
        /// Resets the failed attempt counter and clears any lockout on
        /// successful unlock. The updated manifest is persisted.
        /// </summary>
        public void ResetAttempts(VaultManifest manifest, string manifestPath, string passphrase, string? keyfilePath)
        {
            manifest.FailedAttempts = 0;
            manifest.LockedUntilUtc = null;
            _manifestService.WriteManifest(manifest, manifestPath, passphrase, keyfilePath);
        }

        /// <summary>
        /// Performs irreversible destruction of the vault container and
        /// manifest. This method should be used sparingly and only when
        /// self‑destruct is enabled and the failed attempt threshold has
        /// been reached. After wiping, the files cannot be recovered.
        /// </summary>
        private void SelfDestruct(VaultManifest manifest, string manifestPath, string passphrase, string? keyfilePath, string driveRoot)
        {
            try
            {
                // Attempt to dismount the container if mounted
                string containerAbs = Path.Combine(driveRoot, manifest.ContainerPath);
                string mountName = "Vault";
                try
                {
                    // Use a best‑effort dismount; ignore failures
                    // Use GetAwaiter().GetResult() instead of .Wait() to avoid potential deadlocks
                    _vaultService.DismountVaultAsync(mountName).GetAwaiter().GetResult();
                }
                catch
                {
                    // Ignore - best effort cleanup
                }
                // Overwrite container and manifest with zeros before deletion
                SecureDelete(containerAbs);
                SecureDelete(manifestPath);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Secure deletion failed during self-destruct, attempting regular file deletion");

                // Even if secure deletion fails, remove files
                try
                {
                    File.Delete(Path.Combine(driveRoot, manifest.ContainerPath));
                }
                catch (Exception deleteEx)
                {
                    Log.Error(deleteEx, "Failed to delete container file during self-destruct");
                }

                try
                {
                    File.Delete(manifestPath);
                }
                catch (Exception deleteEx)
                {
                    Log.Error(deleteEx, "Failed to delete manifest file during self-destruct");
                }
            }
        }

        /// <summary>
        /// Overwrites a file with zeros then deletes it. Used for
        /// sensitive data destruction. If the file does not exist, no
        /// action is taken.
        /// </summary>
        private static void SecureDelete(string filePath)
        {
            if (!File.Exists(filePath)) return;
            try
            {
                var fi = new FileInfo(filePath);
                int bufferSize = 4096;
                byte[] zeroBuffer = new byte[bufferSize];
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    long remaining = fi.Length;
                    while (remaining > 0)
                    {
                        int toWrite = (int)Math.Min(bufferSize, remaining);
                        fs.Write(zeroBuffer, 0, toWrite);
                        remaining -= toWrite;
                    }
                    fs.Flush(true);
                }
                File.Delete(filePath);
            }
            catch (Exception ex)
            {
                // If secure overwrite fails, fall back to best‑effort deletion
                Log.Warning(ex, "Secure file overwrite failed for {FilePath}, attempting direct deletion", filePath);
                try
                {
                    File.Delete(filePath);
                }
                catch (Exception deleteEx)
                {
                    Log.Error(deleteEx, "Failed to delete file {FilePath} during secure deletion fallback", filePath);
                }
            }
        }
    }
}