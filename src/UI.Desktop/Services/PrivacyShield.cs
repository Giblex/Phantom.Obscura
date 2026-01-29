using System;
using System.Diagnostics;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Centralizes runtime privacy controls for masking diagnostics and UI surface data.
    /// </summary>
    public static class PrivacyShield
    {
        private static bool _privacyModeEnabled;
        private static bool _redactDiagnostics = true;

        public static event EventHandler? PrivacyModeChanged;

        public static bool PrivacyModeEnabled
        {
            get => _privacyModeEnabled;
            set
            {
                if (_privacyModeEnabled == value)
                {
                    return;
                }

                _privacyModeEnabled = value;
                PrivacyModeChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        public static bool RedactDiagnostics
        {
            get => _redactDiagnostics;
            set => _redactDiagnostics = value;
        }

        public static string MaskText(string? value, string? placeholder = null)
        {
            if (!_privacyModeEnabled)
            {
                return value ?? string.Empty;
            }

            return string.IsNullOrWhiteSpace(placeholder) ? "[Hidden]" : placeholder!;
        }

        public static void DebugInfo(string category, string message)
        {
            Debug.WriteLine($"[{category}] {message}");
        }

        public static void DebugSensitive(string category, string message)
        {
            if (_privacyModeEnabled || _redactDiagnostics)
            {
                Debug.WriteLine($"[{category}] [REDACTED]");
                return;
            }

            Debug.WriteLine($"[{category}] {message}");
        }

        public static void DebugSensitive(string category, Func<string> messageFactory)
        {
            if (_privacyModeEnabled || _redactDiagnostics)
            {
                Debug.WriteLine($"[{category}] [REDACTED]");
                return;
            }

            Debug.WriteLine($"[{category}] {messageFactory()}");
        }
    }
}
