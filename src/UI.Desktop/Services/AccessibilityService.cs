using System;

namespace PhantomVault.UI.Services
{

    public sealed class AccessibilityService
    {
        private static readonly Lazy<AccessibilityService> _instance = new Lazy<AccessibilityService>(() => new AccessibilityService());

        private bool _reduceMotion;
        private bool _useHighContrast;
        private bool _largeTooltips;
        private bool _screenReaderOptimizations;

        public static AccessibilityService Instance => _instance.Value;

        public event EventHandler? SettingsChanged;

        private AccessibilityService()
        {

            _reduceMotion = false;
            _useHighContrast = false;
            _largeTooltips = false;

            TryDetectOsPreferences();
        }

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

        public bool ScreenReaderOptimizations
        {
            get => _screenReaderOptimizations;
            set
            {
                if (_screenReaderOptimizations != value)
                {
                    _screenReaderOptimizations = value;
                    OnSettingsChanged();
                }
            }
        }

        private void TryDetectOsPreferences()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {

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

                            _reduceMotion = string.Equals(output, "false", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                    catch
                    {

                    }
                }
            }
            catch
            {

            }
        }

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

