namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Guards clipboard operations to detect and prevent excessive credential copying.
    /// Tracks copy events in a sliding window and raises threats when thresholds are exceeded.
    /// </summary>
    public interface IClipboardGuard
    {
        /// <summary>
        /// Checks if a clipboard copy operation is allowed.
        /// Returns false if the guard is in cooldown due to excessive copying.
        /// </summary>
        /// <returns>True if copy is allowed, false if blocked by cooldown.</returns>
        bool CanCopy();

        /// <summary>
        /// Registers a clipboard copy event for threat detection.
        /// Call this after successfully copying a credential to the clipboard.
        /// </summary>
        /// <param name="entryId">Identifier of the credential entry being copied.</param>
        void RegisterCopy(string entryId);
    }
}
