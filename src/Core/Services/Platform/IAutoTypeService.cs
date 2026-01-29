using System.Threading.Tasks;

namespace PhantomVault.Core.Services.Platform
{
    /// <summary>
    /// Platform-specific service for typing credentials into forms
    /// </summary>
    public interface IAutoTypeService
    {
        /// <summary>
        /// Types a simple username + password sequence
        /// </summary>
        /// <param name="username">Username to type</param>
        /// <param name="password">Password to type</param>
        /// <param name="submit">Whether to press Enter after password</param>
        Task TypeCredentialsAsync(string username, string password, bool submit = false);

        /// <summary>
        /// Types a custom sequence with special commands
        /// Supports: {username}, {password}, {tab}, {enter}, {delay:ms}
        /// Example: "{username}{tab}{password}{delay:500}{enter}"
        /// </summary>
        Task TypeCustomSequenceAsync(string sequence, string username, string password);

        /// <summary>
        /// Types text character by character with realistic delays
        /// </summary>
        Task TypeTextAsync(string text, int delayMs = 10);

        /// <summary>
        /// Simulates pressing a special key
        /// </summary>
        Task PressKeyAsync(SpecialKey key);
    }

    public enum SpecialKey
    {
        Tab,
        Enter,
        Backspace,
        Delete,
        Escape,
        Up,
        Down,
        Left,
        Right
    }
}
