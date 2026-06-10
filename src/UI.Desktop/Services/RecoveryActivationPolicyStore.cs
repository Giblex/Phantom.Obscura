using System;
using System.IO;
using System.Text.Json;
using PhantomVault.Core.Models;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Persists the Recovery Activation policy to a per-user sidecar JSON in LocalAppData,
    /// mirroring DefenceSettingsService. Avoids the encrypted-manifest write path so the
    /// policy can be edited without re-supplying password + keyfile. The policy is not a
    /// secret — it only gates the UI Recovery launch button.
    /// </summary>
    public sealed class RecoveryActivationPolicyStore
    {
        private readonly string _configFilePath;
        private static readonly JsonSerializerOptions s_jsonOptions = new()
        {
            WriteIndented = true,
        };

        public RecoveryActivationPolicyStore()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var folder = Path.Combine(appData, "PhantomVault");
            Directory.CreateDirectory(folder);
            _configFilePath = Path.Combine(folder, "recovery-activation-policy.json");
        }

        public RecoveryActivationPolicy? Load()
        {
            try
            {
                if (!File.Exists(_configFilePath))
                {
                    return null;
                }
                var json = File.ReadAllText(_configFilePath);
                return JsonSerializer.Deserialize<RecoveryActivationPolicy>(json);
            }
            catch
            {
                // Fail closed: a corrupt file means "no policy" — keep gating off rather than
                // partially-applying a malformed policy.
                return null;
            }
        }

        public void Save(RecoveryActivationPolicy policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            policy.UpdatedUtc = DateTimeOffset.UtcNow;
            var json = JsonSerializer.Serialize(policy, s_jsonOptions);
            File.WriteAllText(_configFilePath, json);
        }

        public RecoveryActivationPolicy LoadOrCreateDefault()
        {
            return Load() ?? new RecoveryActivationPolicy();
        }
    }
}
