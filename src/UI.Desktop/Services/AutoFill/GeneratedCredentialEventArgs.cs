using System;

namespace PhantomVault.UI.Services.AutoFill
{
    /// <summary>
    /// A password the autofill flow generated and typed into a form that had no saved match.
    ///
    /// Carries no username on purpose: nothing reliable is known about the account at this point,
    /// and the save prompt is where the user supplies it.
    /// </summary>
    public sealed class GeneratedCredentialEventArgs : EventArgs
    {
        public GeneratedCredentialEventArgs(string portalId, string url, string domain, string password)
        {
            PortalId = portalId;
            Url = url;
            Domain = domain;
            Password = password;
        }

        /// <summary>Human-readable name of the site or application the form belonged to.</summary>
        public string PortalId { get; }

        public string Url { get; }

        public string Domain { get; }

        /// <summary>The generated password, already typed into the form.</summary>
        public string Password { get; }
    }
}
