using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhantomVault.Core.Models.DomainStores;

namespace PhantomVault.Core.Services.DomainKeys
{
    /// <summary>
    /// Service for recovery audit logging and rekey trigger management.
    ///
    /// Responsibilities:
    /// - Append hash-chained entries to recovery audit log
    /// - Track rekey requirements after recovery operations
    /// - Enforce recovery policy (delays, device binding, etc.)
    /// - Maintain audit chain integrity
    ///
    /// CRITICAL: Every recovery operation MUST be logged.
    /// The audit chain is tamper-evident via hash linking.
    /// </summary>
    public sealed class RecoveryAuditService
    {
        /// <summary>
        /// Event raised when a domain requires rekeying after recovery.
        /// Subscribers should initiate key rotation for the affected domain.
        /// </summary>
        public event EventHandler<RekeyRequiredEventArgs>? RekeyRequired;

        /// <summary>
        /// Logs an audit entry to the recovery store.
        /// Maintains hash chain integrity.
        /// </summary>
        /// <param name="store">Recovery store to append to</param>
        /// <param name="eventType">Type of recovery event</param>
        /// <param name="success">Whether the operation succeeded</param>
        /// <param name="targetDomain">Target domain (if applicable)</param>
        /// <param name="codeNumber">Recovery code number used (if applicable)</param>
        /// <param name="failureReason">Failure reason (if failed)</param>
        /// <param name="deviceFingerprint">Device fingerprint</param>
        /// <param name="appVersion">Application version</param>
        public void LogAuditEntry(
            RecoveryStore store,
            RecoveryEventType eventType,
            bool success,
            CryptoDomain? targetDomain = null,
            int? codeNumber = null,
            string? failureReason = null,
            string? deviceFingerprint = null,
            string? appVersion = null)
        {
            if (store == null)
                throw new ArgumentNullException(nameof(store));

            var entry = new RecoveryAuditEntry
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow,
                EventType = eventType,
                Success = success,
                TargetDomain = targetDomain?.ToString(),
                CodeNumber = codeNumber,
                FailureReason = failureReason,
                DeviceFingerprint = deviceFingerprint,
                AppVersion = appVersion,
                PrevHash = store.AuditChainHead ?? "genesis"
            };

            // Compute hash of this entry (excluding ThisHash field)
            entry.ThisHash = ComputeEntryHash(entry);

            // Append to audit log
            store.AuditLog.Add(entry);

            // Update chain head
            store.AuditChainHead = entry.ThisHash;

            // Trim old entries if over limit
            while (store.AuditLog.Count > store.MaxAuditEntries)
            {
                store.AuditLog.RemoveAt(0);
            }

            // Trigger rekey if this was a successful domain recovery
            if (success && eventType == RecoveryEventType.DomainRecovered && targetDomain.HasValue)
            {
                if (store.Policy.ForceRotationAfterRecovery)
                {
                    RaiseRekeyRequired(targetDomain.Value, entry.Id);
                }
            }
        }

        /// <summary>
        /// Validates a recovery code against stored hashes.
        /// Returns the matching entry if valid, null otherwise.
        /// </summary>
        /// <param name="store">Recovery store containing code hashes</param>
        /// <param name="recoveryCode">Recovery code to validate</param>
        /// <param name="verifyHash">Function to verify Argon2id hash</param>
        /// <returns>Matching code entry if valid</returns>
        public RecoveryCodeEntry? ValidateRecoveryCode(
            RecoveryStore store,
            string recoveryCode,
            Func<string, string, bool> verifyHash)
        {
            if (store == null)
                throw new ArgumentNullException(nameof(store));
            if (string.IsNullOrEmpty(recoveryCode))
                return null;
            if (verifyHash == null)
                throw new ArgumentNullException(nameof(verifyHash));

            // Normalize recovery code
            var normalizedCode = recoveryCode
                .Replace("-", "")
                .Replace(" ", "")
                .ToUpperInvariant();

            foreach (var entry in store.RecoveryCodes)
            {
                // Skip already used codes
                if (entry.Used)
                    continue;

                // Verify against stored Argon2id hash
                if (verifyHash(normalizedCode, entry.Hash))
                {
                    return entry;
                }
            }

            return null;
        }

        /// <summary>
        /// Marks a recovery code as used.
        /// </summary>
        /// <param name="store">Recovery store</param>
        /// <param name="codeEntry">Code entry to mark</param>
        /// <param name="usedForDomain">Domain that was recovered</param>
        public void MarkCodeUsed(
            RecoveryStore store,
            RecoveryCodeEntry codeEntry,
            CryptoDomain usedForDomain)
        {
            if (store == null)
                throw new ArgumentNullException(nameof(store));
            if (codeEntry == null)
                throw new ArgumentNullException(nameof(codeEntry));

            codeEntry.Used = true;
            codeEntry.UsedUtc = DateTimeOffset.UtcNow;
            codeEntry.UsedForDomain = usedForDomain.ToString();

            // If policy says to invalidate all codes after one is used
            if (store.Policy.InvalidateCodesAfterUse)
            {
                foreach (var code in store.RecoveryCodes)
                {
                    if (!code.Used)
                    {
                        code.Used = true;
                        code.UsedUtc = DateTimeOffset.UtcNow;
                        code.UsedForDomain = "invalidated_by_policy";
                    }
                }
            }
        }

        /// <summary>
        /// Marks a sealed recovery key as used.
        /// </summary>
        /// <param name="sealedKey">Sealed key that was used</param>
        public void MarkSealedKeyUsed(SealedRecoveryKey sealedKey)
        {
            if (sealedKey == null)
                throw new ArgumentNullException(nameof(sealedKey));

            sealedKey.HasBeenUsed = true;
            sealedKey.LastUsedUtc = DateTimeOffset.UtcNow;
        }

