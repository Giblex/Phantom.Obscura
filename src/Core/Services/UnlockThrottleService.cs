using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PhantomVault.Core.Services
{

    [SupportedOSPlatform("windows")]
    public sealed class UnlockThrottleService
    {
        private const string ThrottleDataFolder = "PhantomVault";
        private const string ThrottleDataFile = "unlock_throttle.bin";
        private const int DefaultMaxAttemptsBeforeLockout = 5;
        private const int LockoutDurationMinutes = 10;
        private const int MaxLockoutDurationMinutes = 60;

        private readonly object _lock = new();
        private readonly Func<int?>? _maxAttemptsProvider;

        /// <summary>
        /// Local unlock throttle.
        ///
        /// Scope note: the backing file is DPAPI-sealed, so it cannot be edited — but it
        /// can still be deleted, which clears the counter. This is a speed-bump against
        /// casual repeated attempts on a live machine, NOT a defence against offline
        /// brute force. An attacker who has copied the vault is not running this code at
        /// all; the only thing standing between them and the key is Argon2id hardness
        /// (see SecurityTuning.Calibrate). Do not present this as offline protection.
        /// </summary>
        /// <param name="maxAttemptsProvider">
        /// Supplies the user's configured attempt limit (UserSettings.MaxFailedUnlockAttempts);
        /// null result means unlimited. Previously hardcoded to 5, which silently ignored
        /// the choice offered in Security Settings.
        /// </param>
        public UnlockThrottleService(Func<int?>? maxAttemptsProvider = null)
        {
            _maxAttemptsProvider = maxAttemptsProvider;
        }

        private int? ResolveMaxAttempts()
        {
            if (_maxAttemptsProvider == null) return DefaultMaxAttemptsBeforeLockout;

            try
            {
                return _maxAttemptsProvider();
            }
            catch
            {
                return DefaultMaxAttemptsBeforeLockout;
            }
        }

        // Sentinel entry written when the throttle store cannot be read. It applies to
        // every manifest, since a damaged store means we do not know which vaults were
        // mid-lockout.
        private const string CorruptStateKey = "__throttle_state_unreadable__";

        public bool IsThrottled(string manifestPath, out TimeSpan remainingLockout)
        {
            remainingLockout = TimeSpan.Zero;

            var manifestKey = ComputeManifestKey(manifestPath);
            var throttleData = LoadThrottleData();

            // A protective lockout from an unreadable store applies regardless of manifest.
            if (throttleData.TryGetValue(CorruptStateKey, out var corruptRecord)
                && corruptRecord.LockedUntilUtc.HasValue
                && DateTimeOffset.UtcNow < corruptRecord.LockedUntilUtc.Value)
            {
                remainingLockout = corruptRecord.LockedUntilUtc.Value - DateTimeOffset.UtcNow;
                return true;
            }

            if (!throttleData.TryGetValue(manifestKey, out var record))
            {
                return false;
            }

            if (record.LockedUntilUtc.HasValue && DateTimeOffset.UtcNow < record.LockedUntilUtc.Value)
            {
                remainingLockout = record.LockedUntilUtc.Value - DateTimeOffset.UtcNow;
                return true;
            }

            return false;
        }

        public void RegisterFailedAttempt(string manifestPath)
        {
            var manifestKey = ComputeManifestKey(manifestPath);

            lock (_lock)
            {
                var throttleData = LoadThrottleData();

                if (!throttleData.TryGetValue(manifestKey, out var record))
                {
                    record = new ThrottleRecord
                    {
                        FailedAttempts = 0,
                        LastAttemptUtc = DateTimeOffset.UtcNow,
                        LockedUntilUtc = null
                    };
                }

                if (DateTimeOffset.UtcNow - record.LastAttemptUtc > TimeSpan.FromMinutes(30))
                {
                    record.FailedAttempts = 0;
                }

                record.FailedAttempts++;
                record.LastAttemptUtc = DateTimeOffset.UtcNow;

                var maxAttempts = ResolveMaxAttempts();

                // null == the user chose "unlimited attempts"; record the attempt for
                // reporting but never lock out.
                if (maxAttempts.HasValue && record.FailedAttempts >= maxAttempts.Value)
                {
                    int excessAttempts = record.FailedAttempts - maxAttempts.Value + 1;
                    int lockoutMinutes = LockoutDurationMinutes * (int)Math.Pow(2, excessAttempts - 1);
                    lockoutMinutes = Math.Min(lockoutMinutes, MaxLockoutDurationMinutes);

                    record.LockedUntilUtc = DateTimeOffset.UtcNow.AddMinutes(lockoutMinutes);
                }

                throttleData[manifestKey] = record;
                SaveThrottleData(throttleData);
            }
        }

        public void ResetAttempts(string manifestPath)
        {
            var manifestKey = ComputeManifestKey(manifestPath);

            lock (_lock)
            {
                var throttleData = LoadThrottleData();

                if (throttleData.ContainsKey(manifestKey))
                {
                    throttleData.Remove(manifestKey);
                    SaveThrottleData(throttleData);
                }
            }
        }

        public int GetFailedAttemptCount(string manifestPath)
        {
            var manifestKey = ComputeManifestKey(manifestPath);
            var throttleData = LoadThrottleData();

            if (throttleData.TryGetValue(manifestKey, out var record))
            {
                return record.FailedAttempts;
            }

            return 0;
        }

        private static string ComputeManifestKey(string manifestPath)
        {
            var normalizedPath = Path.GetFullPath(manifestPath).ToUpperInvariant();
            var pathBytes = Encoding.UTF8.GetBytes(normalizedPath);
            var hash = SHA256.HashData(pathBytes);
            return Convert.ToHexString(hash);
        }

        private static string GetThrottleDataPath()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appDataPath, ThrottleDataFolder, ThrottleDataFile);
        }

        private Dictionary<string, ThrottleRecord> LoadThrottleData()
        {
            var path = GetThrottleDataPath();

            if (!File.Exists(path))
            {
                return new Dictionary<string, ThrottleRecord>();
            }

            try
            {
                var protectedData = File.ReadAllBytes(path);
                var jsonBytes = ProtectedData.Unprotect(
                    protectedData,
                    Encoding.UTF8.GetBytes("PhantomVault.UnlockThrottle"),
                    DataProtectionScope.CurrentUser);

                var json = Encoding.UTF8.GetString(jsonBytes);
                return JsonSerializer.Deserialize<Dictionary<string, ThrottleRecord>>(json)
                    ?? new Dictionary<string, ThrottleRecord>();
            }
            catch (Exception ex)
            {
                // The file exists but will not unseal or parse — it has been tampered with,
                // truncated, or sealed under a different Windows profile. Returning an empty
                // dictionary here treated that as "no failed attempts on record", which made
                // corrupting the file equivalent to clearing the throttle. Fail closed with a
                // fresh lockout instead, so damaging the file costs an attacker time rather
                // than saving them time.
                Serilog.Log.Warning(ex, "Unlock throttle data could not be read; applying a protective lockout");

                return new Dictionary<string, ThrottleRecord>
                {
                    [CorruptStateKey] = new ThrottleRecord
                    {
                        FailedAttempts = ResolveMaxAttempts() ?? DefaultMaxAttemptsBeforeLockout,
                        LastAttemptUtc = DateTimeOffset.UtcNow,
                        LockedUntilUtc = DateTimeOffset.UtcNow.AddMinutes(LockoutDurationMinutes)
                    }
                };
            }
        }

        private void SaveThrottleData(Dictionary<string, ThrottleRecord> data)
        {
            var path = GetThrottleDataPath();
            var directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(data);
            var jsonBytes = Encoding.UTF8.GetBytes(json);
            var protectedData = ProtectedData.Protect(
                jsonBytes,
                Encoding.UTF8.GetBytes("PhantomVault.UnlockThrottle"),
                DataProtectionScope.CurrentUser);

            File.WriteAllBytes(path, protectedData);
        }

        private struct ThrottleRecord
        {
            public int FailedAttempts { get; set; }
            public DateTimeOffset LastAttemptUtc { get; set; }
            public DateTimeOffset? LockedUntilUtc { get; set; }
        }
    }
}

