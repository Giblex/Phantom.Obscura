using System;
using System.Threading.Tasks;
using PhantomVault.Core.Models.AutoInject;

namespace PhantomVault.Core.Services.AutoInject
{
    /// <summary>
    /// Main orchestrator for USB auto-inject functionality
    /// </summary>
    public interface IUsbAutoInjectService
    {
        /// <summary>
        /// Event raised when USB is inserted and credentials are found
        /// </summary>
        event EventHandler<AutoInjectPromptEventArgs>? PromptRequired;

        /// <summary>
        /// Event raised when passkey authentication is ready (silent)
        /// </summary>
        event EventHandler<PasskeyReadyEventArgs>? PasskeyReady;

        /// <summary>
        /// Sets the factory that creates ICredentialProvider on-demand.
        /// This allows integration with Transient VaultViewModel instances.
        /// </summary>
        void SetCredentialProviderFactory(Func<ICredentialProvider?> factory);

        /// <summary>
        /// Initializes the service and starts monitoring
        /// </summary>
        Task StartAsync();

        /// <summary>
        /// Stops monitoring
        /// </summary>
        Task StopAsync();

        /// <summary>
        /// Manually triggers auto-inject for current context
        /// </summary>
        Task TriggerAutoInjectAsync();

        /// <summary>
        /// Performs auto-fill with the specified credential
        /// </summary>
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
