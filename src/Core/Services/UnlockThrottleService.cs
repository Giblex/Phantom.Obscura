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
        private const int MaxAttemptsBeforeLockout = 5;
        private const int LockoutDurationMinutes = 10;
        private const int MaxLockoutDurationMinutes = 60;

        private readonly object _lock = new();

        public bool IsThrottled(string manifestPath, out TimeSpan remainingLockout)
        {
            remainingLockout = TimeSpan.Zero;

            var manifestKey = ComputeManifestKey(manifestPath);
            var throttleData = LoadThrottleData();

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

                if (record.FailedAttempts >= MaxAttemptsBeforeLockout)
                {

                    int excessAttempts = record.FailedAttempts - MaxAttemptsBeforeLockout + 1;
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
            catch
            {

                return new Dictionary<string, ThrottleRecord>();
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

