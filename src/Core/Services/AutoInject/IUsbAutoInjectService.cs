using System;
using System.Threading.Tasks;
using PhantomVault.Core.Models.AutoInject;

namespace PhantomVault.Core.Services.AutoInject
{

    public interface IUsbAutoInjectService
    {

        event EventHandler<AutoInjectPromptEventArgs>? PromptRequired;

        event EventHandler<PasskeyReadyEventArgs>? PasskeyReady;

        void SetCredentialProviderFactory(Func<ICredentialProvider?> factory);

        Task StartAsync();

        Task StopAsync();

        Task TriggerAutoInjectAsync();

        Task AutoFillAsync(string credentialId, bool autoSubmit);
    }

    public class AutoInjectPromptEventArgs : EventArgs
    {
        public AutoInjectContext Context { get; set; } = new();
        public CredentialMatch[] Matches { get; set; } = Array.Empty<CredentialMatch>();
        public AutoInjectPolicy Policy { get; set; } = new();
    }

    public class PasskeyReadyEventArgs : EventArgs
    {
        public string Domain { get; set; } = string.Empty;
        public string CredentialId { get; set; } = string.Empty;
    }
}

