using System;
using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services.Autofill
{

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

        public async Task StartNativeMessagingHostAsync(CancellationToken cancellationToken = default)
        {
            if (_nativeMessagingHost == null)
                throw new InvalidOperationException("Native messaging host not configured");

            await _nativeMessagingHost.StartAsync(cancellationToken);
        }

        public bool IsNativeMessagingHostRunning => _nativeMessagingHost?.IsRunning ?? false;
    }
}

