using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PhantomVault.Core.Models.Security;
using PhantomVault.Core.Services;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Central defence engine implementation. Processes threat events and executes
    /// defensive actions according to configured rules. Maintains cooldown tracking
    /// to prevent over-triggering of responses.
    /// </summary>
    public sealed class DefenceEngine : IDefenceEngine
    {
        private readonly IReadOnlyList<DefenceRule> _rules;
        private readonly IAuthController _authController;
        private readonly IVaultController _vaultController;
        private readonly ISystemSecurityController _systemSecurityController;
        private readonly ILogger<DefenceEngine>? _logger;

        // Tracks when each rule last fired to enforce cooldowns
        private readonly Dictionary<string, DateTimeOffset> _ruleLastFired = new();
        private readonly object _lock = new();
        
        // Incident scoring and manifest management
        private readonly IncidentState _incidentState = new();
        private readonly ManifestService? _manifestService;
        private string? _manifestPath;

        public DefenceEngine(
            IReadOnlyList<DefenceRule> rules,
            IAuthController authController,
            IVaultController vaultController,
            ISystemSecurityController systemSecurityController,
            ILogger<DefenceEngine>? logger = null,
            ManifestService? manifestService = null,
            string? manifestPath = null)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _authController = authController ?? throw new ArgumentNullException(nameof(authController));
            _vaultController = vaultController ?? throw new ArgumentNullException(nameof(vaultController));
            _systemSecurityController = systemSecurityController ?? throw new ArgumentNullException(nameof(systemSecurityController));
            _logger = logger;
            _manifestService = manifestService;
            _manifestPath = manifestPath;
        }

        /// <summary>
        /// Processes a threat event and executes matching defensive rules.
        /// </summary>
        public void RaiseThreat(ThreatEvent threat)
        {
            if (threat == null)
                throw new ArgumentNullException(nameof(threat));

            _logger?.LogInformation(
                "Threat raised: Type={ThreatType}, Level={ThreatLevel}, Details={Details}",
                threat.Type, threat.Level, threat.Details ?? "none");

            // Find all rules that match this threat
            var matchingRules = _rules
                .Where(r => r.IsEnabled && r.TriggerType == threat.Type && threat.Level >= r.MinLevel)
                .ToList();

            if (matchingRules.Count == 0)
            {
                _logger?.LogDebug("No matching rules found for threat {ThreatType}", threat.Type);
                return;
            }

            lock (_lock)
            {
                foreach (var rule in matchingRules)
                {
                    // Check cooldown
                    if (rule.Cooldown.HasValue && _ruleLastFired.TryGetValue(rule.Id, out var lastFired))
                    {
                        var elapsed = DateTimeOffset.UtcNow - lastFired;
                        if (elapsed < rule.Cooldown.Value)
                        {
                            _logger?.LogDebug(
                                "Rule {RuleId} still in cooldown (remaining: {Remaining})",
                                rule.Id, rule.Cooldown.Value - elapsed);
                            continue;
                        }
                    }

                    // Execute actions
                    _logger?.LogWarning(
                        "Executing defence rule {RuleId} for threat {ThreatType} ({ThreatLevel})",
                        rule.Id, threat.Type, threat.Level);

                    // Fire and forget async execution
                    _ = ExecuteActionsAsync(rule.Actions, threat);

                    // Update last fired timestamp
                    _ruleLastFired[rule.Id] = DateTimeOffset.UtcNow;
                }
            }
        }

        private void UpdateIncident(ThreatEvent threat)
        {
            var now = DateTimeOffset.UtcNow;
            var elapsed = now - _incidentState.LastUpdated;

            // Apply exponential time decay (30-minute half-life)
            if (elapsed.TotalMinutes > 0)
            {
                _incidentState.Score *= Math.Exp(-elapsed.TotalMinutes / 30.0);
            }

            // Calculate threat weight based on type and level
            var weight = CalculateThreatWeight(threat.Type, threat.Level);
            _incidentState.Score += weight;

            _logger?.LogInformation(
                "Incident score updated: {Score:F2} (added {Weight:F2} for {ThreatType}/{ThreatLevel})",
                _incidentState.Score, weight, threat.Type, threat.Level);

            // Determine new security state based on score thresholds
            var newState = _incidentState.Score switch
            {
                < 4.0 => VaultSecurityState.Normal,
                < 10.0 => VaultSecurityState.Suspicious,
                _ => VaultSecurityState.Compromised
            };

            // If state changed to Compromised, update manifest
            if (newState == VaultSecurityState.Compromised && _incidentState.State != VaultSecurityState.Compromised)
            {
                _logger?.LogCritical("Vault security state escalated to COMPROMISED - Setting RekeyRequired flag");
                
                if (_manifestService != null && !string.IsNullOrEmpty(_manifestPath))
                {
                    try
                    {
                        var manifest = _manifestService.ReadManifest(_manifestPath, null, null);
                        manifest.SecurityState = VaultSecurityState.Compromised;
                        manifest.RekeyRequired = true;
                        _manifestService.WriteManifest(manifest, _manifestPath, null, null);
                        _logger?.LogWarning("Manifest updated with Compromised state and RekeyRequired flag");
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Failed to update manifest after security state change");
                    }
                }
                else
                {
                    _logger?.LogWarning("Cannot update manifest - ManifestService or path not available");
                }
            }
            else if (newState != _incidentState.State)
            {
                _logger?.LogWarning(
                    "Vault security state changed: {OldState} → {NewState}",
                    _incidentState.State, newState);
            }

            _incidentState.State = newState;
            _incidentState.LastUpdated = now;
        }

        private double CalculateThreatWeight(ThreatType type, ThreatLevel level)
        {
            // Base weights by level
            var baseWeight = level switch
            {
                ThreatLevel.Info => 0.5,
                ThreatLevel.Warning => 1.5,
                ThreatLevel.Critical => 3.0,
                _ => 1.0
            };

            // Multipliers by threat type (more severe threats get higher multipliers)
            var typeMultiplier = type switch
            {
                ThreatType.FailedLoginBurst => 1.5,
                ThreatType.NewDeviceFingerprint => 0.8,
                ThreatType.IntegrityMismatch => 2.5,
                ThreatType.ExcessiveExports => 1.6,
                ThreatType.HighRiskEntryFlood => 1.4,
                ThreatType.BehaviourDeviation => 1.2,
                ThreatType.PhysicalRemoval => 2.0,
                _ => 1.0
            };

            return baseWeight * typeMultiplier;
        }

        private async Task ExecuteActionsAsync(DefenceActionType[] actions, ThreatEvent threat)
        {
            foreach (var action in actions)
            {
                try
                {
                    await ExecuteActionAsync(action, threat);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex,
                        "Failed to execute defence action {Action} for threat {ThreatType}",
                        action, threat.Type);
                }
            }
        }

        private async Task ExecuteActionAsync(DefenceActionType action, ThreatEvent threat)
        {
            switch (action)
            {
                case DefenceActionType.AddDelay:
                    // Progressively increase delay based on threat level
                    var delay = threat.Level switch
                    {
                        ThreatLevel.Info => TimeSpan.FromSeconds(1),
                        ThreatLevel.Warning => TimeSpan.FromSeconds(3),
                        ThreatLevel.Critical => TimeSpan.FromSeconds(10),
                        _ => TimeSpan.FromSeconds(2)
                    };
                    _logger?.LogInformation("Adding authentication delay: {Delay}", delay);
                    // Fire and forget - don't block threat processing
                    _ = _authController.AddAuthenticationDelayAsync(delay);
                    break;

                case DefenceActionType.TempLockout:
                    // Lock out based on threat level
                    var lockoutDuration = threat.Level switch
                    {
                        ThreatLevel.Warning => TimeSpan.FromMinutes(5),
                        ThreatLevel.Critical => TimeSpan.FromMinutes(30),
                        _ => TimeSpan.FromMinutes(1)
                    };
                    _logger?.LogWarning("Enforcing temporary lockout: {Duration}", lockoutDuration);
                    _ = _authController.EnforceTempLockoutAsync(lockoutDuration);
                    break;

                case DefenceActionType.RequirePhantomKey:
                    _logger?.LogWarning("Requiring PhantomKey for next unlock");
                    _authController.RequirePhantomKeyForNextUnlock();
                    break;

                case DefenceActionType.SwitchToDecoyVault:
                    _logger?.LogCritical("SWITCHING TO DECOY VAULT - Suspected compromise");
                    try
                    {
                        await _vaultController.SwitchToDecoyVaultAsync();
                    }
                    catch (Exception ex)
                    {
                        // If decoy vault activation fails, fall back to lock + wipe
                        _logger?.LogError(ex, "Decoy vault activation failed, falling back to lock + scrub");
                        _authController.RequireReauthentication("Security threat detected");
                        _systemSecurityController.ScrubSensitiveCaches();
                    }
                    break;

                case DefenceActionType.EnterReadOnlyMode:
                    _logger?.LogWarning("Entering read-only mode");
                    _vaultController.EnterReadOnlyMode();
                    break;

                case DefenceActionType.ScrubShortLivedData:
                    _logger?.LogInformation("Scrubbing sensitive caches and clipboard");
                    _ = _systemSecurityController.ClearClipboardAsync();
                    _systemSecurityController.ScrubSensitiveCaches();
                    break;

                default:
                    _logger?.LogWarning("Unknown defence action type: {Action}", action);
                    break;
            }
        }
    }
}
