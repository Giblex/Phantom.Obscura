using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PhantomVault.Core.Models.Security;
using PhantomVault.Core.Services.Security;
using Xunit;

namespace PhantomVault.Core.Tests.Security
{
    /// <summary>
    /// Integration tests for DefenceEngine including:
    /// - Threat event processing
    /// - Rule matching and execution
    /// - Cooldown enforcement
    /// - Defensive action execution
    /// - Incident scoring and state transitions
    /// </summary>
    public class DefenceEngineIntegrationTests
    {
        #region Test Helpers and Mocks

        /// <summary>
        /// Mock auth controller for testing defensive actions
        /// </summary>
        private class TestAuthController : IAuthController
        {
            public List<string> ExecutedActions { get; } = new();
            public TimeSpan? LastDelay { get; private set; }
            public TimeSpan? LastLockoutDuration { get; private set; }
            public bool PhantomKeyRequired { get; private set; }
            public string? ReauthReason { get; private set; }

            public Task AddAuthenticationDelayAsync(TimeSpan delay)
            {
                ExecutedActions.Add($"AddDelay:{delay.TotalSeconds}s");
                LastDelay = delay;
                return Task.CompletedTask;
            }

            public Task EnforceTempLockoutAsync(TimeSpan duration)
            {
                ExecutedActions.Add($"TempLockout:{duration.TotalMinutes}m");
                LastLockoutDuration = duration;
                return Task.CompletedTask;
            }

            public void RequirePhantomKeyForNextUnlock()
            {
                ExecutedActions.Add("RequirePhantomKey");
                PhantomKeyRequired = true;
            }

            public void RequireReauthentication(string reason)
            {
                ExecutedActions.Add($"RequireReauth:{reason}");
                ReauthReason = reason;
            }
        }

        /// <summary>
        /// Mock vault controller for testing vault-level defensive actions
        /// </summary>
        private class TestVaultController : IVaultController
        {
            public List<string> ExecutedActions { get; } = new();
            public bool DecoyVaultActive { get; private set; }
            public bool ReadOnlyMode { get; private set; }

            public bool IsReadOnly => ReadOnlyMode;
            public bool IsDecoyActive => DecoyVaultActive;
            public PhantomVault.Core.Models.VaultDatabase? ActiveDecoyDatabase { get; private set; }

            public Task SwitchToDecoyVaultAsync()
            {
                ExecutedActions.Add("SwitchToDecoyVault");
                DecoyVaultActive = true;
                ActiveDecoyDatabase = new PhantomVault.Core.Models.VaultDatabase { VaultName = "DecoyVault" };
                return Task.CompletedTask;
            }

            public void EnterReadOnlyMode()
            {
                ExecutedActions.Add("EnterReadOnlyMode");
                ReadOnlyMode = true;
            }

            public void ExitReadOnlyMode()
            {
                ExecutedActions.Add("ExitReadOnlyMode");
                ReadOnlyMode = false;
            }
        }

        /// <summary>
        /// Mock system security controller for testing system-level defensive actions
        /// </summary>
        private class TestSystemSecurityController : ISystemSecurityController
        {
            public List<string> ExecutedActions { get; } = new();
            public bool ClipboardCleared { get; private set; }
            public bool CachesScrubbed { get; private set; }

            public Task ClearClipboardAsync()
            {
                ExecutedActions.Add("ClearClipboard");
                ClipboardCleared = true;
                return Task.CompletedTask;
            }

            public void ScrubSensitiveCaches()
            {
                ExecutedActions.Add("ScrubCaches");
                CachesScrubbed = true;
            }
        }

        #endregion

        #region Basic Threat Processing Tests

