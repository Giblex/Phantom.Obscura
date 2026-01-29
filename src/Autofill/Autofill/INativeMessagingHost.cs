using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services.Autofill
{
    /// <summary>
    /// Interface for native messaging host that communicates with browser extensions.
    /// Implements the Chrome/Firefox native messaging protocol (stdin/stdout JSON messages).
    /// See: https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging
    /// </summary>
    public interface INativeMessagingHost
    {
        /// <summary>
        /// Starts the native messaging host, listening for messages on stdin.
        /// Runs until the browser extension disconnects or an error occurs.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the listening loop.</param>
        Task StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets whether the native messaging host is currently running.
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Validates that a message origin is from an allowed browser extension.
        /// Returns true if the origin is in the allowlist, false otherwise.
        /// </summary>
        /// <param name="origin">The extension origin (e.g., "chrome-extension://abcdef123456")</param>
        bool ValidateOrigin(string origin);
    }
}
