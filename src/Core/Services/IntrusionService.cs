using System;
using System.IO;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services.Security;
using PhantomVault.Core.Utils;
using Serilog;

namespace PhantomVault.Core.Services
{

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

        public void RegisterFailedAttempt(VaultManifest manifest, string manifestPath, string passphrase, string? keyfilePath, string driveRoot)
        {
            manifest.FailedAttempts++;

            _defenceEngine?.RaiseThreat(new ThreatEvent(
                ThreatType.FailedLoginBurst,
                ThreatLevel.Warning,
                $"Failed attempt #{manifest.FailedAttempts} detected for vault '{manifest.VaultName}'"
            ));

            if (manifest.FailedAttempts >= manifest.MaxFailedAttempts)
            {
                if (manifest.SelfDestructEnabled)
                {

                    _defenceEngine?.RaiseThreat(new ThreatEvent(
                        ThreatType.FailedLoginBurst,
                        ThreatLevel.Critical,
                        $"Maximum failed attempts reached ({manifest.FailedAttempts}/{manifest.MaxFailedAttempts}). Self-destruct triggered for vault '{manifest.VaultName}'"
                    ));

                    SelfDestruct(manifest, manifestPath, passphrase, keyfilePath, driveRoot);
                    return;
                }
                else
                {

                    _defenceEngine?.RaiseThreat(new ThreatEvent(
                        ThreatType.FailedLoginBurst,
                        ThreatLevel.Critical,
                        $"Maximum failed attempts reached ({manifest.FailedAttempts}/{manifest.MaxFailedAttempts}). Enforcing lockout for vault '{manifest.VaultName}'"
                    ));

                    int multiplier = manifest.FailedAttempts - manifest.MaxFailedAttempts + 1;
                    TimeSpan lockDuration = TimeSpan.FromMinutes(10 * multiplier);
                    manifest.LockedUntilUtc = DateTimeOffset.UtcNow.Add(lockDuration);
                }
            }

            using var sp = SecurePassword.FromString(passphrase);
            _manifestService.WriteManifestSecure(manifest, manifestPath, sp, keyfilePath);
        }

        public void ResetAttempts(VaultManifest manifest, string manifestPath, string passphrase, string? keyfilePath)
        {
            manifest.FailedAttempts = 0;
            manifest.LockedUntilUtc = null;
            using var sp = SecurePassword.FromString(passphrase);
            _manifestService.WriteManifestSecure(manifest, manifestPath, sp, keyfilePath);
        }

        private void SelfDestruct(VaultManifest manifest, string manifestPath, string passphrase, string? keyfilePath, string driveRoot)
        {
            try
            {

                string containerAbs = Path.Combine(driveRoot, manifest.ContainerPath);
                string mountName = "Vault";
                try
                {

                    _vaultService.DismountVaultAsync(mountName).GetAwaiter().GetResult();
                }
                catch
                {

                }

                SecureDelete(containerAbs);
                SecureDelete(manifestPath);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Secure deletion failed during self-destruct, attempting regular file deletion");

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

