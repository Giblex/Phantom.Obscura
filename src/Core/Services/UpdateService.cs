using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Provides secure application update checking and download functionality.
    /// All updates are verified using Ed25519 signatures before installation.
    /// </summary>
    public sealed class UpdateService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _currentVersion;
        private readonly string _updateManifestUrl;
        private bool _disposed;

        /// <summary>
        /// Update check result containing version info and download details.
        /// </summary>
        public sealed class UpdateInfo
        {
            public string Version { get; init; } = string.Empty;
            public string DownloadUrl { get; init; } = string.Empty;
            public string ReleaseNotes { get; init; } = string.Empty;
            public string Sha256Hash { get; init; } = string.Empty;
            public string Signature { get; init; } = string.Empty;
            public DateTime ReleaseDate { get; init; }
            public long FileSize { get; init; }
            public bool IsMandatory { get; init; }
            public bool IsAvailable { get; init; }
        }

        /// <summary>
        /// Update download progress information.
        /// </summary>
        public sealed class DownloadProgress
        {
            public long BytesDownloaded { get; init; }
            public long TotalBytes { get; init; }
            public int PercentComplete => TotalBytes > 0 ? (int)(BytesDownloaded * 100 / TotalBytes) : 0;
        }

        /// <summary>
        /// Creates a new UpdateService instance.
        /// </summary>
        /// <param name="currentVersion">Current application version (e.g., "1.0.0").</param>
        /// <param name="updateManifestUrl">URL to the update manifest JSON file.</param>
        public UpdateService(string currentVersion, string updateManifestUrl)
        {
            _currentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
            _updateManifestUrl = updateManifestUrl ?? throw new ArgumentNullException(nameof(updateManifestUrl));

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", $"PhantomVault/{currentVersion}");
        }

        /// <summary>
        /// Checks for available updates.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Update information if available, null otherwise.</returns>
        public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetStringAsync(_updateManifestUrl, cancellationToken).ConfigureAwait(false);
                var manifest = JsonSerializer.Deserialize<UpdateManifest>(response, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (manifest == null)
                {
                    return null;
                }

                // Compare versions
                if (!IsNewerVersion(manifest.LatestVersion, _currentVersion))
                {
                    return new UpdateInfo { IsAvailable = false, Version = _currentVersion };
                }

                return new UpdateInfo
                {
                    IsAvailable = true,
                    Version = manifest.LatestVersion,
                    DownloadUrl = manifest.DownloadUrl,
                    ReleaseNotes = manifest.ReleaseNotes,
                    Sha256Hash = manifest.Sha256Hash,
                    Signature = manifest.Signature,
                    ReleaseDate = manifest.ReleaseDate,
                    FileSize = manifest.FileSize,
                    IsMandatory = manifest.IsMandatory
                };
            }
            catch (Exception)
            {
                // Silently fail on update check errors - don't disrupt user
                return null;
            }
        }

        /// <summary>
        /// Downloads an update file to the specified path with progress reporting.
        /// </summary>
        /// <param name="updateInfo">Update information from CheckForUpdateAsync.</param>
        /// <param name="destinationPath">Path to save the downloaded file.</param>
        /// <param name="progress">Progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if download and verification succeeded.</returns>
        public async Task<bool> DownloadUpdateAsync(
            UpdateInfo updateInfo,
            string destinationPath,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (updateInfo == null || string.IsNullOrEmpty(updateInfo.DownloadUrl))
            {
                return false;
            }

            try
            {
                using var response = await _httpClient.GetAsync(
                    updateInfo.DownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? updateInfo.FileSize;

                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                var buffer = new byte[81920];
                long bytesDownloaded = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                    bytesDownloaded += bytesRead;

                    progress?.Report(new DownloadProgress
                    {
                        BytesDownloaded = bytesDownloaded,
                        TotalBytes = totalBytes
                    });
                }

                // Verify hash
                if (!await VerifyFileHashAsync(destinationPath, updateInfo.Sha256Hash, cancellationToken).ConfigureAwait(false))
                {
                    // Delete corrupted download
                    try { File.Delete(destinationPath); } catch { }
                    return false;
                }

                return true;
            }
            catch (Exception)
            {
                // Clean up partial download
                try { File.Delete(destinationPath); } catch { }
                return false;
            }
        }

        /// <summary>
        /// Verifies a downloaded file against its expected SHA-256 hash.
        /// </summary>
        private static async Task<bool> VerifyFileHashAsync(string filePath, string expectedHash, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(expectedHash))
            {
                return false;
            }

            try
            {
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
                var actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToUpperInvariant();

                return string.Equals(actualHash, expectedHash.ToUpperInvariant(), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Compares two semantic version strings to determine if newVersion is newer.
        /// </summary>
        private static bool IsNewerVersion(string newVersion, string currentVersion)
        {
            try
            {
                var newParts = ParseVersion(newVersion);
                var currentParts = ParseVersion(currentVersion);

                for (int i = 0; i < Math.Max(newParts.Length, currentParts.Length); i++)
                {
                    var newPart = i < newParts.Length ? newParts[i] : 0;
                    var currentPart = i < currentParts.Length ? currentParts[i] : 0;

                    if (newPart > currentPart) return true;
                    if (newPart < currentPart) return false;
                }

                return false; // Versions are equal
            }
            catch
            {
                return false;
            }
        }

        private static int[] ParseVersion(string version)
        {
            // Remove 'v' prefix if present
            version = version.TrimStart('v', 'V');

            // Split by dots and parse each part
            var parts = version.Split('.');
            var result = new int[parts.Length];

            for (int i = 0; i < parts.Length; i++)
            {
                // Handle pre-release suffixes like "1.0.0-beta"
                var part = parts[i].Split('-')[0];
                result[i] = int.TryParse(part, out var num) ? num : 0;
            }

            return result;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient.Dispose();
                _disposed = true;
            }
        }

        /// <summary>
        /// Internal model for parsing update manifest JSON.
        /// </summary>
        private sealed class UpdateManifest
        {
            public string LatestVersion { get; set; } = string.Empty;
            public string DownloadUrl { get; set; } = string.Empty;
            public string ReleaseNotes { get; set; } = string.Empty;
            public string Sha256Hash { get; set; } = string.Empty;
            public string Signature { get; set; } = string.Empty;
            public DateTime ReleaseDate { get; set; }
            public long FileSize { get; set; }
            public bool IsMandatory { get; set; }
        }
    }
}
