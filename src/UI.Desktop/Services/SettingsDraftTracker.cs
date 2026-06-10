using System;
using System.Collections.Generic;
using ReactiveUI;

namespace PhantomVault.UI.Services
{

    public class SettingsDraftTracker : ReactiveObject
    {

        private readonly Dictionary<string, (Action Commit, Action Discard)> _pending
            = new Dictionary<string, (Action, Action)>(StringComparer.Ordinal);
        private readonly object _lock = new object();
        private bool _hasUnsavedChanges;

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set => this.RaiseAndSetIfChanged(ref _hasUnsavedChanges, value);
        }

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

        public void ClearKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (_lock)
            {
                _pending.Remove(key);
                if (_pending.Count == 0)
                {

                }
            }
            RecomputeFlag();
        }

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

