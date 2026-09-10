using System;
using System.Text.Json;
using System.Threading;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services.Autofill;

namespace PhantomVault.UI.Services.AutoFill
{

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

                        // Unlocked only when the host explicitly answers locked=false. A missing
                        // or non-boolean field used to read as unlocked.
                        _cachedLocked = !(root.TryGetProperty("locked", out var l) && l.ValueKind == JsonValueKind.False);
                        _cachedAutofillEnabled = !_cachedLocked
                            && root.TryGetProperty("autofillEnabled", out var ae)
                            && ae.ValueKind == JsonValueKind.True;
                    }
                    else
                    {
                        FailClosed();
                    }
                }
                catch (Exception ex)
                {
                    // No answer from the vault host must mean locked. This used to keep the
                    // previous value, so a host that stopped responding while unlocked stayed
                    // "unlocked" here indefinitely.
                    Serilog.Log.Debug(ex, "[PipeBackedVaultContext] Vault state query failed; treating the vault as locked");
                    FailClosed();
                }

                _cacheExpiry = DateTime.UtcNow.AddMilliseconds(500);
            }
        }

        private void FailClosed()
        {
            _cachedLocked = true;
            _cachedAutofillEnabled = false;
        }
    }
}

