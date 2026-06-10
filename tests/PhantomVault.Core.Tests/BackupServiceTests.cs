#nullable enable

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using PhantomVault.Core.Services;

namespace PhantomVault.Core.Tests
{
    public class BackupServiceTests : IDisposable
    {
        private readonly string _root;
        private readonly string _manifestPath;
        private readonly string _keyfilePath;
        private readonly string _backupDir;
        private readonly string _restoreDir;
        private readonly byte[] _manifestBytes;
        private const string ManifestFileName = "vault-manifest.json";
        private const string DistinctiveSecret = "PHANTOM_PLAINTEXT_CANARY_8f3a2b";

        public BackupServiceTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "PhantomBackupTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);

            _manifestPath = Path.Combine(_root, ManifestFileName);
            _manifestBytes = Encoding.UTF8.GetBytes("{\"vault\":\"" + DistinctiveSecret + "\",\"entries\":42}");
            File.WriteAllBytes(_manifestPath, _manifestBytes);

            _keyfilePath = Path.Combine(_root, "key.bin");
            File.WriteAllBytes(_keyfilePath, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

            _backupDir = Path.Combine(_root, "backups");
            _restoreDir = Path.Combine(_root, "restored");
            Directory.CreateDirectory(_backupDir);
            Directory.CreateDirectory(_restoreDir);
        }

        private static BackupService CreateService() => new BackupService(new EncryptionService());

        [Fact]
        public async Task Passphrase_Backup_Restore_RoundTrip()
        {
            var svc = CreateService();

            var backupPath = await svc.CreateBackupAsync(_manifestPath, "correct horse", _keyfilePath, _backupDir);
            Assert.True(File.Exists(backupPath));

            // AAD binds to the manifest file name, so the restore destination must share it.
            var restorePath = Path.Combine(_restoreDir, ManifestFileName);
            await svc.RestoreBackupAsync(backupPath, restorePath, "correct horse", _keyfilePath);

            Assert.Equal(_manifestBytes, await File.ReadAllBytesAsync(restorePath));
        }

        [Fact]
        public async Task Passphrase_Backup_DoesNotContainPlaintext()
        {
            var svc = CreateService();
            var backupPath = await svc.CreateBackupAsync(_manifestPath, "correct horse", _keyfilePath, _backupDir);

            var contents = await File.ReadAllTextAsync(backupPath);
            Assert.DoesNotContain(DistinctiveSecret, contents);
        }

        [Fact]
        public async Task Dpapi_Backup_Restore_RoundTrip_NeverPlaintext()
        {
            if (!OperatingSystem.IsWindows())
            {
                return; // DPAPI is Windows-only; the create path falls back to passphrase off-Windows.
            }

            var svc = CreateService();

            // useEncryption: false => DPAPI-sealed, never plaintext.
            var backupPath = await svc.CreateBackupAsync(_manifestPath, "correct horse", _keyfilePath, _backupDir, useEncryption: false);
            Assert.True(File.Exists(backupPath));

            var contents = await File.ReadAllTextAsync(backupPath);
            Assert.Contains("\"protection\":\"dpapi\"", contents);
            Assert.DoesNotContain(DistinctiveSecret, contents);

            var restorePath = Path.Combine(_restoreDir, ManifestFileName);
            await svc.RestoreBackupAsync(backupPath, restorePath, "correct horse", _keyfilePath);

            Assert.Equal(_manifestBytes, await File.ReadAllBytesAsync(restorePath));
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); }
            catch { }
        }
    }
}
