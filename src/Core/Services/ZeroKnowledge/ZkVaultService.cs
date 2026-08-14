using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GiblexVault.Security.ZK;
using GiblexVault.Security.ZK.Container;
using GiblexVault.Security.ZK.Models;
using GiblexVault.Security.ZK.Primitives;
using GiblexVault.Security.ZK.Util;
using PhantomVault.Core.Security;
using PhantomVault.Core.Utils;

namespace PhantomVault.Core.Services.ZeroKnowledge
{

    public sealed class ZkVaultService : IZkVaultService, IDisposable
    {
        private byte[]? _masterKey;
        private readonly EngineOptions _opts;
        private readonly string _pepperPath;
        private readonly string _verifierPath;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly Dictionary<string, int> _tempFileRefCounts = new();
        private readonly List<SecureTempFile> _tempFiles = new();
        private const string MasterKeyVerifierPlaintext = "PhantomVault.MasterKeyVerifier.v1";
        private static readonly byte[] MasterKeyVerifierAad = Encoding.UTF8.GetBytes(MasterKeyVerifierPlaintext);

        public bool IsUnlocked => _masterKey != null;

        public ZkVaultService(EngineOptions? opts = null)
        {
            _opts = opts ?? new EngineOptions(EncryptionProfile.Advanced);
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhantomVault"
            );
            Directory.CreateDirectory(appDataDir);
            _pepperPath = ResolvePepperPath(appDataDir);
            _verifierPath = Path.Combine(appDataDir, "master.verifier.json");
        }

        // The pepper filename is randomised on first run and stored in a tiny
        // pointer file (`pepper.ref`). This prevents an attacker browsing the
        // AppData folder from identifying the pepper by a predictable name
        // derived from MachineName|UserName. The pointer file itself is named
        // generically; the random target file is named like a cache blob.
        private static string ResolvePepperPath(string appDataDir)
        {
            var pointerPath = Path.Combine(appDataDir, "pepper.ref");
            string fileName;

            if (File.Exists(pointerPath))
            {
                try
                {
                    var raw = File.ReadAllText(pointerPath).Trim();
                    if (IsSafePepperFileName(raw))
                    {
                        return Path.Combine(appDataDir, raw);
                    }
                }
                catch { /* fall through to regenerate */ }
            }

            fileName = GenerateRandomPepperName();
            var newPath = Path.Combine(appDataDir, fileName);

            var legacyDeterministic = Path.Combine(appDataDir, LegacyDerivedPepperFileName());
            var legacyOriginal = Path.Combine(appDataDir, "pepper.dpapi");
            string? sourceLegacy = File.Exists(legacyDeterministic) ? legacyDeterministic
                                 : File.Exists(legacyOriginal) ? legacyOriginal
                                 : null;
            try
            {
                if (sourceLegacy != null && !File.Exists(newPath))
                {
                    File.Move(sourceLegacy, newPath);
                }
                File.WriteAllText(pointerPath, fileName);
            }
            catch
            {
                // If pointer write fails, LoadOrCreatePepperAsync will create a
                // fresh pepper at the chosen path. The user re-enters credentials.
            }

            return newPath;
        }

        private static string GenerateRandomPepperName()
        {
            Span<byte> rnd = stackalloc byte[16];
            RandomNumberGenerator.Fill(rnd);
            return Convert.ToHexString(rnd).ToLowerInvariant() + ".dat";
        }

