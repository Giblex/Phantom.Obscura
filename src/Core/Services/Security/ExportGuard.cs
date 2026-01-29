using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Implements export throttling with threat detection.
    /// Tracks export events in a 1-hour sliding window and enforces cooldown when threshold exceeded.
    /// </summary>
    public sealed class ExportGuard : IExportGuard
    {
        private readonly IDefenceEngine? _defenceEngine;
        private readonly ILogger<ExportGuard>? _logger;
        private readonly List<ExportEvent> _exportEvents = new();
        private readonly object _lock = new();

        private const int MaxExportsPerHour = 3;
        private static readonly TimeSpan SlidingWindow = TimeSpan.FromHours(1);
        private static readonly TimeSpan CooldownDuration = TimeSpan.FromMinutes(30);

        private DateTimeOffset? _cooldownUntil;

        public ExportGuard(IDefenceEngine? defenceEngine = null, ILogger<ExportGuard>? logger = null)
        {
            _defenceEngine = defenceEngine;
            _logger = logger;
        }

        /// <summary>
        /// Checks if export is allowed (not in cooldown).
        /// </summary>
        public bool CanExport(string exportType)
        {
            lock (_lock)
            {
                if (_cooldownUntil.HasValue && DateTimeOffset.UtcNow < _cooldownUntil.Value)
                {
                    _logger?.LogWarning("Export ({ExportType}) blocked - cooldown active until {CooldownEnd}", 
                        exportType, _cooldownUntil.Value);
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Registers an export event and checks for excessive exporting.
        /// </summary>
        public void RegisterExport(string exportType)
        {
            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;

                // Remove events outside sliding window
                _exportEvents.RemoveAll(e => now - e.Timestamp > SlidingWindow);

                // Add new event
                _exportEvents.Add(new ExportEvent
                {
                    ExportType = exportType,
                    Timestamp = now
                });

                _logger?.LogInformation("Export registered ({ExportType}). Total in last hour: {Count}", 
                    exportType, _exportEvents.Count);

                // Check threshold
                if (_exportEvents.Count > MaxExportsPerHour)
                {
                    _cooldownUntil = now.Add(CooldownDuration);

                    _logger?.LogWarning("Excessive exports detected ({Count} in 1 hour). Entering cooldown until {CooldownEnd}", 
                        _exportEvents.Count, _cooldownUntil.Value);

                    // Raise threat to Defence Engine
                    _defenceEngine?.RaiseThreat(new ThreatEvent(
                        ThreatType.ExcessiveExports,
                        ThreatLevel.Warning,
                        $"Exports in last hour: {_exportEvents.Count}. Cooldown activated for {CooldownDuration.TotalMinutes} minutes."
                    ));
                }
            }
        }

        private sealed class ExportEvent
        {
            public string ExportType { get; set; } = string.Empty;
            public DateTimeOffset Timestamp { get; set; }
        }
    }
}
