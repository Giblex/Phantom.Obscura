using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services
{

    [SupportedOSPlatform("windows")]
    public sealed class PasskeyService : IPasskeyService
    {
        private const string CredentialStorageFolder = "PhantomVault";
        private const string CredentialStorageFile = "passkey_keys.bin";

        private readonly Lazy<WindowsHelloBridge?> _windowsHelloLazy;

        public PasskeyService()
        {
            _windowsHelloLazy = new Lazy<WindowsHelloBridge?>(
                WindowsHelloBridge.TryCreate,
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        private WindowsHelloBridge? WindowsHello => _windowsHelloLazy.Value;

        public bool IsSupported => WindowsHello?.IsSupported ?? false;

        public bool IsBiometricAvailable => WindowsHello?.IsBiometricAvailable ?? false;

        public string AuthenticatorDescription
        {
            get
            {
                if (IsSupported)
                {
                    return "Windows Hello local authenticator (fingerprint, face, or PIN)";
                }

                if (OperatingSystem.IsMacOS())
                {
                    return "Platform passkeys are not available in this build on macOS";
                }

                if (OperatingSystem.IsLinux())
                {
                    return "Platform passkeys are not available in this build on Linux";
                }

                if (OperatingSystem.IsWindows())
                {
                    return "Windows Hello is unavailable on this device or in the current session";
                }

                return "Platform authenticator not available";
            }
        }

        public async Task<byte[]> RegisterAsync(string userId, string userName, string rpId, byte[] challenge)
        {
            ValidateRegistrationArguments(userId, userName, rpId, challenge);

            var bridge = WindowsHello ?? throw new PlatformNotSupportedException("Windows Hello is not available on this device.");
            await bridge.EnsureAvailableAsync().ConfigureAwait(false);

            var verified = await bridge.PromptForVerificationAsync(
                $"Confirm Windows Hello enrollment for {userName}.").ConfigureAwait(false);

            if (!verified)
            {
                throw new InvalidOperationException("Windows Hello verification was declined or failed.");
            }

            var credentialId = new byte[32];
            RandomNumberGenerator.Fill(credentialId);

            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var publicKey = ecdsa.ExportSubjectPublicKeyInfo();
            var privateKey = ecdsa.ExportECPrivateKey();

            await StoreCredentialKeyAsync(credentialId, rpId, userId, publicKey, privateKey).ConfigureAwait(false);

            return credentialId;
        }

        private readonly System.Collections.Generic.HashSet<string> _rpAllowlist = new(StringComparer.OrdinalIgnoreCase);

        public void AddToRpAllowlist(string rpId)
        {
            if (!string.IsNullOrWhiteSpace(rpId))
            {
                lock (_rpAllowlist)
                {
                    _rpAllowlist.Add(rpId.ToLowerInvariant());
                }
            }
        }

        public void RemoveFromRpAllowlist(string rpId)
        {
            if (!string.IsNullOrWhiteSpace(rpId))
            {
                lock (_rpAllowlist)
                {
                    _rpAllowlist.Remove(rpId.ToLowerInvariant());
                }
            }
        }

        public void ClearRpAllowlist()
        {
            lock (_rpAllowlist)
            {
                _rpAllowlist.Clear();
            }
        }

        public bool IsRpAllowed(string rpId)
        {
            if (string.IsNullOrWhiteSpace(rpId))
                return false;

            lock (_rpAllowlist)
            {
                return _rpAllowlist.Contains(rpId.ToLowerInvariant());
            }
        }

        public async Task<bool> AuthenticateAsync(byte[] credentialId, string rpId, byte[] challenge)
        {
            if (credentialId == null || credentialId.Length == 0)
            {
                throw new ArgumentException("Credential ID cannot be empty", nameof(credentialId));
            }

            if (string.IsNullOrWhiteSpace(rpId))
            {
                throw new ArgumentException("Relying party ID cannot be empty", nameof(rpId));
            }

            if (challenge == null || challenge.Length == 0)
            {
                throw new ArgumentException("Challenge cannot be empty", nameof(challenge));
            }

            lock (_rpAllowlist)
            {
                if (_rpAllowlist.Count == 0)
                {
                    throw new UnauthorizedAccessException(
                        $"Relying party '{rpId}' is not in the allowlist. No RPs are currently allowed. " +
                        "Add trusted relying parties to the allowlist first.");
                }

                if (!_rpAllowlist.Contains(rpId.ToLowerInvariant()))
                {
                    throw new UnauthorizedAccessException(
                        $"Relying party '{rpId}' is not in the allowlist. " +
                        "This may be a phishing attempt. Only authenticate with trusted relying parties.");
                }
            }

            var bridge = WindowsHello ?? throw new PlatformNotSupportedException("Windows Hello is not available on this device.");
            await bridge.EnsureAvailableAsync().ConfigureAwait(false);

            var userVerified = await bridge.PromptForVerificationAsync(
                $"Sign in to: {rpId}\n\nVerify this is the site you intended to sign in to.").ConfigureAwait(false);

            if (!userVerified)
            {
                return false;
            }

            var storedCredential = await LoadCredentialKeyAsync(credentialId).ConfigureAwait(false);
            if (storedCredential == null)
            {
                throw new UnauthorizedAccessException(
                    "No matching credential found for the provided credential ID. " +
                    "The credential may have been deleted or never registered.");
            }

            if (!string.Equals(storedCredential.Value.RpId, rpId, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException(
                    $"Relying party ID mismatch. " +
                    $"Credential was registered for '{storedCredential.Value.RpId}' but request is for '{rpId}'. " +
                    "This may be a credential substitution attack. Authentication denied.");
            }

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportECPrivateKey(storedCredential.Value.PrivateKey, out _);

            var rpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(rpId));
            var authenticatorData = new byte[rpIdHash.Length + 1 + 4];
            rpIdHash.CopyTo(authenticatorData, 0);
            authenticatorData[rpIdHash.Length] = 0x01;

            var clientDataHash = SHA256.HashData(challenge);
            var signedData = new byte[authenticatorData.Length + clientDataHash.Length];
            authenticatorData.CopyTo(signedData, 0);
            clientDataHash.CopyTo(signedData, authenticatorData.Length);

            var signature = ecdsa.SignData(signedData, HashAlgorithmName.SHA256);

            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(storedCredential.Value.PublicKey, out _);

            return verifier.VerifyData(signedData, signature, HashAlgorithmName.SHA256);
        }

        private static void ValidateRegistrationArguments(string userId, string userName, string rpId, byte[] challenge)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("User ID cannot be empty", nameof(userId));
            }

            if (string.IsNullOrWhiteSpace(userName))
            {
                throw new ArgumentException("User name cannot be empty", nameof(userName));
            }

            if (string.IsNullOrWhiteSpace(rpId))
            {
                throw new ArgumentException("Relying party ID cannot be empty", nameof(rpId));
            }

            if (challenge == null || challenge.Length == 0)
            {
                throw new ArgumentException("Challenge cannot be empty", nameof(challenge));
            }
        }

        private static Task StoreCredentialKeyAsync(byte[] credentialId, string rpId, string userId, byte[] publicKey, byte[] privateKey)
        {
            var storagePath = GetCredentialStoragePath();
            var directory = System.IO.Path.GetDirectoryName(storagePath);
            if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            var credentialIdB64 = Convert.ToBase64String(credentialId);
            var publicKeyB64 = Convert.ToBase64String(publicKey);
            var privateKeyB64 = Convert.ToBase64String(privateKey);
            var data = $"{credentialIdB64}|{rpId}|{userId}|{publicKeyB64}|{privateKeyB64}";
            var plainBytes = Encoding.UTF8.GetBytes(data);

            var protectedData = System.Security.Cryptography.ProtectedData.Protect(
                plainBytes,
                Encoding.UTF8.GetBytes("PhantomVault.Passkey.Entropy"),
                System.Security.Cryptography.DataProtectionScope.CurrentUser);

            System.IO.File.WriteAllBytes(storagePath, protectedData);

            Array.Clear(plainBytes, 0, plainBytes.Length);

            return Task.CompletedTask;
        }

        private static Task<StoredCredential?> LoadCredentialKeyAsync(byte[] credentialId)
        {
            var storagePath = GetCredentialStoragePath();
            if (!System.IO.File.Exists(storagePath))
            {
                return Task.FromResult<StoredCredential?>(null);
            }

            try
            {
                var protectedData = System.IO.File.ReadAllBytes(storagePath);
                var plainBytes = System.Security.Cryptography.ProtectedData.Unprotect(
                    protectedData,
                    Encoding.UTF8.GetBytes("PhantomVault.Passkey.Entropy"),
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);

                var data = Encoding.UTF8.GetString(plainBytes);
                var parts = data.Split('|');

                if (parts.Length != 5)
                {
                    return Task.FromResult<StoredCredential?>(null);
                }

                var storedCredentialId = Convert.FromBase64String(parts[0]);

                if (!CryptographicOperations.FixedTimeEquals(credentialId, storedCredentialId))
                {
                    return Task.FromResult<StoredCredential?>(null);
                }

                var credential = new StoredCredential
                {
                    CredentialId = storedCredentialId,
                    RpId = parts[1],
                    UserId = parts[2],
                    PublicKey = Convert.FromBase64String(parts[3]),
                    PrivateKey = Convert.FromBase64String(parts[4])
                };

                Array.Clear(plainBytes, 0, plainBytes.Length);

                return Task.FromResult<StoredCredential?>(credential);
            }
            catch
            {
                return Task.FromResult<StoredCredential?>(null);
            }
        }

        private static byte[] GenerateChallenge()
        {
            var challenge = new byte[32];
            RandomNumberGenerator.Fill(challenge);
            return challenge;
        }

        private async Task<PasskeyCredential> CreateCredentialAsync(string relyingParty, string userId)
        {

            var credentialId = new byte[32];
            RandomNumberGenerator.Fill(credentialId);

            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var publicKey = ecdsa!.ExportSubjectPublicKeyInfo();
            var privateKey = ecdsa.ExportECPrivateKey();

            await StoreCredentialKeyAsync(credentialId, relyingParty, userId, publicKey, privateKey).ConfigureAwait(false);

            Array.Clear(privateKey, 0, privateKey.Length);

            return new PasskeyCredential
            {
                CredentialId = credentialId,
                PublicKey = publicKey,
                CreatedAt = DateTimeOffset.UtcNow,
                Name = $"Windows Hello ({Environment.MachineName})"
            };
        }

        private async Task<byte[]?> SignChallengeAsync(byte[] credentialId, byte[] challenge)
        {
            try
            {
                var credential = await LoadCredentialKeyAsync(credentialId).ConfigureAwait(false);
                if (credential == null)
                    return null;

                var storedCred = credential.Value;
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportECPrivateKey(storedCred.PrivateKey, out _);
                return ecdsa.SignData(challenge, HashAlgorithmName.SHA256);
            }
            catch
            {
                return null;
            }
        }

        public async Task<PasskeyCredential> RotateCredentialAsync(byte[] oldCredentialId, string relyingParty, string userId)
        {

            var storagePath = GetCredentialStoragePath();
            if (System.IO.File.Exists(storagePath))
            {
                BackupCredentialFile(storagePath);
            }

            var newCredential = await CreateCredentialAsync(relyingParty, userId);

            var testChallenge = GenerateChallenge();
            var testSignature = await SignChallengeAsync(newCredential.CredentialId, testChallenge);

            if (testSignature == null || testSignature.Length == 0)
            {
                throw new InvalidOperationException("New credential verification failed - unable to generate signature");
            }

            if (!VerifySignature(newCredential.PublicKey, testChallenge, testSignature))
            {

                RestoreCredentialBackup(storagePath);
                throw new InvalidOperationException("New credential verification failed - signature validation failed");
            }

            CleanupOldBackups();

            return newCredential;
        }

        public Task DeleteCredentialAsync(byte[] credentialId)
        {
            var storagePath = GetCredentialStoragePath();
            if (System.IO.File.Exists(storagePath))
            {

                var credential = LoadCredentialKeyAsync(credentialId).Result;
                if (credential != null)
                {

                    SecureFileDelete(storagePath);
                }
            }
            return Task.CompletedTask;
        }

        private static void BackupCredentialFile(string sourcePath)
        {
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            var backupPath = $"{sourcePath}.backup.{timestamp}";

            try
            {
                System.IO.File.Copy(sourcePath, backupPath, overwrite: false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to backup credential file: {ex.Message}", ex);
            }
        }

        private static void RestoreCredentialBackup(string targetPath)
        {
            var directory = System.IO.Path.GetDirectoryName(targetPath);
            if (string.IsNullOrEmpty(directory))
                return;

            var backupFiles = System.IO.Directory.GetFiles(directory, "passkey_keys.bin.backup.*")
                .OrderByDescending(f => f)
                .ToList();

            if (backupFiles.Count > 0)
            {
                var mostRecentBackup = backupFiles[0];
                System.IO.File.Copy(mostRecentBackup, targetPath, overwrite: true);
            }
        }

        private static void CleanupOldBackups()
        {
            var storagePath = GetCredentialStoragePath();
            var directory = System.IO.Path.GetDirectoryName(storagePath);
            if (string.IsNullOrEmpty(directory) || !System.IO.Directory.Exists(directory))
                return;

            var backupFiles = System.IO.Directory.GetFiles(directory, "passkey_keys.bin.backup.*")
                .OrderByDescending(f => f)
                .Skip(5)
                .ToList();

            foreach (var oldBackup in backupFiles)
            {
                try
                {
                    SecureFileDelete(oldBackup);
                }
                catch
                {

                }
            }
        }

        private static void SecureFileDelete(string filePath)
        {
            try
            {
                var fileInfo = new System.IO.FileInfo(filePath);
                var fileLength = fileInfo.Length;

                using (var fs = System.IO.File.OpenWrite(filePath))
                {
                    var buffer = new byte[4096];
                    long written = 0;
                    while (written < fileLength)
                    {
                        var toWrite = (int)Math.Min(buffer.Length, fileLength - written);
                        RandomNumberGenerator.Fill(buffer.AsSpan(0, toWrite));
                        fs.Write(buffer, 0, toWrite);
                        written += toWrite;
                    }
                    fs.Flush();
                }

                System.IO.File.Delete(filePath);
            }
            catch
            {

                try
                {
                    System.IO.File.Delete(filePath);
                }
                catch
                {

                }
            }
        }

        private static bool VerifySignature(byte[] publicKey, byte[] challenge, byte[] signature)
        {
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
                return ecdsa.VerifyData(challenge, signature, HashAlgorithmName.SHA256);
            }
            catch
            {
                return false;
            }
        }

        private static string GetCredentialStoragePath()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return System.IO.Path.Combine(appDataPath, CredentialStorageFolder, CredentialStorageFile);
        }

        private struct StoredCredential
        {
            public byte[] CredentialId;
            public string RpId;
            public string UserId;
            public byte[] PublicKey;
            public byte[] PrivateKey;
        }

        private sealed class WindowsHelloBridge
        {
            private const string VerifierTypeName = "Windows.Security.Credentials.UI.UserConsentVerifier, Windows, ContentType=WindowsRuntime";
            private const string AvailabilityEnumName = "Windows.Security.Credentials.UI.UserConsentVerifierAvailability, Windows, ContentType=WindowsRuntime";
            private const string VerificationResultEnumName = "Windows.Security.Credentials.UI.UserConsentVerificationResult, Windows, ContentType=WindowsRuntime";

            private readonly Type _verifierType;
            private readonly MethodInfo _checkAvailabilityAsync;
            private readonly MethodInfo _requestVerificationAsync;
            private readonly MethodInfo _asTaskGeneric;
            private readonly int _availabilityAvailable;
            private readonly int _availabilityDeviceBusy;
            private readonly int _verificationVerified;
            private readonly Lazy<Task<int>> _availabilityProbe;

            private WindowsHelloBridge(
                Type verifierType,
                MethodInfo checkAvailabilityAsync,
                MethodInfo requestVerificationAsync,
                MethodInfo asTaskGeneric,
                int availabilityAvailable,
                int availabilityDeviceBusy,
                int verificationVerified)
            {
                _verifierType = verifierType;
                _checkAvailabilityAsync = checkAvailabilityAsync;
                _requestVerificationAsync = requestVerificationAsync;
                _asTaskGeneric = asTaskGeneric;
                _availabilityAvailable = availabilityAvailable;
                _availabilityDeviceBusy = availabilityDeviceBusy;
                _verificationVerified = verificationVerified;
                _availabilityProbe = new Lazy<Task<int>>(() => QueryAvailabilityAsync(), LazyThreadSafetyMode.ExecutionAndPublication);
            }

            public bool IsSupported
            {
                get
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        return false;
                    }

                    var availability = GetAvailabilitySafeAsync().GetAwaiter().GetResult();
                    return availability == _availabilityAvailable || availability == _availabilityDeviceBusy;
                }
            }

            public bool IsBiometricAvailable
            {
                get
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        return false;
                    }

                    var availability = GetAvailabilitySafeAsync().GetAwaiter().GetResult();
                    return availability == _availabilityAvailable;
                }
            }

            public static WindowsHelloBridge? TryCreate()
            {
                if (!OperatingSystem.IsWindowsVersionAtLeast(10))
                {
                    return null;
                }

                try
                {
                    var verifierType = Type.GetType(VerifierTypeName, throwOnError: true) ?? throw new InvalidOperationException("UserConsentVerifier type not found.");
                    var availabilityEnum = Type.GetType(AvailabilityEnumName, throwOnError: true) ?? throw new InvalidOperationException("UserConsentVerifierAvailability type not found.");
                    var verificationEnum = Type.GetType(VerificationResultEnumName, throwOnError: true) ?? throw new InvalidOperationException("UserConsentVerificationResult type not found.");

                    var checkAvailabilityAsync = verifierType.GetMethod("CheckAvailabilityAsync", BindingFlags.Public | BindingFlags.Static) ?? throw new InvalidOperationException("CheckAvailabilityAsync method not found.");
                    var requestVerificationAsync = verifierType.GetMethod("RequestVerificationAsync", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string) }) ?? throw new InvalidOperationException("RequestVerificationAsync method not found.");

                    var asTaskGeneric = typeof(WindowsRuntimeSystemExtensions)
                        .GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "AsTask" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
                        ?? throw new InvalidOperationException("WindowsRuntimeSystemExtensions.AsTask<T> overload not found.");

                    int availabilityAvailable = GetEnumValue(availabilityEnum, "Available");
                    int availabilityDeviceBusy = GetEnumValue(availabilityEnum, "DeviceBusy");
                    int verificationVerified = GetEnumValue(verificationEnum, "Verified");

                    return new WindowsHelloBridge(
                        verifierType,
                        checkAvailabilityAsync,
                        requestVerificationAsync,
                        asTaskGeneric,
                        availabilityAvailable,
                        availabilityDeviceBusy,
                        verificationVerified);
                }
                catch
                {
                    return null;
                }
            }

            public async Task EnsureAvailableAsync()
            {
                var availability = await GetAvailabilitySafeAsync().ConfigureAwait(false);
                if (availability != _availabilityAvailable)
                {
                    throw new PlatformNotSupportedException("Windows Hello is not available on this device.");
                }
            }

            public async Task<bool> PromptForVerificationAsync(string message)
            {
                try
                {
                    var asyncOperation = _requestVerificationAsync.Invoke(null, new object[] { message }) ?? throw new InvalidOperationException("RequestVerificationAsync returned null.");
                    var result = await InvokeAsyncOperation<int>(asyncOperation).ConfigureAwait(false);
                    return result == _verificationVerified;
                }
                catch (TargetInvocationException ex)
                {
                    throw ex.InnerException ?? ex;
                }
            }

            private Task<int> GetAvailabilitySafeAsync()
            {
                try
                {
                    return _availabilityProbe.Value;
                }
                catch (TargetInvocationException ex)
                {
                    if (ex.InnerException != null)
                    {
                        throw ex.InnerException;
                    }
                    throw;
                }
            }

            private Task<int> QueryAvailabilityAsync()
            {
                var asyncOperation = _checkAvailabilityAsync.Invoke(null, Array.Empty<object>()) ?? throw new InvalidOperationException("CheckAvailabilityAsync returned null.");
                return InvokeAsyncOperation<int>(asyncOperation);
            }

            private async Task<T> InvokeAsyncOperation<T>(object asyncOperation)
            {
                var taskObj = _asTaskGeneric.MakeGenericMethod(typeof(T)).Invoke(null, new[] { asyncOperation }) ?? throw new InvalidOperationException("Unable to bridge WinRT async operation.");

                if (taskObj is Task<T> typedTask)
                {
                    return await typedTask.ConfigureAwait(false);
                }

                if (taskObj is Task genericTask)
                {
                    await genericTask.ConfigureAwait(false);
                    var resultProperty = genericTask.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance);
                    if (resultProperty == null)
                    {
                        throw new InvalidOperationException("WinRT task completed without a result.");
                    }

                    return (T)resultProperty.GetValue(genericTask)!;
                }

                throw new InvalidOperationException("Unexpected task type returned from WindowsRuntimeSystemExtensions.AsTask.");
            }

            private static int GetEnumValue(Type enumType, string valueName)
            {
                var value = Enum.Parse(enumType, valueName);
                return Convert.ToInt32(value);
            }
        }
    }
}