        private static bool IsSafePepperFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (name.Length > 64) return false;
            if (name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) return false;
            if (name.Contains("..")) return false;
            return true;
        }

        private static string LegacyDerivedPepperFileName()
        {
            var key = $"{Environment.MachineName}|{Environment.UserName}|PhantomVault.Pepper";
            var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key));
            return $"{Convert.ToHexString(hash[..8]).ToLowerInvariant()}.dpapi";
        }

        public async Task<bool> UnlockMasterKeyAsync(string password, string? keyfilePath = null, string? deviceId = null)
        {
            KeyfileGuard.Require(keyfilePath, nameof(keyfilePath));
            await _lock.WaitAsync();

            byte[]? pwdBytes = null;
            byte[]? pepper = null;
            byte[]? keyfileBytes = null;
            byte[]? combined = null;

            try
            {
                if (IsUnlocked)
                {
                    await LockAndWipeKeysAsync();
                }

                bool createdPepper;
                try
                {
                    (pepper, createdPepper) = await LoadOrCreatePepperAsync();
                }
                catch (CryptographicException ex)
                {
                    // The pepper is sealed with platform key protection (DPAPI). A failure
                    // here means the protection blob will not unseal — a moved profile,
                    // a restored machine, a corrupted file — not a wrong credential.
                    // Classify it at the source rather than inferring it later.
                    throw new VaultUnlockOperationException(
                        "The stored key-protection data could not be read on this machine. " +
                        "This is a system-level failure, not an incorrect keyfile or password.", ex);
                }

                pwdBytes = Encoding.UTF8.GetBytes(password);
                keyfileBytes = !string.IsNullOrEmpty(keyfilePath)
                    ? await CompositeKeyfilePath.ReadCombinedBytesAsync(keyfilePath, required: true).ConfigureAwait(false)
                    : null;

                var (salt, createdSalt) = LoadOrCreateSalt();

                // How the Argon2 input is assembled is pinned per vault. Existing vaults
                // keep v1 (raw concatenation) because changing it would change the derived
                // master key and make the vault unopenable. New vaults get v2, which is
                // domain-separated and length-prefixed.
                var kdfInputVersion = ResolveKdfInputVersion(bootstrapping: createdPepper || createdSalt);
                combined = BuildKdfInput(kdfInputVersion, pwdBytes, pepper, keyfileBytes);

                if (!string.IsNullOrEmpty(deviceId))
                {
                    salt = DeviceBinding.DeviceSalt(deviceId, salt);
                }

                var kdfParams = new KdfParams
                {
                    Kdf = "argon2id",
                    Ops = _opts.ArgonOpsLimit,
                    MemMiB = _opts.ArgonMemMiB,
                    Parallelism = _opts.ArgonParallelism,
                    Salt = salt
                };

                _masterKey = Argon2Kdf.DeriveKey(combined, salt, kdfParams);

                if (!await ValidateOrInitializeMasterKeyVerifierAsync(_masterKey, createdPepper || createdSalt, kdfInputVersion).ConfigureAwait(false))
                {
                    CryptographicOperations.ZeroMemory(_masterKey);
                    _masterKey = null;
                    return false;
                }

                return true;
            }
            catch (Exception ex) when (IsOperationalFailure(ex))
            {
                // Operational failures are NOT authentication failures. Returning false
                // here told the user their keyfile was wrong when the real cause was a
                // disconnected USB key, an unreadable file, or a DPAPI failure after a
                // Windows profile change — and burned a throttle attempt doing it.
                // Surfacing these lets the caller show an accurate message.
                if (_masterKey != null)
                {
                    CryptographicOperations.ZeroMemory(_masterKey);
                    _masterKey = null;
                }
                // Already classified at the point of failure — pass it through unchanged
                // so the specific message survives.
                if (ex is VaultUnlockOperationException) throw;

                throw new VaultUnlockOperationException(
                    "The vault could not be opened because of a system or storage error, " +
                    "not because the credentials were wrong.", ex);
            }
            catch (Exception)
            {
                // Genuine authentication failure (bad keyfile/password, failed verifier).
                if (_masterKey != null)
                {
                    CryptographicOperations.ZeroMemory(_masterKey);
                    _masterKey = null;
                }
                return false;
            }
            finally
            {

                if (pwdBytes != null) CryptographicOperations.ZeroMemory(pwdBytes);
                if (pepper != null) CryptographicOperations.ZeroMemory(pepper);
                if (keyfileBytes != null) CryptographicOperations.ZeroMemory(keyfileBytes);
                if (combined != null) CryptographicOperations.ZeroMemory(combined);
                _lock.Release();
            }
        }

        /// <summary>
        /// Distinguishes "the environment failed" from "the credentials were wrong".
        /// Only the latter should count as a failed unlock attempt.
        /// </summary>
        private static bool IsOperationalFailure(Exception ex) => ex switch
        {
            // Already classified at the point of failure (e.g. pepper unseal).
            VaultUnlockOperationException => true,
            IOException => true,
            UnauthorizedAccessException => true,
            System.Security.SecurityException => true,
            OutOfMemoryException => true,
            _ => false
        };

        public async Task<bool> UnlockWithHybridKeyAsync(byte[] hybridDek)
        {
            if (hybridDek == null)
                throw new ArgumentNullException(nameof(hybridDek));
            if (hybridDek.Length != 32)
                throw new ArgumentException("Hybrid DEK must be exactly 32 bytes", nameof(hybridDek));

            await _lock.WaitAsync();
            try
            {
                if (IsUnlocked)
                {
                    await LockAndWipeKeysAsync();
                }

                _masterKey = new byte[32];
                Buffer.BlockCopy(hybridDek, 0, _masterKey, 0, 32);

                return true;
            }
            catch (Exception)
            {
                if (_masterKey != null)
                {
                    CryptographicOperations.ZeroMemory(_masterKey);
                    _masterKey = null;
                }
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<Stream> OpenFileStreamForViewingAsync(string vaultPath, string? fileRelativePath = null, CancellationToken ct = default)
        {
            if (!IsUnlocked)
                throw new InvalidOperationException("Vault is locked. Call UnlockMasterKeyAsync first.");

            await _lock.WaitAsync(ct);
            try
            {
                byte[] decryptedBytes;

                if (string.IsNullOrEmpty(fileRelativePath))
                {

                    decryptedBytes = await VaultFileZk.DecryptToArrayAsync(vaultPath, _masterKey!, _opts).ConfigureAwait(false);
                }
                else
                {

                    decryptedBytes = GvContainerZk.Extract(vaultPath, _masterKey!, _opts, fileRelativePath);
                }

                var ms = new MemoryStream();
                ms.Write(decryptedBytes, 0, decryptedBytes.Length);
                ms.Position = 0;

                CryptographicOperations.ZeroMemory(decryptedBytes);

                return ms;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<Stream> OpenEncryptedStreamForViewingAsync(Stream encryptedVaultStream, CancellationToken ct = default)
        {
            if (!IsUnlocked)
                throw new InvalidOperationException("Vault is locked. Call UnlockMasterKeyAsync first.");
            if (encryptedVaultStream == null || !encryptedVaultStream.CanRead)
                throw new ArgumentException("Encrypted vault stream must be readable", nameof(encryptedVaultStream));

            await _lock.WaitAsync(ct);
            try
            {
                var plaintextStream = new MemoryStream();
                await VaultFileZk.DecryptToStreamAsync(encryptedVaultStream, plaintextStream, _masterKey!, _opts).ConfigureAwait(false);
                plaintextStream.Position = 0;
                return plaintextStream;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<string> ExtractFileToSecureTempAsync(string containerPath, string fileRelativePath, TimeSpan ttl)
        {
            if (!IsUnlocked)
                throw new InvalidOperationException("Vault is locked. Call UnlockMasterKeyAsync first.");

            await _lock.WaitAsync();
            try
            {
                var bytes = GvContainerZk.Extract(containerPath, _masterKey!, _opts, fileRelativePath);

                var appDataTemp = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PhantomVault",
                    "SecureTemp",
                    Guid.NewGuid().ToString("N"));

                Directory.CreateDirectory(appDataTemp);

                if (OperatingSystem.IsWindows())
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(appDataTemp);
                        var acl = dirInfo.GetAccessControl();
                        acl.SetAccessRuleProtection(true, false);

                        var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent();
                        var rule = new System.Security.AccessControl.FileSystemAccessRule(
                            currentUser.User!,
                            System.Security.AccessControl.FileSystemRights.FullControl,
                            System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                            System.Security.AccessControl.PropagationFlags.None,
                            System.Security.AccessControl.AccessControlType.Allow);
                        acl.AddAccessRule(rule);
                        dirInfo.SetAccessControl(acl);
                    }
                    catch (Exception ex)
                    {

                        try { Directory.Delete(appDataTemp, true); } catch { }
                        throw new SecurityException($"Failed to set secure ACL on temp directory: {ex.Message}", ex);
                    }
                }
                else
                {

                    try
                    {
                        File.SetUnixFileMode(appDataTemp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    }
                    catch
                    {

                    }
                }

                var tempFile = Path.Combine(appDataTemp, Path.GetFileName(fileRelativePath));
                await File.WriteAllBytesAsync(tempFile, bytes);

                CryptographicOperations.ZeroMemory(bytes);

                var secureTempFile = new SecureTempFile(tempFile, appDataTemp, DateTimeOffset.UtcNow.Add(ttl));
                _tempFiles.Add(secureTempFile);

                _tempFileRefCounts[tempFile] = _tempFileRefCounts.GetValueOrDefault(tempFile, 0) + 1;

                _ = Task.Run(async () =>
                {
                    await Task.Delay(ttl);

                    await _lock.WaitAsync();
                    try
                    {

                        if (_tempFileRefCounts.ContainsKey(tempFile))
                        {
                            _tempFileRefCounts[tempFile]--;
                            if (_tempFileRefCounts[tempFile] <= 0)
                            {
                                _tempFileRefCounts.Remove(tempFile);

                                try
                                {
                                    SecureDeleteFile(tempFile);
                                    if (Directory.Exists(appDataTemp))
                                    {
                                        Directory.Delete(appDataTemp, true);
                                    }
                                    _tempFiles.Remove(secureTempFile);
                                }
                                catch
                                {

                                }
                            }
                        }
                    }
                    finally
                    {
                        _lock.Release();
                    }
                });

                return tempFile;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<IEnumerable<string>> ListContainerEntriesAsync(string containerPath)
        {
            if (!IsUnlocked)
                throw new InvalidOperationException("Vault is locked. Call UnlockMasterKeyAsync first.");

            return await Task.Run(() =>
            {
                return GvContainerZk.List(containerPath, _masterKey!, _opts);
            });
        }

        public async Task EncryptFileAsync(string plaintextPath, string encryptedOutputPath, CancellationToken ct = default)
        {
            if (!IsUnlocked)
                throw new InvalidOperationException("Vault is locked. Call UnlockMasterKeyAsync first.");

            if (!File.Exists(plaintextPath))
                throw new FileNotFoundException("Plaintext file not found", plaintextPath);

            await VaultFileZk.EncryptAsync(plaintextPath, encryptedOutputPath, _masterKey!, _opts);
        }

        public async Task EncryptStreamAsync(Stream plaintextStream, string encryptedOutputPath, CancellationToken ct = default)
        {
            if (!IsUnlocked)
                throw new InvalidOperationException("Vault is locked. Call UnlockMasterKeyAsync first.");

            if (plaintextStream == null || !plaintextStream.CanRead)
                throw new ArgumentException("Plaintext stream must be readable", nameof(plaintextStream));

            await VaultFileZk.EncryptAsync(plaintextStream, encryptedOutputPath, _masterKey!, _opts).ConfigureAwait(false);
        }

        public async Task EncryptStreamToStreamAsync(Stream plaintextStream, Stream encryptedOutputStream, CancellationToken ct = default)
        {
            if (!IsUnlocked)
                throw new InvalidOperationException("Vault is locked. Call UnlockMasterKeyAsync first.");
            if (plaintextStream == null || !plaintextStream.CanRead)
                throw new ArgumentException("Plaintext stream must be readable", nameof(plaintextStream));
            if (encryptedOutputStream == null || !encryptedOutputStream.CanWrite)
                throw new ArgumentException("Encrypted output stream must be writable", nameof(encryptedOutputStream));

            await VaultFileZk.EncryptToStreamAsync(plaintextStream, encryptedOutputStream, _masterKey!, _opts).ConfigureAwait(false);
            if (encryptedOutputStream.CanSeek)
            {
                encryptedOutputStream.Position = 0;
            }
        }

        public async Task LockAndWipeKeysAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (_masterKey != null)
                {
                    CryptographicOperations.ZeroMemory(_masterKey);
                    _masterKey = null;
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task CleanupOrphanedTempFilesAsync(TimeSpan maxAge)
        {
            await Task.Run(() =>
            {
                var tempRoot = Path.Combine(Path.GetTempPath(), "PhantomVaultTemp");
                if (!Directory.Exists(tempRoot))
                    return;

                var now = DateTime.UtcNow;
                foreach (var sessionDir in Directory.GetDirectories(tempRoot))
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(sessionDir);
                        if (now - dirInfo.CreationTimeUtc > maxAge)
                        {

                            foreach (var file in dirInfo.GetFiles())
                            {
                                SecureDeleteFile(file.FullName);
                            }
                            Directory.Delete(sessionDir, true);
                        }
                    }
                    catch
                    {

                    }
                }
            });
        }

        private void SecureDeleteFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return;

                var fileInfo = new FileInfo(path);
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    var zeros = new byte[stream.Length];
                    stream.Position = 0;
                    stream.Write(zeros, 0, zeros.Length);
                    stream.Flush(true);
                }

                File.Delete(path);
            }
            catch
            {

                try
                {
                    File.Delete(path);
                }
                catch
                {

                }
            }
        }

        private async Task<(byte[] Pepper, bool Created)> LoadOrCreatePepperAsync()
        {
            var appDataDir = Path.GetDirectoryName(_pepperPath)!;
            Directory.CreateDirectory(appDataDir);

            var legacyPepperPath = Path.Combine(appDataDir, "pepper.dpapi");
            if (!File.Exists(_pepperPath) && File.Exists(legacyPepperPath))
            {
                try { File.Move(legacyPepperPath, _pepperPath); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "[ZkVaultService] Failed to migrate legacy pepper file"); }
            }

            if (File.Exists(_pepperPath))
            {
                try
                {
                    var sealedPepper = await File.ReadAllBytesAsync(_pepperPath);
                    return (SecurityTuning.UnsealPepper(sealedPepper), false);
                }
                catch
                {

                }
            }

            var protectedPepper = SecurityTuning.CreatePepperProtected();
            await File.WriteAllBytesAsync(_pepperPath, protectedPepper);
            return (SecurityTuning.UnsealPepper(protectedPepper), true);
        }

        private (byte[] Salt, bool Created) LoadOrCreateSalt()
        {
            var saltPath = Path.Combine(
                Path.GetDirectoryName(_pepperPath)!,
                "master.salt"
            );

            if (File.Exists(saltPath))
            {
                try
                {
                    return (File.ReadAllBytes(saltPath), false);
                }
                catch
                {

                }
            }

            var salt = RandomNumberGenerator.GetBytes(32);
            File.WriteAllBytes(saltPath, salt);
            return (salt, true);
        }

        // ── KDF input assembly ───────────────────────────────────────────────────────
        //
        // v1 concatenated the factors raw: password ‖ pepper ‖ keyfile. With no length
        // prefixes or domain separation that is the classic canonicalization
        // anti-pattern — distinct factor splits can in principle produce the same byte
        // string, and it is fragile against exactly the changes this area keeps seeing
        // (adding a factor, reordering, making the pepper variable-length).
        //
        // v2 prefixes a domain tag and length-prefixes every component, so the mapping
        // from factors to input bytes is injective.
        //
        // The version is per vault and recorded in the verifier: existing vaults must
        // keep deriving the same master key or their data becomes unreadable.
        private const int KdfInputVersionLegacy = 1;
        private const int KdfInputVersionCurrent = 2;
        private static readonly byte[] KdfInputDomainV2 = Encoding.UTF8.GetBytes("PhantomVault.KdfInput.v2");

        private int ResolveKdfInputVersion(bool bootstrapping)
        {
            // A verifier on disk is authoritative — it says how this vault was built.
            if (File.Exists(_verifierPath))
            {
                try
                {
                    var bytes = File.ReadAllBytes(_verifierPath);
                    var record = JsonSerializer.Deserialize<MasterKeyVerifierRecord>(bytes);
                    if (record != null)
                    {
                        return record.KdfInputVersion <= 0 ? KdfInputVersionLegacy : record.KdfInputVersion;
                    }
                }
                catch
                {
                    // Unreadable verifier: fall through. Validation will fail anyway and
                    // report a bad unlock rather than silently deriving a different key.
                }
                return KdfInputVersionLegacy;
            }

            // No verifier. If we just created the pepper or salt this is a brand-new
            // vault and can use the current scheme; otherwise it predates verifiers
            // entirely and must stay on the legacy assembly.
            return bootstrapping ? KdfInputVersionCurrent : KdfInputVersionLegacy;
        }

        private static byte[] BuildKdfInput(int version, byte[] password, byte[] pepper, byte[]? keyfile)
        {
            var kf = keyfile ?? Array.Empty<byte>();

            if (version == KdfInputVersionLegacy)
            {
                var legacy = new byte[password.Length + pepper.Length + kf.Length];
                Buffer.BlockCopy(password, 0, legacy, 0, password.Length);
                Buffer.BlockCopy(pepper, 0, legacy, password.Length, pepper.Length);
                if (kf.Length > 0)
                {
                    Buffer.BlockCopy(kf, 0, legacy, password.Length + pepper.Length, kf.Length);
                }
                return legacy;
            }

            // v2: domain ‖ len(pwd) ‖ pwd ‖ len(pepper) ‖ pepper ‖ len(keyfile) ‖ keyfile
            var total = KdfInputDomainV2.Length + 4 + password.Length + 4 + pepper.Length + 4 + kf.Length;
            var buffer = new byte[total];
            var offset = 0;

            Buffer.BlockCopy(KdfInputDomainV2, 0, buffer, offset, KdfInputDomainV2.Length);
            offset += KdfInputDomainV2.Length;

            offset = AppendLengthPrefixed(buffer, offset, password);
            offset = AppendLengthPrefixed(buffer, offset, pepper);
            _ = AppendLengthPrefixed(buffer, offset, kf);

            return buffer;
        }

        private static int AppendLengthPrefixed(byte[] destination, int offset, byte[] value)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
                destination.AsSpan(offset, 4), (uint)value.Length);
            offset += 4;

            if (value.Length > 0)
            {
                Buffer.BlockCopy(value, 0, destination, offset, value.Length);
                offset += value.Length;
            }
            return offset;
        }

        private async Task<bool> ValidateOrInitializeMasterKeyVerifierAsync(byte[] masterKey, bool allowBootstrap, int kdfInputVersion)
        {
            if (File.Exists(_verifierPath))
            {
                return await ValidateMasterKeyVerifierAsync(masterKey).ConfigureAwait(false);
            }

            if (!allowBootstrap)
            {

                return true;
            }

            await WriteMasterKeyVerifierAsync(masterKey, kdfInputVersion).ConfigureAwait(false);
            return true;
        }

        private async Task<bool> ValidateMasterKeyVerifierAsync(byte[] masterKey)
        {
            try
            {
                var verifierBytes = await File.ReadAllBytesAsync(_verifierPath).ConfigureAwait(false);
                var verifier = JsonSerializer.Deserialize<MasterKeyVerifierRecord>(verifierBytes);
                if (verifier == null)
                {
                    return false;
                }

                var nonce = Convert.FromBase64String(verifier.Nonce);
                var ciphertext = Convert.FromBase64String(verifier.Ciphertext);
                var plaintext = Aead.Decrypt(CipherSuite.Aes256Gcm, masterKey, nonce, MasterKeyVerifierAad, ciphertext);
                try
                {
                    return CryptographicOperations.FixedTimeEquals(
                        plaintext,
                        Encoding.UTF8.GetBytes(MasterKeyVerifierPlaintext));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                }
            }
            catch
            {
                return false;
            }
        }

        private async Task WriteMasterKeyVerifierAsync(byte[] masterKey, int kdfInputVersion)
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var plaintext = Encoding.UTF8.GetBytes(MasterKeyVerifierPlaintext);
            try
            {
                var ciphertext = Aead.Encrypt(CipherSuite.Aes256Gcm, masterKey, nonce, MasterKeyVerifierAad, plaintext);
                try
                {
                    var verifier = new MasterKeyVerifierRecord(
                        Convert.ToBase64String(nonce),
                        Convert.ToBase64String(ciphertext),
                        kdfInputVersion);
                    var json = JsonSerializer.SerializeToUtf8Bytes(verifier);
                    await File.WriteAllBytesAsync(_verifierPath, json).ConfigureAwait(false);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(ciphertext);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
                CryptographicOperations.ZeroMemory(nonce);
            }
        }

        public void Dispose()
        {

            foreach (var tempFile in _tempFiles.ToArray())
            {
                try
                {
                    if (File.Exists(tempFile.FilePath))
                    {
                        SecureDeleteFile(tempFile.FilePath);
                    }
                    if (Directory.Exists(tempFile.DirectoryPath))
                    {
                        Directory.Delete(tempFile.DirectoryPath, true);
                    }
                }
                catch
                {

                }
            }
            _tempFiles.Clear();
            _tempFileRefCounts.Clear();

            LockAndWipeKeysAsync().GetAwaiter().GetResult();
            _lock?.Dispose();
        }
    }

    internal sealed record SecureTempFile(string FilePath, string DirectoryPath, DateTimeOffset ExpiresAt);
    /// <summary>
    /// Master-key verifier. <c>KdfInputVersion</c> records how the Argon2 input was
    /// assembled for this vault so the derivation stays reproducible across upgrades —
    /// absent means version 1 (pre-versioning vaults).
    /// </summary>
    internal sealed record MasterKeyVerifierRecord(string Nonce, string Ciphertext, int KdfInputVersion = 1);

    /// <summary>
    /// Raised when unlocking fails for an environmental reason — storage, permissions,
    /// memory, or platform key protection — rather than because the supplied credentials
    /// were wrong. Callers should report this distinctly and must not count it as a
    /// failed unlock attempt against the throttle.
    /// </summary>
    public sealed class VaultUnlockOperationException : Exception
    {
        public VaultUnlockOperationException(string message, Exception inner) : base(message, inner) { }
    }
}

