using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.Autofill
{
    /// <summary>
    /// Windows desktop autofill provider. 
    /// Works in conjunction with the browser extension and native messaging host.
    /// Does not implement OS-level autofill (Windows Credential Manager) - 
    /// instead provides backend support for browser-based autofill via extension.
    /// </summary>
    public class WindowsAutofillService : IAutofillProvider
    {
        private readonly ICredentialRepository _credentialRepository;
        private readonly INativeMessagingHost? _nativeMessagingHost;

        public bool IsSupported =>
            OperatingSystem.IsWindows() &&
            _nativeMessagingHost != null;

        public WindowsAutofillService(
            ICredentialRepository credentialRepository,
            INativeMessagingHost? nativeMessagingHost = null)
        {
            _credentialRepository = credentialRepository ?? throw new ArgumentNullException(nameof(credentialRepository));
            _nativeMessagingHost = nativeMessagingHost;
        }

        /// <summary>
        /// Attempts to fill credentials for the specified domain.
        /// On Windows desktop, autofill is primarily handled by the browser extension
        /// communicating with the native messaging host. This method is provided for
        /// programmatic access but browser-based autofill is the recommended approach.
        /// </summary>
        public bool TryFill(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
                return false;

            if (!IsSupported)
                return false;

            try
            {
                // Query repository for matching credentials
                var credentials = _credentialRepository.GetCredentialsByDomainAsync(domain).GetAwaiter().GetResult();

                if (credentials.Count == 0)
                    return false;

                // For Windows desktop, credentials are typically filled by the browser extension.
                // This method can be used to verify credentials exist for a domain.
                // Actual filling is done by the extension via native messaging host.

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Starts the native messaging host if available.
        /// This allows the browser extension to communicate with PhantomVault.
        /// </summary>
        public async Task StartNativeMessagingHostAsync(CancellationToken cancellationToken = default)
        {
            if (_nativeMessagingHost == null)
                throw new InvalidOperationException("Native messaging host not configured");

            await _nativeMessagingHost.StartAsync(cancellationToken);
        }

        /// <summary>
        /// Gets whether the native messaging host is currently running.
        /// </summary>
        public bool IsNativeMessagingHostRunning =>
            _nativeMessagingHost?.IsRunning ?? false;
    }
}