        [Fact]
        public void RaiseThreat_WithMatchingRule_ExecutesDefensiveActions()
        {
            // Arrange
            var authController = new TestAuthController();
            var vaultController = new TestVaultController();
            var systemController = new TestSystemSecurityController();

            var rules = new List<DefenceRule>
            {
                new DefenceRule(
                    id: "rule1",
                    triggerType: ThreatType.FailedLoginBurst,
                    minLevel: ThreatLevel.Warning,
                    actions: new[] { DefenceActionType.AddDelay, DefenceActionType.ScrubShortLivedData },
                    cooldown: null,
                    isEnabled: true)
            };

            var engine = new DefenceEngine(
                rules,
                authController,
                vaultController,
                systemController);

            // Act
            var threat = new ThreatEvent(
                ThreatType.FailedLoginBurst,
                ThreatLevel.Warning,
                "Multiple failed login attempts detected");

            engine.RaiseThreat(threat);

            // Give async execution time to complete
            Thread.Sleep(100);

            // Assert
            Assert.Contains(authController.ExecutedActions, a => a.StartsWith("AddDelay"));
            Assert.Contains(systemController.ExecutedActions, a => a == "ScrubCaches");
            Assert.NotNull(authController.LastDelay);
            Assert.True(authController.LastDelay.Value.TotalSeconds >= 1);
        }

        [Fact]
        public void RaiseThreat_WithNoMatchingRules_DoesNotExecuteActions()
        {
            // Arrange
            var authController = new TestAuthController();
            var vaultController = new TestVaultController();
            var systemController = new TestSystemSecurityController();

            var rules = new List<DefenceRule>
            {
                new DefenceRule(id: "rule1", triggerType: ThreatType.IntegrityMismatch, minLevel: ThreatLevel.Critical, actions: new[] { DefenceActionType.SwitchToDecoyVault }, cooldown: null, isEnabled: true)
            };

            var engine = new DefenceEngine(
                rules,
                authController,
                vaultController,
                systemController);

            // Act - different threat type
            var threat = new ThreatEvent(
                ThreatType.FailedLoginBurst,
                ThreatLevel.Warning,
                "No matching rule");

            engine.RaiseThreat(threat);
            Thread.Sleep(100);

            // Assert
            Assert.Empty(authController.ExecutedActions);
            Assert.Empty(vaultController.ExecutedActions);
            Assert.Empty(systemController.ExecutedActions);
        }

        [Fact]
        public void RaiseThreat_WithDisabledRule_DoesNotExecuteActions()
        {
            // Arrange
            var authController = new TestAuthController();
            var vaultController = new TestVaultController();
            var systemController = new TestSystemSecurityController();

            var rules = new List<DefenceRule>
            {
                new DefenceRule(
                    id: "rule1",
                    triggerType: ThreatType.FailedLoginBurst,
                    minLevel: ThreatLevel.Warning,
                    actions: new[] { DefenceActionType.AddDelay },
                    cooldown: null,
                    isEnabled: false) // Disabled!
            };

            var engine = new DefenceEngine(
                rules,
                authController,
                vaultController,
                systemController);

            // Act
            var threat = new ThreatEvent(
                ThreatType.FailedLoginBurst,
                ThreatLevel.Warning,
                "Rule is disabled");

            engine.RaiseThreat(threat);
            Thread.Sleep(100);

            // Assert
            Assert.Empty(authController.ExecutedActions);
        }

        #endregion

        #region Cooldown Tests

        [Fact]
        public void RaiseThreat_WithCooldown_PreventsRepeatedExecution()
        {
            // Arrange
            var authController = new TestAuthController();
            var vaultController = new TestVaultController();
            var systemController = new TestSystemSecurityController();

            var rules = new List<DefenceRule>
            {
                new DefenceRule(
                    id: "rule1",
                    triggerType: ThreatType.FailedLoginBurst,
                    minLevel: ThreatLevel.Warning,
                    actions: new[] { DefenceActionType.AddDelay },
                    cooldown: TimeSpan.FromSeconds(5), // 5 second cooldown
                    isEnabled: true)
            };

            var engine = new DefenceEngine(
                rules,
                authController,
                vaultController,
                systemController);

            var threat = new ThreatEvent(
                ThreatType.FailedLoginBurst,
                ThreatLevel.Warning,
                "Test cooldown");

            // Act - First trigger
            engine.RaiseThreat(threat);
            Thread.Sleep(100);
            int firstCount = authController.ExecutedActions.Count;

            // Act - Second trigger within cooldown period
            engine.RaiseThreat(threat);
            Thread.Sleep(100);
            int secondCount = authController.ExecutedActions.Count;

            // Assert - should not execute second time
            Assert.Equal(1, firstCount);
            Assert.Equal(1, secondCount); // Should still be 1
        }

