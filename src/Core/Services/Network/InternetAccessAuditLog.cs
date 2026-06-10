using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PhantomVault.Core.Services.Network
{
    /// <summary>
    /// In-memory ring-buffer of internet-access events. Surfaced in the UI
    /// (Settings → Privacy → Network Activity) so the user can audit who
    /// asked for internet, when, and why.
    ///
    /// Persistence is intentionally OFF by default — these events would
    /// otherwise be a continuous activity record. The user may opt to
    /// persist via Settings.
    /// </summary>
    public sealed class InternetAccessAuditLog
    {
        private const int MaxEntries = 256;
        private readonly object _lock = new();
        private readonly LinkedList<InternetAccessAuditEntry> _entries = new();

        public IReadOnlyList<InternetAccessAuditEntry> Snapshot()
        {
            lock (_lock)
            {
                return new ReadOnlyCollection<InternetAccessAuditEntry>(_entries.ToList());
            }
        }

        public void Append(InternetAccessAuditEntry entry)
        {
            lock (_lock)
            {
                _entries.AddLast(entry);
                while (_entries.Count > MaxEntries)
                    _entries.RemoveFirst();
            }
        }

        public void Clear()
        {
            lock (_lock) _entries.Clear();
        }
    }

    public enum InternetAccessAuditKind
    {
        Requested,
        Granted,
        Denied,
        Used,
        Blocked,
        Revoked,
        Expired,
        PinFailure,
    }

    public sealed record InternetAccessAuditEntry(
        DateTimeOffset Timestamp,
        InternetAccessAuditKind Kind,
        string FeatureId,
        string UserVisibleReason,
        string? Host,
        string? Detail);
}
