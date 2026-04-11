using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Performs system-level security operations (clipboard scrub, in-memory cache wipe)
    /// on behalf of the DefenceEngine. A platform-specific clipboard-clearing callback
    /// is registered at startup by the UI layer via <see cref="RegisterClipboardClearer"/>.
    /// </summary>
    public sealed class SystemSecurityController : ISystemSecurityController
    {
        private readonly ILogger<SystemSecurityController>? _logger;
        private Func<Task>? _clipboardClearer;

        public SystemSecurityController(ILogger<SystemSecurityController>? logger = null)
        {
            _logger = logger;
        }

        /// <inheritdoc />
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

            // Force collection of generation-0 objects where short-lived
            // decrypted credential buffers typically reside. This gives
            // finalizers a chance to run and zero-fill any SecureString or
            // similar wrappers that are no longer referenced.
            GC.Collect(0, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();

            _logger?.LogInformation("Sensitive cache scrub complete");
        }
    }
}