        [Fact]
        public void RaiseThreat_AfterCooldownExpires_AllowsReexecution()
        {
            // Arrange
            var authController = new TestAuthController();
            var vaultController = new TestVaultController();
            var systemController = new TestSystemSecurityController();

            var rules = new List<DefenceRule>
            {
                new DefenceRule(
                    id: "rule1",
                    triggerType: ThreatType.FailedLoginBurst,
                    minLevel: ThreatLevel.Warning,
                    actions: new[] { DefenceActionType.AddDelay },
                    cooldown: TimeSpan.FromMilliseconds(200), // Very short cooldown for testing
                    isEnabled: true)
            };

            var engine = new DefenceEngine(
                rules,
                authController,
                vaultController,
                systemController);

            var threat = new ThreatEvent(
                ThreatType.FailedLoginBurst,
                ThreatLevel.Warning,
                "Test cooldown expiration");

            // Act - First trigger
            engine.RaiseThreat(threat);
            Thread.Sleep(100);
            int firstCount = authController.ExecutedActions.Count;

            // Wait for cooldown to expire
            Thread.Sleep(200);

            // Act - Second trigger after cooldown
            engine.RaiseThreat(threat);
            Thread.Sleep(100);
            int secondCount = authController.ExecutedActions.Count;

            // Assert - should execute both times
            Assert.Equal(1, firstCount);
            Assert.Equal(2, secondCount);
        }

        #endregion

        #region Threat Level Tests

        [Fact]
        public void RaiseThreat_BelowMinLevel_DoesNotTriggerRule()
        {
            // Arrange
            var authController = new TestAuthController();
            var vaultController = new TestVaultController();
            var systemController = new TestSystemSecurityController();

            var rules = new List<DefenceRule>
            {
                new DefenceRule(
                    id: "rule1",
                    triggerType: ThreatType.FailedLoginBurst,
                    minLevel: ThreatLevel.Critical, // Requires Critical
                    actions: new[] { DefenceActionType.SwitchToDecoyVault },
                    cooldown: null,
                    isEnabled: true)
            };

            var engine = new DefenceEngine(
                rules,
                authController,
                vaultController,
                systemController);

            // Act - Only Warning level
            var threat = new ThreatEvent(
                ThreatType.FailedLoginBurst,
                ThreatLevel.Warning,
                "Below minimum level");

            engine.RaiseThreat(threat);
            Thread.Sleep(100);

            // Assert
            Assert.Empty(vaultController.ExecutedActions);
            Assert.False(vaultController.DecoyVaultActive);
        }

        [Fact]
        public void RaiseThreat_AtOrAboveMinLevel_TriggersRule()
        {
            // Arrange
            var authController = new TestAuthController();
            var vaultController = new TestVaultController();
            var systemController = new TestSystemSecurityController();

            var rules = new List<DefenceRule>
            {
                new DefenceRule(
                    id: "rule1",
                    triggerType: ThreatType.FailedLoginBurst,
                    minLevel: ThreatLevel.Warning, // Requires Warning or higher
                    actions: new[] { DefenceActionType.TempLockout },
                    cooldown: null,
                    isEnabled: true)
            };

            var engine = new DefenceEngine(
                rules,
                authController,
                vaultController,
                systemController);

            // Act - Critical level (higher than Warning)
            var threat = new ThreatEvent(
                ThreatType.FailedLoginBurst,
                ThreatLevel.Critical,
                "Above minimum level");

            engine.RaiseThreat(threat);
            Thread.Sleep(100);

            // Assert
            Assert.Contains(authController.ExecutedActions, a => a.StartsWith("TempLockout"));
        }

        #endregion

        #region Defensive Action Tests

