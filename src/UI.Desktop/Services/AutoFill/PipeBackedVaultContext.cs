using System;
using System.Text.Json;
using System.Threading;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services.Autofill;

namespace PhantomVault.UI.Services.AutoFill
{
    /// <summary>
    /// <see cref="IAutofillVaultContext"/> implementation that queries vault state
    /// from the running desktop app via <see cref="PipeNativeHostClient"/>.
    /// Results are cached for 500 ms to avoid a pipe round-trip per message.
    /// </summary>
    public sealed class PipeBackedVaultContext : IAutofillVaultContext
    {
        private readonly PipeNativeHostClient _client;
        private bool _cachedLocked = true;
        private bool _cachedAutofillEnabled = false;
        private DateTime _cacheExpiry = DateTime.MinValue;
        private readonly object _cacheLock = new();

        public PipeBackedVaultContext(PipeNativeHostClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public bool IsUnlocked
        {
            get
            {
                RefreshIfStale();
                return !_cachedLocked;
            }
        }

        public VaultManifest? CurrentManifest
        {
            get
            {
                RefreshIfStale();
                if (_cachedLocked) return null;
                return new VaultManifest { AutoFillEnabled = _cachedAutofillEnabled };
            }
        }

        private void RefreshIfStale()
        {
            lock (_cacheLock)
            {
                if (DateTime.UtcNow < _cacheExpiry) return;

                try
                {
                    var req = JsonSerializer.Serialize(new { action = "getVaultState" });
                    var resp = _client.SendAsync(req, CancellationToken.None).GetAwaiter().GetResult();
                    if (resp != null)
                    {
                        using var doc = JsonDocument.Parse(resp);
                        var root = doc.RootElement;
                        _cachedLocked = root.TryGetProperty("locked", out var l) && l.GetBoolean();
                        _cachedAutofillEnabled = root.TryGetProperty("autofillEnabled", out var ae) && ae.GetBoolean();
                    }
                }
                catch
                {
                    // Keep stale cached values; desktop app may be busy
                }

                _cacheExpiry = DateTime.UtcNow.AddMilliseconds(500);
            }
        }
    }
}
