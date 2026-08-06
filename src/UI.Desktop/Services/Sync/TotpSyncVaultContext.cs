using PhantomVault.Core.Services.Sync;

namespace PhantomVault.UI.Services.Sync
{
    public sealed class TotpSyncVaultContext : ITotpSyncVaultContext
    {
        private readonly object _lock = new();
        private bool _isUnlocked;
        private bool _syncTotpEnabled;
        private ITotpSyncBridge? _bridge;

        public bool IsUnlocked
        {
            get { lock (_lock) return _isUnlocked; }
        }

        public bool SyncTotpEnabled
        {
            get { lock (_lock) return _syncTotpEnabled; }
        }

        public ITotpSyncBridge? Bridge
        {
            get { lock (_lock) return _bridge; }
        }

        public void SetUnlocked(ITotpSyncBridge bridge, bool syncTotpEnabled)
        {
            lock (_lock)
            {
                _bridge = bridge;
                _isUnlocked = true;
                _syncTotpEnabled = syncTotpEnabled;
            }
        }

        public void SetLocked()
        {
            lock (_lock)
            {
                _bridge = null;
                _isUnlocked = false;
                _syncTotpEnabled = false;
            }
        }
    }
}
