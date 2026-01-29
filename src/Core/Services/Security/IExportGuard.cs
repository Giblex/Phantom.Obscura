namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Guards vault export operations to detect and prevent data exfiltration.
    /// Tracks export events in a sliding window and raises threats when thresholds are exceeded.
    /// </summary>
    public interface IExportGuard
    {
        /// <summary>
        /// Checks if an export operation is allowed for the specified export type.
        /// Returns false if the guard is in cooldown due to excessive exports.
        /// </summary>
        /// <param name="exportType">Type of export (e.g., "CSV", "JSON", "KeePass").</param>
        /// <returns>True if export is allowed, false if blocked by cooldown.</returns>
        bool CanExport(string exportType);

        /// <summary>
        /// Registers an export event for threat detection.
        /// Call this after successfully completing an export operation.
        /// </summary>
        /// <param name="exportType">Type of export that was performed.</param>
        void RegisterExport(string exportType);
    }
}
