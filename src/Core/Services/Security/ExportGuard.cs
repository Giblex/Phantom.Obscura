using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace PhantomVault.Core.Services.Security
{

    public sealed class ExportGuard : IExportGuard
    {
        private readonly IDefenceEngine? _defenceEngine;
        private readonly IDefenceSettingsService? _defenceSettings;
        private readonly ILogger<ExportGuard>? _logger;
        private readonly List<ExportEvent> _exportEvents = new();
        private readonly object _lock = new();

        // Rule id shared with SecuritySettingsViewModel.IsExportGuardEnabled, which is
        // what the Security Settings toggle writes. Without consulting it here the
        // toggle changes nothing.
        private const string RuleId = "excessive-exports";

        private const int MaxExportsPerHour = 3;
        private static readonly TimeSpan SlidingWindow = TimeSpan.FromHours(1);
        private static readonly TimeSpan CooldownDuration = TimeSpan.FromMinutes(30);

        private DateTimeOffset? _cooldownUntil;

        public ExportGuard(
            IDefenceEngine? defenceEngine = null,
            IDefenceSettingsService? defenceSettings = null,
            ILogger<ExportGuard>? logger = null)
        {
            _defenceEngine = defenceEngine;
            _defenceSettings = defenceSettings;
            _logger = logger;
        }

        private bool IsEnabled => _defenceSettings?.GetRuleEnabled(RuleId) ?? true;

        public bool CanExport(string exportType)
        {
            if (!IsEnabled) return true;

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

        public void RegisterExport(string exportType)
        {
            if (!IsEnabled) return;

            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;

                _exportEvents.RemoveAll(e => now - e.Timestamp > SlidingWindow);

                _exportEvents.Add(new ExportEvent
                {
                    ExportType = exportType,
                    Timestamp = now
                });

                _logger?.LogInformation("Export registered ({ExportType}). Total in last hour: {Count}",
                    exportType, _exportEvents.Count);

                if (_exportEvents.Count > MaxExportsPerHour)
                {
                    _cooldownUntil = now.Add(CooldownDuration);

                    _logger?.LogWarning("Excessive exports detected ({Count} in 1 hour). Entering cooldown until {CooldownEnd}",
                        _exportEvents.Count, _cooldownUntil.Value);

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

