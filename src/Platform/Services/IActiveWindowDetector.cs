using System.Threading.Tasks;
using PhantomVault.Core.Models.AutoInject;

namespace PhantomVault.Platform.Services
{
    /// <summary>
    /// Platform-specific service for detecting the active window and extracting context.
    /// </summary>
    public interface IActiveWindowDetector
    {
        /// <summary>
        /// Gets the current active window context.
        /// </summary>
        AutoInjectContext GetCurrentContext();

        /// <summary>
        /// Checks if the active window is a supported browser.
        /// </summary>
        bool IsActiveBrowser();

        /// <summary>
        /// Attempts to extract URL from active browser window.
        /// </summary>
        string? TryGetBrowserUrl();

        /// <summary>
        /// Inspects the foreground native application window via UI Automation and
        /// returns a <see cref="NativeLoginContext"/> when a login form is detected.
        /// Returns <c>null</c> if no login fields are found or the platform does not
        /// support UI Automation.
        /// </summary>
        NativeLoginContext? DetectNativeLoginFields();

        /// <summary>
        /// Fills username and password into a native application login form using
        /// UI Automation <c>ValuePattern</c>. Returns <c>true</c> on success;
        /// callers should fall back to keyboard simulation when this returns <c>false</c>.
        /// </summary>
        Task<bool> TryFillNativeLoginAsync(NativeLoginContext context, string username, string password);
    }
}
