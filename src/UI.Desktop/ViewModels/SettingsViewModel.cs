using System;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Styling;
using ReactiveUI;
using PhantomVault.Core.Services.Security;
using PhantomVault.UI.Services;
using PhantomVault.UI.ViewModels.Settings;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model for the settings window. Provides a toggle for dark/light
    /// theme and could be extended with additional preferences. Changing
    /// themes affects the entire application.
    /// </summary>
    public sealed class SettingsViewModel : ReactiveObject
    {
        private readonly IDefenceSettingsService? _defenceSettings;
        private bool _isDarkTheme = false;

        public SettingsViewModel(IDefenceSettingsService? defenceSettingsService = null)
        {
            _defenceSettings = defenceSettingsService;
            _isDarkTheme = SettingsService.Load().IsDarkTheme;

            // Initialize Password Security ViewModel
            PasswordSecurityViewModel = new PasswordSecurityViewModel();

            ToggleThemeCommand = ReactiveCommand.Create(() =>
            {
                IsDarkTheme = !IsDarkTheme;
                // Use App.SetTheme to keep theme switching consistent
                try
                {
                    if (Avalonia.Application.Current is App app)
                    {
                        app.SetTheme(IsDarkTheme ? "dark" : "light");
                        var settings = SettingsService.Load();
                        settings.IsDarkTheme = IsDarkTheme;
                        SettingsService.Save(settings);
                    }
                }
                catch
                {
                    ApplyTheme();
                }
            });
        }

        public PasswordSecurityViewModel PasswordSecurityViewModel { get; }

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set => this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
        }

        public ReactiveCommand<Unit, Unit> ToggleThemeCommand { get; }

        private void ApplyTheme()
        {
            if (Application.Current == null) return;
            Application.Current.RequestedThemeVariant = IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        // ===== Defence Engine Settings =====

        /// <summary>
        /// Require Phantom Key / extra checks on new devices.
        /// Maps to "new-device" rule.
        /// </summary>
        public bool IsNewDeviceProtectionEnabled
        {
            get => _defenceSettings?.GetRuleEnabled("new-device") ?? true;
            set
            {
                _defenceSettings?.SetRuleEnabled("new-device", value);
                this.RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Open in Safe (read-only) mode if tampering suspected.
        /// Maps to "integrity-critical" rule.
        /// </summary>
        public bool IsIntegritySafeModeEnabled
        {
            get => _defenceSettings?.GetRuleEnabled("integrity-critical") ?? true;
            set
            {
                _defenceSettings?.SetRuleEnabled("integrity-critical", value);
                this.RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Limit rapid copy-to-clipboard of secrets.
        /// Maps to clipboard guard logic (not a specific rule, but we'll use a conceptual flag).
        /// For now, we can create a virtual rule ID for clipboard guard.
        /// </summary>
        public bool IsClipboardGuardEnabled
        {
            get => _defenceSettings?.GetRuleEnabled("clipboard-guard") ?? true;
            set
            {
                _defenceSettings?.SetRuleEnabled("clipboard-guard", value);
                this.RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Limit repeated vault exports in short time.
        /// Maps to "excessive-exports" rule.
        /// </summary>
        public bool IsExportGuardEnabled
        {
            get => _defenceSettings?.GetRuleEnabled("excessive-exports") ?? true;
            set
            {
                _defenceSettings?.SetRuleEnabled("excessive-exports", value);
                this.RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Detect and respond to abnormal session behaviour.
        /// Maps to "behavior-deviation" rule.
        /// </summary>
        public bool IsBehaviourDeviationProtectionEnabled
        {
            get => _defenceSettings?.GetRuleEnabled("behavior-deviation") ?? true;
            set
            {
                _defenceSettings?.SetRuleEnabled("behavior-deviation", value);
                this.RaisePropertyChanged();
            }
        }

        // ===== PhantomRecovery Developer Mode =====

        /// <summary>
        /// Enable developer mode for PhantomRecovery integration.
        /// Provides simulated USB paths and default credentials for testing.
        /// </summary>
        public bool IsRecoveryDeveloperModeEnabled
        {
            get => RecoveryDeveloperMode.IsEnabled;
            set
            {
                RecoveryDeveloperMode.IsEnabled = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(RecoveryDeveloperVaultPath));
                this.RaisePropertyChanged(nameof(RecoveryDeveloperUsbPath));
            }
        }

        /// <summary>
        /// Developer vault path (read-only, computed from RecoveryDeveloperMode)
        /// </summary>
        public string RecoveryDeveloperVaultPath => RecoveryDeveloperMode.DeveloperVaultPath;

        /// <summary>
        /// Developer USB path (read-only, computed from RecoveryDeveloperMode)
        /// </summary>
        public string RecoveryDeveloperUsbPath => RecoveryDeveloperMode.DeveloperUsbPath;

        /// <summary>
        /// Skip USB binding checks in developer mode
        /// </summary>
        public bool SkipUsbBindingCheck
        {
            get => RecoveryDeveloperMode.SkipUsbBindingCheck;
            set
            {
                RecoveryDeveloperMode.SkipUsbBindingCheck = value;
                this.RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Enable verbose logging for recovery operations
        /// </summary>
        public bool RecoveryVerboseLogging
        {
            get => RecoveryDeveloperMode.VerboseLogging;
            set
            {
                RecoveryDeveloperMode.VerboseLogging = value;
                this.RaisePropertyChanged();
            }
        }
    }
}