        [Fact]
        public void DefensiveAction_AddDelay_ScalesWithThreatLevel()
        {
            // Arrange
            var authController = new TestAuthController();
            var vaultController = new TestVaultController();
            var systemController = new TestSystemSecurityController();

            var rules = new List<DefenceRule>
            {
                new DefenceRule(id: "rule1", triggerType: ThreatType.FailedLoginBurst, minLevel: ThreatLevel.Info, actions: new[] { DefenceActionType.AddDelay }, cooldown: null, isEnabled: true)
            };

            var engine = new DefenceEngine(
                rules,
                authController,
                vaultController,
                systemController);

            // Act - Info level
            var infoThreat = new ThreatEvent(ThreatType.FailedLoginBurst, ThreatLevel.Info, "Info");
            engine.RaiseThreat(infoThreat);
            Thread.Sleep(100);
            var infoDelay = authController.LastDelay;

            // Act - Critical level
            authController.ExecutedActions.Clear();
            var criticalThreat = new ThreatEvent(ThreatType.FailedLoginBurst, ThreatLevel.Critical, "Critical");
            engine.RaiseThreat(criticalThreat);
            Thread.Sleep(100);
            var criticalDelay = authController.LastDelay;

            // Assert - Critical should have longer delay than Info
            Assert.NotNull(infoDelay);
            Assert.NotNull(criticalDelay);
            Assert.True(criticalDelay.Value > infoDelay.Value,
                $"Critical delay ({criticalDelay.Value.TotalSeconds}s) should be greater than Info delay ({infoDelay.Value.TotalSeconds}s)");
        }

        [Fact]
        public void DefensiveAction_TempLockout_ScalesWithThreatLevel()
        {
            // Arrange
            var authController = new TestAuthController();
            var vaultController = new TestVaultController();
            var systemController = new TestSystemSecurityController();

            var rules = new List<DefenceRule>
            {
                new DefenceRule(id: "rule1", triggerType: ThreatType.FailedLoginBurst, minLevel: ThreatLevel.Warning, actions: new[] { DefenceActionType.TempLockout }, cooldown: null, isEnabled: true)
            };

            var engine = new DefenceEngine(
                rules,
                authController,
                vaultController,
                systemController);

            // Act - Warning level
            var warningThreat = new ThreatEvent(ThreatType.FailedLoginBurst, ThreatLevel.Warning, "Warning");
            engine.RaiseThreat(warningThreat);
            Thread.Sleep(100);
            var warningLockout = authController.LastLockoutDuration;

            // Act - Critical level
            authController.ExecutedActions.Clear();
            var criticalThreat = new ThreatEvent(ThreatType.FailedLoginBurst, ThreatLevel.Critical, "Critical");
            engine.RaiseThreat(criticalThreat);
            Thread.Sleep(100);
            var criticalLockout = authController.LastLockoutDuration;

            // Assert - Critical should have longer lockout than Warning
            Assert.NotNull(warningLockout);
            Assert.NotNull(criticalLockout);
            Assert.True(criticalLockout.Value > warningLockout.Value,
                $"Critical lockout ({criticalLockout.Value.TotalMinutes}m) should be greater than Warning lockout ({warningLockout.Value.TotalMinutes}m)");
        }

        [Fact]
        public void DefensiveAction_SwitchToDecoyVault_ActivatesDecoy()
        {
            // Arrange
            var authController = new TestAuthController();
            var vaultController = new TestVaultController();
            var systemController = new TestSystemSecurityController();

            var rules = new List<DefenceRule>
            {
                new DefenceRule(id: "rule1", triggerType: ThreatType.IntegrityMismatch, minLevel: ThreatLevel.Critical, actions: new[] { DefenceActionType.SwitchToDecoyVault }, cooldown: null, isEnabled: true)
            };

            var engine = new DefenceEngine(
                rules,
                authController,
                vaultController,
                systemController);

            // Act
            var threat = new ThreatEvent(
                ThreatType.IntegrityMismatch,
                ThreatLevel.Critical,
                "Suspected compromise");

            engine.RaiseThreat(threat);
            Thread.Sleep(100);

            // Assert
            Assert.Contains("SwitchToDecoyVault", vaultController.ExecutedActions);
            Assert.True(vaultController.DecoyVaultActive);
        }

