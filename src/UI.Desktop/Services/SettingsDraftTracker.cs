using System;
using System.Collections.Generic;
using ReactiveUI;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Tracks pending settings changes that have been entered in the UI but
    /// not yet persisted. Each opt-in property setter calls <see cref="Stage"/>
    /// with a commit action (write to <c>SettingsService</c>) and a discard
    /// action (rewind the in-memory UI value back to its last committed
    /// state). The settings overlay's Save / Discard buttons drive
    /// <see cref="CommitAll"/> / <see cref="DiscardAll"/>, and the overlay
    /// close intercept uses <see cref="HasUnsavedChanges"/> to decide whether
    /// to prompt.
    ///
    /// This is intentionally decoupled from any view-model boundary so that
    /// settings spread across <c>VaultViewModel</c> AND per-tab VMs can all
    /// stage into the same tracker — i.e. it works regardless of whether the
    /// per-tab VM refactor has happened yet.
    /// </summary>
    public class SettingsDraftTracker : ReactiveObject
    {
        // Most-recent commit/discard per key — re-staging the same key replaces
        // the previous pair rather than queueing duplicate writes. Keyed
        // dictionary is fine because each setting has a stable string id.
        private readonly Dictionary<string, (Action Commit, Action Discard)> _pending
            = new Dictionary<string, (Action, Action)>(StringComparer.Ordinal);
        private readonly object _lock = new object();
        private bool _hasUnsavedChanges;

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set => this.RaiseAndSetIfChanged(ref _hasUnsavedChanges, value);
        }

        /// <summary>
        /// Stage a pending change. <paramref name="commit"/> will run when the
        /// user clicks Save; <paramref name="discard"/> will run when the user
        /// clicks Cancel / Discard. Re-staging the same <paramref name="key"/>
        /// replaces the prior pair, so toggling a checkbox back and forth
        /// doesn't accumulate work.
        /// </summary>
        public void Stage(string key, Action commit, Action discard)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("key required", nameof(key));
            if (commit == null) throw new ArgumentNullException(nameof(commit));
            if (discard == null) throw new ArgumentNullException(nameof(discard));

            lock (_lock)
            {
                _pending[key] = (commit, discard);
            }
            HasUnsavedChanges = true;
        }

        /// <summary>
        /// Drop a pending change without running its commit or discard — used
        /// when the staged value happens to equal the already-committed value
        /// (e.g. user toggled twice and ended back at the original state).
        /// </summary>
        public void ClearKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (_lock)
            {
                _pending.Remove(key);
                if (_pending.Count == 0)
                {
                    // Defer HasUnsavedChanges update until outside the lock to
                    // keep listeners off the lock path.
                }
            }
            RecomputeFlag();
        }

        /// <summary>
        /// Run every staged commit and clear the queue. Returns the number of
        /// commits executed. Exceptions from individual commits are
        /// swallowed so one bad setter can't strand others — but the failing
        /// commit is logged via Debug.WriteLine.
        /// </summary>
        public int CommitAll()
        {
            (Action Commit, Action Discard)[] snapshot;
            lock (_lock)
            {
                snapshot = new (Action, Action)[_pending.Count];
                int i = 0;
                foreach (var kv in _pending) snapshot[i++] = kv.Value;
                _pending.Clear();
            }
            HasUnsavedChanges = false;

            int ran = 0;
            foreach (var (commit, _) in snapshot)
            {
                try { commit(); ran++; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SettingsDraftTracker] commit failed: {ex.Message}");
                }
            }
            return ran;
        }

        /// <summary>
        /// Run every staged discard (rewinding UI values) and clear the
        /// queue. Returns the number of discards executed.
        /// </summary>
        public int DiscardAll()
        {
            (Action Commit, Action Discard)[] snapshot;
            lock (_lock)
            {
                snapshot = new (Action, Action)[_pending.Count];
                int i = 0;
                foreach (var kv in _pending) snapshot[i++] = kv.Value;
                _pending.Clear();
            }
            HasUnsavedChanges = false;

            int ran = 0;
            foreach (var (_, discard) in snapshot)
            {
                try { discard(); ran++; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SettingsDraftTracker] discard failed: {ex.Message}");
                }
            }
            return ran;
        }

        private void RecomputeFlag()
        {
            bool any;
            lock (_lock) any = _pending.Count > 0;
            HasUnsavedChanges = any;
        }
    }
}
