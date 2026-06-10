using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PhantomVault.Core.Services.Security
{

    public sealed class SystemSecurityController : ISystemSecurityController
    {
        private readonly ILogger<SystemSecurityController>? _logger;
        private Func<Task>? _clipboardClearer;

        public SystemSecurityController(ILogger<SystemSecurityController>? logger = null)
        {
            _logger = logger;
        }

        public void RegisterClipboardClearer(Func<Task> clearer)
        {
            _clipboardClearer = clearer ?? throw new ArgumentNullException(nameof(clearer));
        }

        public async Task ClearClipboardAsync()
        {
            _logger?.LogInformation("Clearing clipboard");
            try
            {
                if (_clipboardClearer is not null)
                {
                    await _clipboardClearer();
                }
                else
                {
                    _logger?.LogWarning("No clipboard clearer registered; clipboard was not cleared");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to clear clipboard");
            }
        }

        public void ScrubSensitiveCaches()
        {
            _logger?.LogInformation("Scrubbing sensitive caches");

            GC.Collect(0, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();

            _logger?.LogInformation("Sensitive cache scrub complete");
        }
    }
}

