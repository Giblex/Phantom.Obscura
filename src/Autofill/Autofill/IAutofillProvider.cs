namespace PhantomVault.Core.Services.Autofill
{
    /// <summary>
    /// Abstraction for platform‑specific auto‑fill providers. Each
    /// platform (Windows/macOS, Android, iOS) should implement this
    /// interface to integrate with the operating system's password
    /// auto‑completion framework. Implementations should respect user
    /// preferences from the manifest (auto‑fill enabled, username vs
    /// password injection, domain whitelist).
    /// </summary>
    public interface IAutofillProvider
    {
        /// <summary>
        /// Determines whether the provider is supported on the current
        /// platform and properly configured. Should return false if
        /// necessary permissions are missing or if auto‑fill cannot be
        /// offered.
        /// </summary>
        bool IsSupported { get; }

        /// <summary>
        /// Initiates an auto‑fill operation for the specified domain.
        /// Implementations should query the vault for matching
        /// credentials and then inject the username and/or password
        /// according to user preferences. The method returns true if
        /// credentials were filled, false otherwise.
        /// </summary>
        bool TryFill(string domain);
    }
}