// Partial class — USB Auto-Inject section of VaultViewModel
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using PhantomVault.Core.Models.AutoInject;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.AutoInject;
using PhantomVault.UI.Desktop.Services;
using PhantomVault.UI.Views;
using PhantomVault.UI.Views.Autofill;

namespace PhantomVault.UI.ViewModels
{
    public sealed partial class VaultViewModel
    {
        #region USB Auto-Inject

        private void InitializeAutoInject()
        {
            if (_autoInjectService == null)
                return;

            try
            {
                _autoInjectService.SetCredentialProviderFactory(() =>
                    new VaultViewModelCredentialProvider(this));

                _autoInjectService.PromptRequired += OnAutoInjectPromptRequired;
                _autoInjectService.PasskeyReady += OnPasskeyReady;

                _ = _autoInjectService.StartAsync();

                Debug.WriteLine("[VaultViewModel] Auto-inject service initialized");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VaultViewModel] Failed to initialize auto-inject: {ex.Message}");
                RecentIssuesLog.Instance.Record(IssueSeverity.Warning, "Auto-fill unavailable", $"USB auto-inject could not start; browser auto-fill may not work this session: {ex.Message}");
            }
        }

        private void OnAutoInjectPromptRequired(object? sender, AutoInjectPromptEventArgs e)
        {
            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    // ── 1. Get target field rect ─────────────────────────────────────────
                    var fieldRect = GetForegroundWindowFieldRect();

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));

                    // ── 2. Logo rise + field trace + collapse animation ───────────────────
                    // The overlay is topmost and borderless. It MUST be closed on every
                    // path, including exceptions: a leaked one sits over the whole
                    // desktop and the user cannot click past it.
                    var overlay = new AutofillUsbIndicatorOverlay();
                    AutofillIconBadge badge;
                    try
                    {
                        overlay.Show();
                        await overlay.RunAsync(fieldRect, cts.Token);

                        // ── 3. Badge takes over the traced ring ─────────────────────
                        // Shown while the stroke is still up, then the stroke fades over
                        // it, so the drawn ring appears to become the badge.
                        badge = new AutofillIconBadge();
                        badge.Show();
                        badge.PositionLeftOfField(fieldRect);

                        var popIn = badge.PopInAsync();
                        await overlay.FadeOutAsync(cts.Token);
                        await popIn;

                        // Pre-fill the strongest match so the common case needs no
                        // clicks at all — but never submit. The badge stays put so the
                        // user can open the menu and switch to a different account if
                        // this guess was wrong.
                        var best = e.Matches.FirstOrDefault(m => !m.IsPasskey);
                        if (best != null && _autoInjectService != null)
                        {
                            await _autoInjectService.AutoFillAsync(
                                best.CredentialId, autoSubmit: false, AutoFillField.Both);
                        }
                    }
                    finally
                    {
                        overlay.ForceClose();
                    }

                    // ── 4. Icon click → credential menu ──────────────────────────────────
                    var autoInjectSvc = _autoInjectService;
                    badge.IconClicked += (_, _) =>
                    {
                        try
                        {
                            var badgePos = badge.Position;
                            var badgeH = badge.Height;

                            var menu = new AutofillCredentialMenu(
                                e.Context.Domain ?? e.Context.WindowTitle ?? "",
                                e.Matches,
                                async action =>
                                {
                                    if (autoInjectSvc == null) return;
                                    var id = action.Match.CredentialId;

                                    if (action.Copy)
                                    {
                                        await autoInjectSvc.CopyFieldAsync(id, action.Field);
                                        return;
                                    }

                                    if (action.Authenticate)
                                    {
                                        await AuthenticatePasskeyAsync(action.Match);
                                        return;
                                    }

                                    // Never auto-submit. The user reviews what was entered
                                    // and presses Enter themselves, so a wrong match can
                                    // still be corrected.
                                    await autoInjectSvc.AutoFillAsync(id, autoSubmit: false, action.Field);
                                },
                                totpProvider: id => autoInjectSvc is null
                                    ? System.Threading.Tasks.Task.FromResult<TotpSnapshot?>(null)
                                    : autoInjectSvc.GetTotpAsync(id));

                            // Retire the badge whenever the menu goes away, whatever the
                            // reason. Doing it here only — not also in the fill callback —
                            // keeps it from being closed twice.
                            menu.Closed += (_, _) => badge.CloseOnce();

                            menu.Show();
                            menu.PositionNearBadge(badgePos, badgeH);
                            menu.Activate();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[AutoFill] Menu open failed: {ex.Message}");
                        }
                    };

                    // The badge deliberately persists. It used to self-dismiss after
                    // 12 seconds, which meant looking away for a moment lost the only
                    // route to the other matches. It now stays until the user fills,
                    // authenticates, or dismisses the menu.
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[VaultViewModel] Auto-inject prompt error: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Runs the passkey assertion for a suggestion row.
        ///
        /// Username and password are meaningless for a passkey, so the row's primary
        /// action lands here instead of typing anything. The stored handle and relying
        /// party now travel on <see cref="CredentialMatch"/>; previously neither was
        /// available, which is why this could only log its intent.
        /// </summary>
        private async System.Threading.Tasks.Task AuthenticatePasskeyAsync(CredentialMatch match)
        {
            try
            {
                var passkeys = (Avalonia.Application.Current as PhantomVault.UI.App)?
                    .Services?.GetService(typeof(IPasskeyService)) as IPasskeyService;

                if (passkeys == null || !passkeys.IsSupported)
                {
                    Debug.WriteLine("[AutoFill] No passkey authenticator available");
                    RecentIssuesLog.Instance.Record(IssueSeverity.Warning,
                        "Passkey unavailable",
                        "No platform authenticator is available on this device.");
                    return;
                }

                if (string.IsNullOrEmpty(match.PasskeyId))
                {
                    Debug.WriteLine($"[AutoFill] Credential '{match.CredentialId}' has no stored passkey handle");
                    return;
                }

                // The challenge must be fresh per assertion, otherwise the ceremony is
                // replayable.
                var challenge = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
                var handle = DecodePasskeyHandle(match.PasskeyId);
                var rpId = string.IsNullOrWhiteSpace(match.RelyingPartyId) ? match.Domain : match.RelyingPartyId;

                bool ok = await passkeys.AuthenticateAsync(handle, rpId, challenge);
                Debug.WriteLine($"[AutoFill] Passkey assertion for '{rpId}': {(ok ? "succeeded" : "failed")}");

                if (!ok)
                {
                    RecentIssuesLog.Instance.Record(IssueSeverity.Warning,
                        "Passkey rejected",
                        $"The authenticator did not approve sign-in for {rpId}.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AutoFill] Passkey authentication failed: {ex.Message}");
                RecentIssuesLog.Instance.Record(IssueSeverity.Warning,
                    "Passkey error", ex.Message);
            }
        }

        /// <summary>
        /// Stored handles are base64 where possible; older entries are plain text.
        /// Falling back to UTF-8 keeps those working rather than throwing.
        /// </summary>
        private static byte[] DecodePasskeyHandle(string stored)
        {
            try { return Convert.FromBase64String(stored); }
            catch (FormatException) { return System.Text.Encoding.UTF8.GetBytes(stored); }
        }

        // ── Win32 helpers ─────────────────────────────────────────────────────────

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private static PixelRect GetForegroundWindowFieldRect()
        {
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var wr))
                {
                    int ww = wr.Right - wr.Left;
                    int wh = wr.Bottom - wr.Top;
                    int fw = Math.Min(ww - 80, 320);
                    int fx = wr.Left + (ww - fw) / 2;
                    int fy = wr.Top + (int)(wh * 0.45);
                    return new PixelRect(fx, fy, fw, 36);
                }
            }
            catch { /* best-effort */ }
            return new PixelRect(760, 440, 320, 36);
        }

        private void OnPasskeyReady(object? sender, PasskeyReadyEventArgs e)
        {
            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    var services = TryGetServiceProvider();
                    var passkeyService = services?.GetService(typeof(IPasskeyService)) as IPasskeyService
                        ?? services?.GetService(typeof(PasskeyService)) as IPasskeyService;

                    if (passkeyService == null)
                    {
                        Debug.WriteLine("[VaultViewModel] PasskeyService not available");
                        return;
                    }

                    var credentialId = Convert.FromBase64String(e.CredentialId);
                    byte[] challenge = new byte[32];
                    System.Security.Cryptography.RandomNumberGenerator.Fill(challenge);

                    bool ok = await passkeyService.AuthenticateAsync(credentialId, e.Domain, challenge);
                    if (ok)
                    {
                        Debug.WriteLine($"[VaultViewModel] Passkey authenticated for {e.Domain}");
                        if (_autoInjectService != null)
                        {
                            await _autoInjectService.AutoFillAsync(e.CredentialId, autoSubmit: false);
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[VaultViewModel] Passkey authentication failed for {e.Domain}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[VaultViewModel] Passkey handler error: {ex.Message}");
                }
            });
        }

        private Window? GetOwnerWindowForDialog()
        {
            return _ownerWindow ??
                   (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        }

        #endregion
    }
}
