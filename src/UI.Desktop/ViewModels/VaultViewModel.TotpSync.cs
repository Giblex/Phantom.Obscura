// Partial class — TOTP Synchronization section of VaultViewModel
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services.Sync;
using PhantomVault.UI.Desktop.Services;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.ViewModels
{
    public sealed partial class VaultViewModel
    {
        #region TOTP Synchronization

        private async Task InitializeTotpSyncAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_mountPath))
                {
                    SetTotpSyncContextState(syncTotpEnabled: false);
                    return;
                }

                // TOTP secrets are the only sync channel that moves secret material, so it
                // is governed by an explicit, revocable opt-in. When cross-app sync is off,
                // or the TOTP channel specifically is off, we never write seeds to disk.
                var syncSettings = SettingsService.Load();
                if (!syncSettings.SyncEnabled || !syncSettings.SyncTotp)
                {
                    Debug.WriteLine("[VaultViewModel] TOTP sync disabled by settings — skipping Attestor TOTP sync.");
                    SetTotpSyncContextState(syncTotpEnabled: false);
                    return;
                }

                var syncPath = Path.Combine(_mountPath, ".phantom", "vaults", "totp-sync.json");
                var syncDir = Path.GetDirectoryName(syncPath);

                if (!string.IsNullOrEmpty(syncDir) && !Directory.Exists(syncDir))
                {
                    Directory.CreateDirectory(syncDir);
                }

                _totpSyncService = new TotpSyncServiceObscura(syncPath);
                _totpSyncService.EntriesChanged += OnTotpEntriesChanged;
                await _totpSyncService.InitializeAsync();

                SetTotpSyncContextState(syncTotpEnabled: true);

                await ExportTotpToSyncAsync();

                StatusMessage = "TOTP sync initialized";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize TOTP sync: {ex.Message}");
                StatusMessage = "TOTP sync unavailable";
            }
        }

        /// <summary>
        /// Updates the shared <see cref="PhantomVault.UI.Services.Sync.TotpSyncVaultContext"/> so
        /// <see cref="PhantomVault.UI.Services.Sync.TotpSyncPipeServer"/> can gate and route TOTP
        /// pushes from PhantomAttestor. Best-effort: the live pipe is purely additive on top of
        /// the existing totp-sync.json file transport.
        /// </summary>
        private void SetTotpSyncContextState(bool syncTotpEnabled)
        {
            try
            {
                var ctx = (Application.Current as App)?.Services?.GetService(typeof(PhantomVault.UI.Services.Sync.TotpSyncVaultContext))
                          as PhantomVault.UI.Services.Sync.TotpSyncVaultContext;
                ctx?.SetUnlocked(this, syncTotpEnabled);
            }
            catch { /* best-effort */ }
        }

        private async Task ExportTotpToSyncAsync()
        {
            try
            {
                if (_totpSyncService == null) return;

                var coreCredentials = _credentials
                    .Select(c => c.GetCredential())
                    .Where(c => !string.IsNullOrWhiteSpace(c.TotpSecret));

                var obscuraEntries = _totpSyncService.ExtractFromVault(coreCredentials);

                if (obscuraEntries.Count > 0)
                {
                    await _totpSyncService.ExportToSyncFileAsync(obscuraEntries);
                    Debug.WriteLine($"Exported {obscuraEntries.Count} TOTP entries from Obscura to sync file");

                    _ = PushTotpEntriesOverPipeAsync(obscuraEntries);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to export TOTP entries: {ex.Message}");
            }
        }

        /// <summary>
        /// Fire-and-forget live push of TOTP entries to PhantomAttestor over
        /// <see cref="PhantomVault.UI.Services.Sync.TotpSyncPipeClient"/>. If Attestor isn't
        /// running or the pipe is rejected, this silently no-ops — the totp-sync.json file
        /// watcher on Attestor's side will pick up the change as it does today.
        /// </summary>
        private async Task PushTotpEntriesOverPipeAsync(List<SharedTotpEntry> entries)
        {
            try
            {
                var client = (Application.Current as App)?.Services?.GetService(typeof(PhantomVault.UI.Services.Sync.TotpSyncPipeClient))
                             as PhantomVault.UI.Services.Sync.TotpSyncPipeClient;
                if (client == null) return;

                await client.TryPushEntriesAsync(entries);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VaultViewModel] TOTP pipe push failed (falling back to file sync): {ex.Message}");
            }
        }

        private async void OnTotpEntriesChanged(object? sender, List<SharedTotpEntry> entries)
        {
            await ApplyIncomingTotpEntriesAsync(entries);
        }

        /// <summary>
        /// Merges TOTP entries pushed from PhantomAttestor (whether delivered via the
        /// totp-sync.json file watcher or the live <see cref="PhantomVault.UI.Services.Sync.TotpSyncPipeServer"/>)
        /// into the unlocked vault.
        /// </summary>
        private async Task ApplyIncomingTotpEntriesAsync(List<SharedTotpEntry> entries)
        {
            try
            {
                foreach (var entry in entries)
                {
                    var cred = _credentials.FirstOrDefault(c => c.Title == entry.LinkedPasswordEntryId);
                    if (cred != null)
                    {
                        var coreCred = cred.GetCredential();

                        coreCred.TotpSecret = entry.Secret;
                        coreCred.TotpDigits = entry.Digits;
                        coreCred.TotpTimeStep = entry.Period;
                        coreCred.TotpAlgorithm = entry.Algorithm;
                        coreCred.TotpIssuer = entry.Issuer;
                        coreCred.TotpAccountName = entry.AccountName;
                        coreCred.LastUpdatedUtc = DateTimeOffset.UtcNow;

                        var index = _credentials.IndexOf(cred);
                        _credentials.RemoveAt(index);
                        var newCredentialVM = new CredentialViewModel(coreCred);
                        _credentials.Insert(index, newCredentialVM);

                        if (SelectedCredential?.Title == entry.LinkedPasswordEntryId)
                        {
                            SelectedCredential = newCredentialVM;
                        }
                    }
                }

                await SaveVaultAsync();

                StatusMessage = $"Synced {entries.Count} TOTP entries from PhantomAttestor";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to sync TOTP entries: {ex.Message}");
                RecentIssuesLog.Instance.Record(IssueSeverity.Warning, "TOTP sync failed", $"Incoming authenticator codes could not be applied: {ex.Message}");
            }
        }

        Task ITotpSyncBridge.ApplyIncomingEntriesAsync(List<SharedTotpEntry> entries)
            => ApplyIncomingTotpEntriesAsync(entries);

        #endregion
    }
}
