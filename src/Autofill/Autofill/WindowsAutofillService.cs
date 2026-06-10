using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.Autofill
{

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

        public bool TryFill(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
                return false;

            if (!IsSupported)
                return false;

            try
            {

                var credentials = _credentialRepository.GetCredentialsByDomainAsync(domain).GetAwaiter().GetResult();

                if (credentials.Count == 0)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task StartNativeMessagingHostAsync(CancellationToken cancellationToken = default)
        {
            if (_nativeMessagingHost == null)
                throw new InvalidOperationException("Native messaging host not configured");

            await _nativeMessagingHost.StartAsync(cancellationToken);
        }

        public bool IsNativeMessagingHostRunning =>
            _nativeMessagingHost?.IsRunning ?? false;
    }
}

