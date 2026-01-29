using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Provides unlock attempt throttling that works independently of manifest decryption.
    /// Stores failed attempts in a DPAPI-protected local file, keyed by manifest file hash.
    /// This allows throttling even when the wrong password is entered.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class UnlockThrottleService
    {
        private const string ThrottleDataFolder = "PhantomVault";
        private const string ThrottleDataFile = "unlock_throttle.bin";
        private const int MaxAttemptsBeforeLockout = 5;
        private const int LockoutDurationMinutes = 10;
        private const int MaxLockoutDurationMinutes = 60;

        private readonly object _lock = new();

        /// <summary>
        /// Checks if unlock attempts are currently throttled for the given manifest.
        /// Returns true if locked out, false if unlock can proceed.
        /// </summary>
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

        /// <summary>
        /// Registers a failed unlock attempt for the given manifest.
        /// May trigger or extend a lockout period.
        /// </summary>
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

                // Reset counter if last attempt was more than 30 minutes ago
                if (DateTimeOffset.UtcNow - record.LastAttemptUtc > TimeSpan.FromMinutes(30))
                {
                    record.FailedAttempts = 0;
                }

                record.FailedAttempts++;
                record.LastAttemptUtc = DateTimeOffset.UtcNow;

                // Apply lockout if threshold reached
                if (record.FailedAttempts >= MaxAttemptsBeforeLockout)
                {
                    // True exponential backoff: 10min, 20min, 40min, 60min (capped)
                    // Formula: baseTime * 2^(attempts - threshold)
                    int excessAttempts = record.FailedAttempts - MaxAttemptsBeforeLockout + 1;
                    int lockoutMinutes = LockoutDurationMinutes * (int)Math.Pow(2, excessAttempts - 1);
                    lockoutMinutes = Math.Min(lockoutMinutes, MaxLockoutDurationMinutes);

                    record.LockedUntilUtc = DateTimeOffset.UtcNow.AddMinutes(lockoutMinutes);
                }

                throttleData[manifestKey] = record;
                SaveThrottleData(throttleData);
            }
        }

        /// <summary>
        /// Resets the failed attempt counter on successful unlock.
        /// </summary>
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

        /// <summary>
        /// Gets the current failed attempt count for a manifest.
        /// </summary>
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

        /// <summary>
        /// Computes a stable key for the manifest based on its full path.
        /// Uses SHA256 hash of the normalized path.
        /// </summary>
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
                // If data is corrupted, start fresh
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
