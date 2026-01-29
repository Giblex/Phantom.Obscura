using PhantomVault.Core.Models.AutoInject;

namespace PhantomVault.Platform.Services
{
    /// <summary>
    /// Platform-specific service for detecting the active window and extracting context
    /// </summary>
    public interface IActiveWindowDetector
    {
        /// <summary>
        /// Gets the current active window context
        /// </summary>
        /// <returns>Context information about the active window</returns>
        AutoInjectContext GetCurrentContext();

        /// <summary>
        /// Checks if the active window is a supported browser
        /// </summary>
        bool IsActiveBrowser();

        /// <summary>
        /// Attempts to extract URL from active browser window
        /// </summary>
        /// <returns>URL if available, otherwise null</returns>
        string? TryGetBrowserUrl();
    }
}
