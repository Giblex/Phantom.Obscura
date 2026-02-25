using System;

namespace PhantomVault.Core.Models.AutoInject
{
    /// <summary>
    /// Describes login form fields found in a native Windows application
    /// via UI Automation. Used by <c>WindowsNativeLoginDetector</c> to
    /// convey which controls to fill.
    /// </summary>
    public sealed class NativeLoginContext
    {
        /// <summary>Handle of the foreground window at detection time.</summary>
        public IntPtr WindowHandle { get; set; }

        /// <summary>AutomationId or Name of the username/email field. Null if not found.</summary>
        public string? UsernameAutomationId { get; set; }

        /// <summary>AutomationId or Name of the password field. Null if not found.</summary>
        public string? PasswordAutomationId { get; set; }

        /// <summary>
        /// AutomationId or Name of the TOTP / 2FA code field.
        /// Populated only after the TOTP step appears.
        /// </summary>
        public string? TotpAutomationId { get; set; }

        /// <summary>Process name of the host application (e.g. "slack", "putty").</summary>
        public string ProcessName { get; set; } = string.Empty;

        /// <summary>Title bar text of the host window.</summary>
        public string WindowTitle { get; set; } = string.Empty;
    }
}
