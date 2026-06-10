using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Serilog;

namespace PhantomVault.UI.Services
{
    // Wires the "Start with Windows" preference to the per-user Run key. Uses the
    // current executable path so it survives moves/updates only if the path is
    // stable; re-applied on every enable to keep the path current.
    [SupportedOSPlatform("windows")]
    public static class WindowsStartupRegistration
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "PhantomObscura";

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                var value = key?.GetValue(ValueName) as string;
                return !string.IsNullOrEmpty(value);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "WindowsStartupRegistration: failed to read Run key");
                return false;
            }
        }

        public static bool Apply(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
                if (key == null) return false;

                if (enabled)
                {
                    var exePath = Environment.ProcessPath
                                  ?? Process.GetCurrentProcess().MainModule?.FileName;
                    if (string.IsNullOrEmpty(exePath)) return false;
                    key.SetValue(ValueName, $"\"{exePath}\"", RegistryValueKind.String);
                }
                else
                {
                    if (key.GetValue(ValueName) != null)
                        key.DeleteValue(ValueName, throwOnMissingValue: false);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "WindowsStartupRegistration: failed to apply Run key (enabled={Enabled})", enabled);
                return false;
            }
        }
    }
}
