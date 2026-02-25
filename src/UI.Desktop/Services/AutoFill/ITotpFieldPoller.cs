using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models.AutoInject;

namespace PhantomVault.UI.Services.AutoFill
{
    /// <summary>
    /// Polls for a TOTP / 2FA input field to appear after password fill.
    /// Supports both browser (via native messaging) and native Windows app paths.
    /// </summary>
    public interface ITotpFieldPoller
    {
        /// <summary>
        /// Waits for a TOTP field to appear in the active login portal.
        /// Returns the field descriptor when found, or <c>null</c> when the
        /// timeout expires before a field is detected.
        /// </summary>
        /// <param name="context">Window context captured before password fill.</param>
        /// <param name="isBrowserContext">
        /// When <c>true</c>, polls via native messaging; otherwise via UI Automation.
        /// </param>
        /// <param name="pollIntervalMs">Polling interval in milliseconds.</param>
        /// <param name="timeoutMs">Maximum total time to poll in milliseconds.</param>
        /// <param name="ct">Cancellation token.</param>
        Task<TotpFieldDescriptor?> WaitForTotpFieldAsync(
            AutoInjectContext context,
            bool isBrowserContext,
            int pollIntervalMs = 500,
            int timeoutMs = 8000,
            CancellationToken ct = default);
    }
}
