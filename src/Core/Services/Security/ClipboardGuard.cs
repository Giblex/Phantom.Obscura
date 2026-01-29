using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Implements clipboard copy throttling with threat detection.
    /// Tracks copy events in a 1-minute sliding window and enforces cooldown when threshold exceeded.
    /// </summary>
    public sealed class ClipboardGuard : IClipboardGuard
    {
        private readonly IDefenceEngine? _defenceEngine;
        private readonly ILogger<ClipboardGuard>? _logger;
        private readonly List<CopyEvent> _copyEvents = new();
        private readonly object _lock = new();

        private const int MaxCopiesPerMinute = 10;
        private static readonly TimeSpan SlidingWindow = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan CooldownDuration = TimeSpan.FromMinutes(2);

        private DateTimeOffset? _cooldownUntil;

        public ClipboardGuard(IDefenceEngine? defenceEngine = null, ILogger<ClipboardGuard>? logger = null)
        {
            _defenceEngine = defenceEngine;
            _logger = logger;
        }

        /// <summary>
        /// Checks if copying is allowed (not in cooldown).
        /// </summary>
        public bool CanCopy()
        {
            lock (_lock)
            {
                if (_cooldownUntil.HasValue && DateTimeOffset.UtcNow < _cooldownUntil.Value)
                {
                    _logger?.LogWarning("Clipboard copy blocked - cooldown active until {CooldownEnd}", _cooldownUntil.Value);
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Registers a copy event and checks for excessive copying.
        /// </summary>
        public void RegisterCopy(string entryId)
        {
            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;

                // Remove events outside sliding window
                _copyEvents.RemoveAll(e => now - e.Timestamp > SlidingWindow);

                // Add new event
                _copyEvents.Add(new CopyEvent
                {
                    EntryId = entryId,
                    Timestamp = now
                });

                _logger?.LogInformation("Clipboard copy registered for entry {EntryId}. Total in last minute: {Count}", 
                    entryId, _copyEvents.Count);

                // Check threshold
                if (_copyEvents.Count > MaxCopiesPerMinute)
                {
                    _cooldownUntil = now.Add(CooldownDuration);

                    _logger?.LogWarning("Excessive clipboard copies detected ({Count} in 1 minute). Entering cooldown until {CooldownEnd}", 
                        _copyEvents.Count, _cooldownUntil.Value);

                    // Raise threat to Defence Engine
                    _defenceEngine?.RaiseThreat(new ThreatEvent(
                        ThreatType.HighRiskEntryFlood,
                        ThreatLevel.Warning,
                        $"Clipboard copies in last minute: {_copyEvents.Count}. Cooldown activated for {CooldownDuration.TotalMinutes} minutes."
                    ));
                }
            }
        }

        private sealed class CopyEvent
        {
            public string EntryId { get; set; } = string.Empty;
            public DateTimeOffset Timestamp { get; set; }
        }
    }
}
