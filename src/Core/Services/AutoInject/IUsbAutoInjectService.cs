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

        /// <summary>
        /// Types part or all of a credential into the focused window.
        ///
        /// <paramref name="autoSubmit"/> should normally be false: the user is
        /// expected to review what was entered and press Enter themselves. Submitting
        /// for them removes the chance to correct a wrong match.
        /// </summary>
        Task AutoFillAsync(string credentialId, bool autoSubmit, AutoFillField field = AutoFillField.Both);

        /// <summary>
        /// Copies one field to the clipboard, excluded from clipboard history and
        /// cleared automatically after <paramref name="clearAfterSeconds"/>.
        /// Returns false if the credential or field is unavailable.
        /// </summary>
        Task<bool> CopyFieldAsync(string credentialId, AutoFillField field, int clearAfterSeconds = 30);

        /// <summary>
        /// Current TOTP code and its remaining lifetime, or null if this credential
        /// has no TOTP secret configured.
        /// </summary>
        Task<TotpSnapshot?> GetTotpAsync(string credentialId);
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

