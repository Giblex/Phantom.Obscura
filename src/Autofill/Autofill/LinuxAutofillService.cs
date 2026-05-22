using System;
using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services.Autofill
{
    /// <summary>
    /// Linux autofill provider. Supports browser-based autofill via the
    /// PhantomVault browser extension and native messaging host.
    ///
    /// Linux has no single OS-level autofill framework comparable to Android
    /// or iOS. The primary autofill surface is browser extensions (Chrome/Firefox
    /// native messaging). Future: integrate with IBus/AT-SPI for desktop forms.
    /// </summary>
    public sealed class LinuxAutofillService : IAutofillProvider
    {
        private readonly ICredentialRepository _credentialRepository;
        private readonly INativeMessagingHost? _nativeMessagingHost;

        public bool IsSupported =>
            OperatingSystem.IsLinux() &&
            _nativeMessagingHost != null;

        public LinuxAutofillService(
            ICredentialRepository credentialRepository,
            INativeMessagingHost? nativeMessagingHost = null)
        {
            _credentialRepository = credentialRepository
                ?? throw new ArgumentNullException(nameof(credentialRepository));
            _nativeMessagingHost = nativeMessagingHost;
        }

        /// <summary>
        /// Verifies that credentials exist for the domain. Actual browser filling
        /// is performed by the browser extension via the native messaging host.
        /// </summary>
        public bool TryFill(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain)) return false;
            if (!IsSupported) return false;

            try
            {
                var credentials = _credentialRepository
                    .GetCredentialsByDomainAsync(domain)
                    .GetAwaiter()
                    .GetResult();

                return credentials != null && credentials.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Starts the native messaging host so the browser extension can connect.</summary>
        public async Task StartNativeMessagingHostAsync(CancellationToken cancellationToken = default)
        {
            if (_nativeMessagingHost == null)
                throw new InvalidOperationException("Native messaging host not configured");

            await _nativeMessagingHost.StartAsync(cancellationToken);
        }

        /// <summary>Gets whether the native messaging host is currently running.</summary>
        public bool IsNativeMessagingHostRunning => _nativeMessagingHost?.IsRunning ?? false;
    }
}
