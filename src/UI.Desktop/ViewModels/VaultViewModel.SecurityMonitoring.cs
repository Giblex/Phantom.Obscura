// Partial class — Security Monitoring section of VaultViewModel
using System;
using System.Diagnostics;
using Avalonia.Threading;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.Security;
using PhantomVault.UI.Desktop.Services;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.ViewModels
{
    public sealed partial class VaultViewModel
    {
        #region Security Methods

        private void OnThreatLevelChanged(object? sender, ThreatLevelChangedEventArgs e)
        {
            CurrentThreatLevel = e.CurrentLevel;

            SecurityStatus = e.CurrentLevel switch
            {
                SecurityThreatLevel.None => "Secure",
                SecurityThreatLevel.Low => "Low Risk",
                SecurityThreatLevel.Medium => "Medium Risk",
                SecurityThreatLevel.High => "High Risk",
                SecurityThreatLevel.Critical => "Critical Threat",
                _ => "Unknown"
            };

            ShowSecurityAlert = e.CurrentLevel >= SecurityThreatLevel.Medium;

            Debug.WriteLine($"[Security] Threat level changed: {e.PreviousLevel} → {e.CurrentLevel}");
            Debug.WriteLine($"[Security] Tamper: {e.Check.TamperCheckResult.GetDescription()}");
            Debug.WriteLine($"[Security] Keylogging: {e.Check.KeyloggingCheckResult.GetDescription()}");
        }

        private async void OnCriticalThreatDetected(object? sender, CriticalThreatEventArgs e)
        {
            if (_securityCoordinator == null)
                return;

            var action = await _securityCoordinator.RespondToThreatAsync(e.Check);

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await _dialogService.ShowWarningAsync(
                    "Security Threat Detected",
                    action.Message,
                    _ownerWindow);
            });

            if (action.ShouldLockVault)
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    StatusMessage = "Vault locked due to security threat";

                    if (!string.IsNullOrEmpty(_mountPath))
                    {
                        try
                        {
                            var result = await _vaultLockDurationService.LockVaultAsync(LockReason.SecurityThreat);
                            if (result.Success)
                            {
                                _items.Clear();
                                _ownerWindow?.Close();
                            }
                        }
                        catch (Exception ex)
                        {
                            Serilog.Log.Fatal(ex, "[SecurityMonitoring] Vault failed to lock after a security threat was detected.");
                            RecentIssuesLog.Instance.Record(IssueSeverity.Error, "Vault did not lock", "A security threat was detected, but automatic locking could not be confirmed. Close the vault immediately.");
                        }
                    }
                });
            }

            if (action.ShouldExitApplication)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Environment.Exit(1);
                });
            }
        }

        private void StartSecurityMonitoring()
        {
            if (_securityCoordinator == null)
                return;

            _securityCoordinator.EnableMaximumSecurity();
            SecurityStatus = "Secure";
            CurrentThreatLevel = SecurityThreatLevel.None;
            ShowSecurityAlert = false;

            Debug.WriteLine("[Security] Monitoring started");
        }

        private void StopSecurityMonitoring()
        {
            if (_securityCoordinator == null)
                return;

            _securityCoordinator.StopMonitoring();
            SecurityStatus = "Inactive";
            CurrentThreatLevel = SecurityThreatLevel.None;
            ShowSecurityAlert = false;

            Debug.WriteLine("[Security] Monitoring stopped");
        }

        #endregion
    }
}
