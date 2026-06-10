namespace PhantomVault.Core.Models.AutoInject
{

    public sealed class TotpFieldDescriptor
    {

        public bool IsBrowserField { get; set; }

        public string? BrowserFieldId { get; set; }

        public string? NativeAutomationId { get; set; }

        public int MaxLength { get; set; } = 6;
    }
}

