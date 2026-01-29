using System;
using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Abstraction for interacting with a VeraCrypt installation. Implementations
    /// may call the VeraCrypt CLI or a native API/driver. This interface is
    /// intentionally small to cover the common operations needed by the vault
    /// creation flow: create, mount and dismount volumes.
    /// </summary>
    public interface IVeraCryptService
    {
        /// <summary>
        /// Gets the path to the VeraCrypt executable if found, otherwise empty string.
        /// </summary>
        string VeraCryptPath { get; }

        /// <summary>
        /// Checks if VeraCrypt is installed and accessible on the system.
        /// </summary>
        bool IsVeraCryptInstalled();

        /// <summary>
        /// Creates a new encrypted VeraCrypt volume.
        /// </summary>
        Task<VeraCryptResult> CreateVolumeAsync(string containerPath, ReadOnlySpan<char> password, long sizeBytes, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a new encrypted VeraCrypt volume with optional keyfile.
        /// </summary>
        Task<VeraCryptResult> CreateVolumeAsync(string containerPath, ReadOnlySpan<char> password, long sizeBytes, string? keyfilePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a new encrypted VeraCrypt container with detailed options and progress reporting.
        /// </summary>
        Task<(bool success, string message)> CreateContainerAsync(
            string containerPath,
            int sizeMB,
            string password,
            string encryptionAlgorithm,
            string hashAlgorithm,
            string filesystem,
            IProgress<int>? progress = null);

        /// <summary>
        /// Mounts a VeraCrypt volume to a specified drive letter.
        /// </summary>
        Task<VeraCryptResult> MountVolumeAsync(string containerPath, char driveLetter, ReadOnlySpan<char> password, CancellationToken cancellationToken = default);

        /// <summary>
        /// Mounts a VeraCrypt volume with optional keyfile support.
        /// Supports password-only, keyfile-only, or dual-factor authentication.
        /// </summary>
        /// <param name="containerPath">Path to the VeraCrypt container.</param>
        /// <param name="driveLetter">Drive letter to mount to.</param>
        /// <param name="password">Password (can be empty if using keyfile-only).</param>
        /// <param name="keyfilePath">Optional path to keyfile.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task<VeraCryptResult> MountVolumeAsync(string containerPath, char driveLetter, ReadOnlySpan<char> password, string? keyfilePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Dismounts a VeraCrypt volume from the specified drive letter.
        /// </summary>
        Task<VeraCryptResult> DismountVolumeAsync(char driveLetter, CancellationToken cancellationToken = default);
    }
}