        [Fact]
        public void DefensiveAction_EnterReadOnlyMode_ActivatesReadOnly()
        {
            // Arrange
            var authController = new TestAuthController();
            var vaultController = new TestVaultController();
            var systemController = new TestSystemSecurityController();

            var rules = new List<DefenceRule>
            {
                new DefenceRule(id: "rule1", triggerType: ThreatType.ExcessiveExports, minLevel: ThreatLevel.Warning, actions: new[] { DefenceActionType.EnterReadOnlyMode }, cooldown: null, isEnabled: true)
            };

            var engine = new DefenceEngine(
                rules,
                authController,
                vaultController,
                systemController);

            // Act
            var threat = new ThreatEvent(
                ThreatType.ExcessiveExports,
                ThreatLevel.Warning,
                "Too many exports detected");

            engine.RaiseThreat(threat);
            Thread.Sleep(100);

            // Assert
            Assert.Contains("EnterReadOnlyMode", vaultController.ExecutedActions);
            Assert.True(vaultController.ReadOnlyMode);
        }

        [Fact]
        public void DefensiveAction_ScrubShortLivedData_ClearsSensitiveData()
        {
            // Arrange
            var authController = new TestAuthController();
            var vaultController = new TestVaultController();
            var systemController = new TestSystemSecurityController();

            var rules = new List<DefenceRule>
            {
                new DefenceRule(id: "rule1", triggerType: ThreatType.BehaviourDeviation, minLevel: ThreatLevel.Warning, actions: new[] { DefenceActionType.ScrubShortLivedData }, cooldown: null, isEnabled: true)
            };

            var engine = new DefenceEngine(
                rules,
                authController,
                vaultController,
                systemController);

            // Act
            var threat = new ThreatEvent(
                ThreatType.BehaviourDeviation,
                ThreatLevel.Warning,
                "Unusual behavior detected");

            engine.RaiseThreat(threat);
            Thread.Sleep(100);

            // Assert
            Assert.True(systemController.ClipboardCleared);
            Assert.True(systemController.CachesScrubbed);
        }

        #endregion

        #region Multiple Actions Test

        [Fact]
        public void RaiseThreat_WithMultipleActions_ExecutesAll()
        {
            // Arrange
            var authController = new TestAuthController();
            var vaultController = new TestVaultController();
            var systemController = new TestSystemSecurityController();

            var rules = new List<DefenceRule>
            {
                new DefenceRule(id: "rule1", triggerType: ThreatType.FailedLoginBurst, minLevel: ThreatLevel.Critical, actions: new[] { DefenceActionType.AddDelay, DefenceActionType.TempLockout, DefenceActionType.RequirePhantomKey, DefenceActionType.ScrubShortLivedData }, cooldown: null, isEnabled: true)
            };

            var engine = new DefenceEngine(
                rules,
                authController,
                vaultController,
                systemController);

            // Act
            var threat = new ThreatEvent(
                ThreatType.FailedLoginBurst,
                ThreatLevel.Critical,
                "Multiple actions test");

            engine.RaiseThreat(threat);
            Thread.Sleep(200);

            // Assert - all actions should have executed
            Assert.Contains(authController.ExecutedActions, a => a.StartsWith("AddDelay"));
            Assert.Contains(authController.ExecutedActions, a => a.StartsWith("TempLockout"));
            Assert.Contains(authController.ExecutedActions, a => a == "RequirePhantomKey");
            Assert.True(systemController.ClipboardCleared);
            Assert.True(systemController.CachesScrubbed);
        }

        #endregion

        #region Null Argument Tests

        [Fact]
        public void RaiseThreat_WithNullThreat_ThrowsArgumentNullException()
        {
            // Arrange
            var authController = new TestAuthController();
            var vaultController = new TestVaultController();
            var systemController = new TestSystemSecurityController();
            var rules = new List<DefenceRule>();

            var engine = new DefenceEngine(
                rules,
                authController,
                vaultController,
                systemController);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => engine.RaiseThreat(null!));
        }

        [Fact]
        public void Constructor_WithNullRules_ThrowsArgumentNullException()
        {
            // Arrange
            var authController = new TestAuthController();
            var vaultController = new TestVaultController();
            var systemController = new TestSystemSecurityController();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new DefenceEngine(
                null!,
                authController,
                vaultController,
                systemController));
        }

        [Fact]
        public void Constructor_WithNullAuthController_ThrowsArgumentNullException()
        {
            // Arrange
            var vaultController = new TestVaultController();
            var systemController = new TestSystemSecurityController();
            var rules = new List<DefenceRule>();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new DefenceEngine(
                rules,
                null!,
                vaultController,
                systemController));
        }

        #endregion
    }
}
