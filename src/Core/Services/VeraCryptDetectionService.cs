using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Diagnostics;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Service for detecting, downloading, and managing VeraCrypt installation.
    /// Provides automatic detection of existing installations and guided download process.
    /// </summary>
    public class VeraCryptDetectionService
    {
        private static readonly HttpClient _httpClient = new();
        
        private static readonly Dictionary<OSPlatform, string[]> _veraCryptPaths = new()
        {
            {
                OSPlatform.Windows, new[]
                {
                    @"C:\Program Files\VeraCrypt\VeraCrypt.exe",
                    @"C:\Program Files (x86)\VeraCrypt\VeraCrypt.exe",
                    @"%ProgramFiles%\VeraCrypt\VeraCrypt.exe",
                    @"%LocalAppData%\VeraCrypt\VeraCrypt.exe"
                }
            },
            {
                OSPlatform.Linux, new[]
                {
                    "/usr/bin/veracrypt",
                    "/usr/local/bin/veracrypt",
                    "/opt/veracrypt/veracrypt"
                }
            },
            {
                OSPlatform.OSX, new[]
                {
                    "/Applications/VeraCrypt.app/Contents/MacOS/VeraCrypt",
                    "/usr/local/bin/veracrypt"
                }
            }
        };

        private static readonly Dictionary<OSPlatform, string> _downloadUrls = new()
        {
            { OSPlatform.Windows, "https://launchpad.net/veracrypt/trunk/1.26.7/+download/VeraCrypt Setup 1.26.7.exe" },
            { OSPlatform.Linux, "https://launchpad.net/veracrypt/trunk/1.26.7/+download/veracrypt-1.26.7-setup.tar.bz2" },
            { OSPlatform.OSX, "https://launchpad.net/veracrypt/trunk/1.26.7/+download/VeraCrypt_1.26.7.dmg" }
        };

        public event EventHandler<DownloadProgressEventArgs>? DownloadProgressChanged;
        public event EventHandler<string>? StatusMessageChanged;

        /// <summary>
        /// Detects if VeraCrypt is installed and returns the path to the executable.
        /// </summary>
        public VeraCryptDetectionResult DetectVeraCrypt()
        {
            var result = new VeraCryptDetectionResult { IsInstalled = false };

            try
            {
                OSPlatform currentPlatform = GetCurrentPlatform();
                
                if (!_veraCryptPaths.ContainsKey(currentPlatform))
                {
                    result.ErrorMessage = $"Platform {currentPlatform} is not supported for VeraCrypt detection.";
                    return result;
                }

                foreach (var path in _veraCryptPaths[currentPlatform])
                {
                    var expandedPath = Environment.ExpandEnvironmentVariables(path);
                    
                    if (File.Exists(expandedPath))
                    {
                        result.IsInstalled = true;
                        result.InstallPath = expandedPath;
                        result.Version = GetVeraCryptVersion(expandedPath);
                        result.StatusMessage = $"VeraCrypt {result.Version} detected at: {expandedPath}";
                        return result;
                    }
                }

                result.StatusMessage = "VeraCrypt is not installed on this system.";
                result.DownloadUrl = GetDownloadUrl();
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Error detecting VeraCrypt: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Downloads VeraCrypt installer to the specified location.
        /// </summary>
        public async Task<DownloadResult> DownloadVeraCryptAsync(string destinationPath)
        {
            var result = new DownloadResult { Success = false };

            try
            {
                string? downloadUrl = GetDownloadUrl();
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    result.ErrorMessage = "No download URL available for current platform.";
                    return result;
                }

                StatusMessageChanged?.Invoke(this, "Starting VeraCrypt download...");

                var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;
                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalBytesRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalBytesRead += bytesRead;

                    if (totalBytes.HasValue)
                    {
                        double percentage = (double)totalBytesRead / totalBytes.Value * 100;
                        DownloadProgressChanged?.Invoke(this, new DownloadProgressEventArgs
                        {
                            BytesDownloaded = totalBytesRead,
                            TotalBytes = totalBytes.Value,
                            PercentageComplete = percentage
                        });

                        StatusMessageChanged?.Invoke(this, $"Downloading: {percentage:F1}% ({totalBytesRead / 1024 / 1024} MB / {totalBytes.Value / 1024 / 1024} MB)");
                    }
                }

                result.Success = true;
                result.FilePath = destinationPath;
                result.StatusMessage = $"VeraCrypt installer downloaded to: {destinationPath}";
                StatusMessageChanged?.Invoke(this, result.StatusMessage);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Error downloading VeraCrypt: {ex.Message}";
                StatusMessageChanged?.Invoke(this, result.ErrorMessage);
            }

            return result;
        }

        /// <summary>
        /// Launches the VeraCrypt installer.
        /// </summary>
        public InstallResult LaunchInstaller(string installerPath)
        {
            var result = new InstallResult { Success = false };

            try
            {
                if (!File.Exists(installerPath))
                {
                    result.ErrorMessage = $"Installer file not found: {installerPath}";
                    return result;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true,
                    Verb = "runas" // Request admin privileges on Windows
                };

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // On macOS, open the DMG file
                    startInfo.FileName = "open";
                    startInfo.Arguments = installerPath;
                    startInfo.Verb = "";
                }

                var process = Process.Start(startInfo);
                result.Success = true;
                result.StatusMessage = "VeraCrypt installer launched. Please follow the installation wizard.";
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Error launching installer: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Gets the recommended download URL for the current platform.
        /// </summary>
        public string? GetDownloadUrl()
        {
            OSPlatform currentPlatform = GetCurrentPlatform();
            return _downloadUrls.ContainsKey(currentPlatform) ? _downloadUrls[currentPlatform] : null;
        }

        /// <summary>
        /// Opens the VeraCrypt downloads page in the default browser.
        /// </summary>
        public void OpenDownloadPage()
        {
            try
            {
                var url = "https://www.veracrypt.fr/en/Downloads.html";
                var startInfo = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                StatusMessageChanged?.Invoke(this, $"Error opening download page: {ex.Message}");
            }
        }

        private string GetVeraCryptVersion(string executablePath)
        {
            try
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
                return versionInfo.FileVersion ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private OSPlatform GetCurrentPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return OSPlatform.Windows;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return OSPlatform.Linux;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return OSPlatform.OSX;

            throw new PlatformNotSupportedException("Current platform is not supported.");
        }
    }

    #region Result Classes

    public class VeraCryptDetectionResult
    {
        public bool IsInstalled { get; set; }
        public string? InstallPath { get; set; }
        public string? Version { get; set; }
        public string? DownloadUrl { get; set; }
        public string? StatusMessage { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class DownloadResult
    {
        public bool Success { get; set; }
        public string? FilePath { get; set; }
        public string? StatusMessage { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class InstallResult
    {
        public bool Success { get; set; }
        public string? StatusMessage { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class DownloadProgressEventArgs : EventArgs
    {
        public long BytesDownloaded { get; set; }
        public long TotalBytes { get; set; }
        public double PercentageComplete { get; set; }
    }

    #endregion
}
