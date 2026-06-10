using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhantomVault.Core.Models.DomainStores;

namespace PhantomVault.Core.Services.DomainKeys
{

    public sealed class RecoveryAuditService
    {

        public event EventHandler<RekeyRequiredEventArgs>? RekeyRequired;

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

            entry.ThisHash = ComputeEntryHash(entry);

            store.AuditLog.Add(entry);

            store.AuditChainHead = entry.ThisHash;

            while (store.AuditLog.Count > store.MaxAuditEntries)
            {
                store.AuditLog.RemoveAt(0);
            }

            if (success && eventType == RecoveryEventType.DomainRecovered && targetDomain.HasValue)
            {
                if (store.Policy.ForceRotationAfterRecovery)
                {
                    RaiseRekeyRequired(targetDomain.Value, entry.Id);
                }
            }
        }

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

            var normalizedCode = recoveryCode
                .Replace("-", "")
                .Replace(" ", "")
                .ToUpperInvariant();

            foreach (var entry in store.RecoveryCodes)
            {

                if (entry.Used)
                    continue;

                if (verifyHash(normalizedCode, entry.Hash))
                {
                    return entry;
                }
            }

            return null;
        }

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

        public void MarkSealedKeyUsed(SealedRecoveryKey sealedKey)
        {
            if (sealedKey == null)
                throw new ArgumentNullException(nameof(sealedKey));

            sealedKey.HasBeenUsed = true;
            sealedKey.LastUsedUtc = DateTimeOffset.UtcNow;
        }

        public void ValidatePolicy(
            RecoveryStore store,
            string? currentDeviceId,
            string? currentAppVersion)
        {
            if (store == null)
                throw new ArgumentNullException(nameof(store));

            var policy = store.Policy;

            if (policy.DeviceBindingMode == RecoveryDeviceBindingMode.Block)
            {
                if (!string.IsNullOrEmpty(store.DeviceId) &&
                    !string.Equals(store.DeviceId, currentDeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException(
                        "Recovery blocked: Device mismatch. This recovery store is bound to a different device.");
                }
            }

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

        public bool VerifyAuditChainIntegrity(RecoveryStore store)
        {
            if (store == null || store.AuditLog.Count == 0)
                return true;

            string expectedPrevHash = "genesis";

            foreach (var entry in store.AuditLog)
            {

                if (entry.PrevHash != expectedPrevHash)
                    return false;

                var computedHash = ComputeEntryHash(entry);
                if (computedHash != entry.ThisHash)
                    return false;

                expectedPrevHash = entry.ThisHash;
            }

            var lastEntry = store.AuditLog[^1];
            return store.AuditChainHead == lastEntry.ThisHash;
        }

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

        private static string ComputeEntryHash(RecoveryAuditEntry entry)
        {

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

        private static int CompareVersions(string current, string minimum)
        {

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

    public sealed class RekeyRequiredEventArgs : EventArgs
    {

        public CryptoDomain Domain { get; init; }

        public string TriggeringAuditEntryId { get; init; } = string.Empty;

        public DateTimeOffset Timestamp { get; init; }
    }
}

