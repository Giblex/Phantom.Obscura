using System;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Global service for managing accessibility settings across the application.
    /// Provides a centralized source of truth for motion reduction, contrast, and other accessibility preferences.
    /// </summary>
    public sealed class AccessibilityService
    {
        private static readonly Lazy<AccessibilityService> _instance = new Lazy<AccessibilityService>(() => new AccessibilityService());

        private bool _reduceMotion;
        private bool _useHighContrast;
        private bool _largeTooltips;

        /// <summary>
        /// Gets the singleton instance of the AccessibilityService.
        /// </summary>
        public static AccessibilityService Instance => _instance.Value;

        /// <summary>
        /// Event raised when accessibility settings change.
        /// </summary>
        public event EventHandler? SettingsChanged;

        private AccessibilityService()
        {
            // Default values
            _reduceMotion = false;
            _useHighContrast = false;
            _largeTooltips = false;

            // Try to detect OS-level reduce motion preference
            TryDetectOsPreferences();
        }

        /// <summary>
        /// Gets or sets whether motion and animations should be reduced for accessibility.
        /// </summary>
        public bool ReduceMotion
        {
            get => _reduceMotion;
            set
            {
                if (_reduceMotion != value)
                {
                    _reduceMotion = value;
                    OnSettingsChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets whether high contrast mode is enabled.
        /// </summary>
        public bool UseHighContrast
        {
            get => _useHighContrast;
            set
            {
                if (_useHighContrast != value)
                {
                    _useHighContrast = value;
                    OnSettingsChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets whether large tooltips should be shown.
        /// </summary>
        public bool LargeTooltips
        {
            get => _largeTooltips;
            set
            {
                if (_largeTooltips != value)
                {
                    _largeTooltips = value;
                    OnSettingsChanged();
                }
            }
        }

        /// <summary>
        /// Attempts to detect OS-level accessibility preferences.
        /// Currently supports Windows 10+ motion reduction detection.
        /// </summary>
        private void TryDetectOsPreferences()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // Check Windows Registry for motion reduction setting
                    // HKEY_CURRENT_USER\Control Panel\Accessibility\ReduceMotion
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        @"Control Panel\Accessibility", false);

                    if (key != null)
                    {
                        var value = key.GetValue("ReduceMotion");
                        if (value is int intValue)
                        {
                            _reduceMotion = intValue == 1;
                        }
                    }
                }
                else if (OperatingSystem.IsMacOS())
                {
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo("defaults", "read com.apple.universalaccess reduceMotion")
                        {
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var proc = System.Diagnostics.Process.Start(psi);
                        if (proc != null)
                        {
                            var output = proc.StandardOutput.ReadToEnd().Trim();
                            proc.WaitForExit(2000);
                            _reduceMotion = output == "1";
                        }
                    }
                    catch
                    {
                        // defaults command may fail if preference doesn't exist
                    }
                }
                else if (OperatingSystem.IsLinux())
                {
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo("gsettings", "get org.gnome.desktop.interface enable-animations")
                        {
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var proc = System.Diagnostics.Process.Start(psi);
                        if (proc != null)
                        {
                            var output = proc.StandardOutput.ReadToEnd().Trim();
                            proc.WaitForExit(2000);
                            // enable-animations=false means reduce motion is ON
                            _reduceMotion = string.Equals(output, "false", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                    catch
                    {
                        // gsettings may not be available on non-GNOME desktops
                    }
                }
            }
            catch
            {
                // If detection fails, use default (false)
            }
        }

        /// <summary>
        /// Updates all accessibility settings at once.
        /// </summary>
        /// <param name="reduceMotion">Reduce motion and animations</param>
        /// <param name="useHighContrast">Use high contrast theme</param>
        /// <param name="largeTooltips">Show larger tooltips</param>
        public void UpdateSettings(bool reduceMotion, bool useHighContrast, bool largeTooltips)
        {
            bool changed = false;

            if (_reduceMotion != reduceMotion)
            {
                _reduceMotion = reduceMotion;
                changed = true;
            }

            if (_useHighContrast != useHighContrast)
            {
                _useHighContrast = useHighContrast;
                changed = true;
            }

            if (_largeTooltips != largeTooltips)
            {
                _largeTooltips = largeTooltips;
                changed = true;
            }

            if (changed)
            {
                OnSettingsChanged();
            }
        }

        private void OnSettingsChanged()
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
