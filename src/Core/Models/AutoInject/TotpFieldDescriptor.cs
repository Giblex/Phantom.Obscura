namespace PhantomVault.Core.Models.AutoInject
{
    /// <summary>
    /// Describes a TOTP / 2FA input field detected after a password fill.
    /// Carries enough information for the orchestrator to route the fill
    /// through the correct channel (browser native messaging or UI Automation).
    /// </summary>
    public sealed class TotpFieldDescriptor
    {
        /// <summary>True when the field was found via the browser extension.</summary>
        public bool IsBrowserField { get; set; }

        /// <summary>
        /// HTML element id or name attribute (browser path).
        /// Null for native-app fields.
        /// </summary>
        public string? BrowserFieldId { get; set; }

        /// <summary>
        /// UI Automation AutomationId or control Name (native-app path).
        /// Null for browser fields.
        /// </summary>
        public string? NativeAutomationId { get; set; }

        /// <summary>Expected code length — typically 6 or 8 digits.</summary>
        public int MaxLength { get; set; } = 6;
    }
}