        /// <summary>
        /// Validates recovery policy before allowing recovery.
        /// Throws if policy is violated.
        /// </summary>
        /// <param name="store">Recovery store with policy</param>
        /// <param name="currentDeviceId">Current device identifier</param>
        /// <param name="currentAppVersion">Current app version</param>
        public void ValidatePolicy(
            RecoveryStore store,
            string? currentDeviceId,
            string? currentAppVersion)
        {
            if (store == null)
                throw new ArgumentNullException(nameof(store));

            var policy = store.Policy;

            // Check device binding
            if (policy.DeviceBindingMode == RecoveryDeviceBindingMode.Block)
            {
                if (!string.IsNullOrEmpty(store.DeviceId) &&
                    !string.Equals(store.DeviceId, currentDeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException(
                        "Recovery blocked: Device mismatch. This recovery store is bound to a different device.");
                }
            }

            // Check minimum app version
            if (!string.IsNullOrEmpty(policy.MinAppVersion) && !string.IsNullOrEmpty(currentAppVersion))
            {
                if (CompareVersions(currentAppVersion, policy.MinAppVersion) < 0)
                {
                    throw new UnauthorizedAccessException(
                        $"Recovery blocked: App version {currentAppVersion} is below minimum required version {policy.MinAppVersion}. " +
                        "Please update the application before attempting recovery.");
                }
            }
        }

        /// <summary>
        /// Checks if device matches and returns warning if mismatched (for Warn mode).
        /// </summary>
        public bool CheckDeviceMatch(RecoveryStore store, string? currentDeviceId, out string? warning)
        {
            warning = null;

            if (store?.Policy.DeviceBindingMode != RecoveryDeviceBindingMode.Warn)
                return true;

            if (!string.IsNullOrEmpty(store.DeviceId) &&
                !string.Equals(store.DeviceId, currentDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                warning = "Warning: This recovery is being performed from a different device than the one " +
                          "that originally set up recovery. Proceed with caution.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Verifies the integrity of the audit chain.
        /// </summary>
        /// <param name="store">Recovery store to verify</param>
        /// <returns>True if chain is intact</returns>
        public bool VerifyAuditChainIntegrity(RecoveryStore store)
        {
            if (store == null || store.AuditLog.Count == 0)
                return true;

            string expectedPrevHash = "genesis";

            foreach (var entry in store.AuditLog)
            {
                // Verify prev hash links correctly
                if (entry.PrevHash != expectedPrevHash)
                    return false;

                // Verify entry hash is correct
                var computedHash = ComputeEntryHash(entry);
                if (computedHash != entry.ThisHash)
                    return false;

                expectedPrevHash = entry.ThisHash;
            }

            // Verify chain head matches last entry
            var lastEntry = store.AuditLog[^1];
            return store.AuditChainHead == lastEntry.ThisHash;
        }

        /// <summary>
        /// Gets count of remaining (unused) recovery codes.
        /// </summary>
        public int GetRemainingCodeCount(RecoveryStore store)
        {
            if (store == null)
                return 0;

            int count = 0;
            foreach (var code in store.RecoveryCodes)
            {
                if (!code.Used)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Computes hash of an audit entry for chain linking.
        /// </summary>
        private static string ComputeEntryHash(RecoveryAuditEntry entry)
        {
            // Create a canonical representation for hashing
            var canonical = new
            {
                entry.Id,
                Timestamp = entry.Timestamp.ToUnixTimeMilliseconds(),
                EventType = entry.EventType.ToString(),
                entry.Success,
                entry.TargetDomain,
                entry.CodeNumber,
                entry.DeviceFingerprint,
                entry.AppVersion,
                entry.FailureReason,
                entry.PrevHash
            };

            var json = JsonSerializer.Serialize(canonical);
            var bytes = Encoding.UTF8.GetBytes(json);

            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(bytes);
            return $"sha256:{Convert.ToBase64String(hash)}";
        }

        /// <summary>
        /// Compares two version strings (simple semver comparison).
        /// </summary>
        private static int CompareVersions(string current, string minimum)
        {
            // Simple semver-like comparison
            var currentParts = current.Split('.');
            var minimumParts = minimum.Split('.');

            for (int i = 0; i < Math.Max(currentParts.Length, minimumParts.Length); i++)
            {
                int currentPart = i < currentParts.Length && int.TryParse(currentParts[i], out var c) ? c : 0;
                int minimumPart = i < minimumParts.Length && int.TryParse(minimumParts[i], out var m) ? m : 0;

                if (currentPart < minimumPart)
                    return -1;
                if (currentPart > minimumPart)
                    return 1;
            }

            return 0;
        }

        /// <summary>
        /// Raises the RekeyRequired event.
        /// </summary>
        private void RaiseRekeyRequired(CryptoDomain domain, string auditEntryId)
        {
            RekeyRequired?.Invoke(this, new RekeyRequiredEventArgs
            {
                Domain = domain,
                TriggeringAuditEntryId = auditEntryId,
                Timestamp = DateTimeOffset.UtcNow
            });
        }
    }

    /// <summary>
    /// Event args for the RekeyRequired event.
    /// </summary>
    public sealed class RekeyRequiredEventArgs : EventArgs
    {
        /// <summary>
        /// Domain that requires rekeying.
        /// </summary>
        public CryptoDomain Domain { get; init; }

        /// <summary>
        /// ID of the audit entry that triggered this requirement.
        /// </summary>
        public string TriggeringAuditEntryId { get; init; } = string.Empty;

        /// <summary>
        /// When the rekey was triggered.
        /// </summary>
        public DateTimeOffset Timestamp { get; init; }
    }
}
