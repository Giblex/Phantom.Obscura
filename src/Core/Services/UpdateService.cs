using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services
{

    public sealed class UpdateService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _currentVersion;
        private readonly string _updateManifestUrl;
        private bool _disposed;

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

        public sealed class DownloadProgress
        {
            public long BytesDownloaded { get; init; }
            public long TotalBytes { get; init; }
            public int PercentComplete => TotalBytes > 0 ? (int)(BytesDownloaded * 100 / TotalBytes) : 0;
        }

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

                return null;
            }
        }

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

                if (!await VerifyFileHashAsync(destinationPath, updateInfo.Sha256Hash, cancellationToken).ConfigureAwait(false))
                {

                    try { File.Delete(destinationPath); } catch { }
                    return false;
                }

                return true;
            }
            catch (Exception)
            {

                try { File.Delete(destinationPath); } catch { }
                return false;
            }
        }

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

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static int[] ParseVersion(string version)
        {

            version = version.TrimStart('v', 'V');

            var parts = version.Split('.');
            var result = new int[parts.Length];

            for (int i = 0; i < parts.Length; i++)
            {

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

