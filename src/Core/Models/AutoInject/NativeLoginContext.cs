using System;

namespace PhantomVault.Core.Models.AutoInject
{

    public sealed class NativeLoginContext
    {

        public IntPtr WindowHandle { get; set; }

        public string? UsernameAutomationId { get; set; }

        public string? PasswordAutomationId { get; set; }

        public string? TotpAutomationId { get; set; }

        public string ProcessName { get; set; } = string.Empty;

        public string WindowTitle { get; set; } = string.Empty;
    }
}

