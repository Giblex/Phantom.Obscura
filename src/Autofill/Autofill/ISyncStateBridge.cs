using System;
using System.Collections.Generic;

namespace PhantomVault.Core.Services.Autofill
{
    /// <summary>
    /// Non-secret synchronization state shared between the Phantom Obscura desktop
    /// app and the browser extension. This intentionally carries NO credentials,
    /// passwords, keys, or TOTP secrets — only the cosmetic / configuration layer
    /// that is safe to mirror into browser-managed storage (chrome.storage.local).
    /// </summary>
    public sealed class SyncState
    {
        /// <summary>Active theme identifier (e.g. the runtime theme id).</summary>
        public string? ThemeId { get; set; }

        /// <summary>
        /// Free-form non-secret UI preferences (reduce-animations, density, etc.).
        /// Keys and values must never contain sensitive data.
        /// </summary>
        public Dictionary<string, string> UiPrefs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The autofill origin allowlist (extension origins permitted to talk to
        /// the native host). Non-secret; mirrors %APPDATA% origins.
        /// </summary>
        public List<string> EnabledOrigins { get; set; } = new();

        /// <summary>
        /// Optional TOTP issuer labels (issuer/account only — NEVER secrets).
        /// Empty unless an unlocked-vault bridge explicitly populates it; the
        /// host-readable file layer leaves this empty by design because the TOTP
        /// sync file lives on the USB vault and is only known to the unlocked app.
        /// </summary>
        public List<string> TotpIssuers { get; set; } = new();

        /// <summary>Timestamp of the last change to the source-of-truth file(s).</summary>
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    /// <summary>
    /// Bridges the browser extension's sync requests to the desktop app's existing
    /// file-backed sync source of truth (e.g. <c>%APPDATA%/PhantomVault/phantom-sync.json</c>,
    /// watched by <c>CrossAppSyncService</c>). The native messaging host calls this;
    /// it never reaches into the vault.
    /// </summary>
    public interface ISyncStateBridge
    {
        /// <summary>Reads the current non-secret sync state from the source of truth.</summary>
        SyncState Read();

        /// <summary>
        /// Applies an incoming non-secret sync state from the extension by writing
        /// theme / UI prefs back to the shared file. The desktop app's file watcher
        /// then picks up and applies the change. Secrets are ignored if present.
        /// </summary>
        void Apply(SyncState incoming);

        /// <summary>
        /// Raised when the underlying source-of-truth file(s) change, so the host
        /// can push an <c>onSyncChanged</c> notification to the extension.
        /// </summary>
        event EventHandler? Changed;
    }
}
