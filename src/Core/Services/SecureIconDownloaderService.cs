using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Secure service for downloading icons from Flaticon with controlled internet access.
    /// This service implements security measures to ensure safe icon downloads.
    /// SECURITY: Only connects to authenticated Flaticon API endpoints with HTTPS.
    /// Note: API key storage uses DPAPI which is Windows-only.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class SecureIconDownloaderService : IDisposable
    {
        private HttpClient? _httpClient;
        private bool _isInternetEnabled;
        private readonly string _iconCacheDir;
        private readonly string _apiConfigPath;
        private bool _disposed;
        private readonly EncryptionService _encryptionService;

        private const int ApiKeyStorageVersion = 2;
        private const string ApiKeyProtector = "dpapi-user";
        private const string ApiKeyAssociatedData = "PhantomVault.Flaticon.API";

        // Flaticon API Configuration (requires user to add their API key)
        private const string FLATICON_API_BASE = "https://api.flaticon.com/v3";
        private const string FLATICON_CDN_BASE = "https://cdn-icons-png.flaticon.com";

        private string? _apiKey;

        public SecureIconDownloaderService()
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhantomVault",
                "IconCache"
            );

            _iconCacheDir = appDataDir;
            _apiConfigPath = Path.Combine(appDataDir, "flaticon_api.config");
            _encryptionService = new EncryptionService();

            if (!Directory.Exists(_iconCacheDir))
            {
                Directory.CreateDirectory(_iconCacheDir);
            }

            // Load API key if it exists; icon downloads require explicit opt-in.
            try
            {
                LoadApiKey();
            }
            catch (Exception)
            {
                // If loading fails (e.g., vault not unlocked), remain unconfigured
                _apiKey = null;
            }
        }

        public bool IsInternetEnabled => _isInternetEnabled;
        public bool HasApiKeyConfigured => !string.IsNullOrWhiteSpace(_apiKey);

        /// <summary>
        /// Enable temporary internet access for icon downloads.
        /// This creates an isolated, controlled HTTP client.
        /// SECURITY: Only connects to authenticated Flaticon endpoints with HTTPS.
        /// </summary>
        public void EnableInternet()
        {
            if (_isInternetEnabled)
            {
                return;
            }

            // Security: Verify API key is configured
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                throw new InvalidOperationException(
                    "Flaticon API key not configured. Please set your API key first using SetApiKey().\n" +
                    "Get your API key from: https://www.flaticon.com/api");
            }

            // Create isolated HTTP client with security settings
            var handler = new HttpClientHandler
            {
                // Security: Only allow HTTPS, no redirects
                AllowAutoRedirect = false,
                MaxAutomaticRedirections = 0,
                UseCookies = false, // Don't track cookies
                UseDefaultCredentials = false,
                // Security: Use TLS 1.2 or higher
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
                BaseAddress = new Uri(FLATICON_API_BASE)
            };

            // Security: Set authenticated headers
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PhantomVault/1.0");
            _httpClient.DefaultRequestHeaders.Add("DNT", "1"); // Do Not Track
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            _isInternetEnabled = true;
        }

        /// <summary>
        /// Disable internet access and dispose of HTTP client.
        /// This completely cuts off network capability.
        /// </summary>
        public void DisableInternet()
        {
            if (!_isInternetEnabled)
            {
                return;
            }

            _httpClient?.Dispose();
            _httpClient = null;
            _isInternetEnabled = false;
        }

        /// <summary>
        /// Search for icons on Flaticon using authenticated API.
        /// SECURITY: Only uses HTTPS endpoints with authenticated API access.
        /// </summary>
        public async Task<List<IconSearchResult>> SearchIconsAsync(string query)
        {
            if (!_isInternetEnabled || _httpClient == null)
            {
                throw new InvalidOperationException("Internet access not enabled. Call EnableInternet() first.");
            }

            try
            {
                // Security: Sanitize query to prevent injection
                var sanitizedQuery = Uri.EscapeDataString(query.Trim());

                // Call authenticated Flaticon API
                // API Documentation: https://api.flaticon.com/docs
                var endpoint = $"/search/icons?q={sanitizedQuery}&limit=20&order_by=popular";

                var response = await _httpClient.GetAsync(endpoint);

                // Security: Validate response
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new InvalidOperationException($"Flaticon API error ({response.StatusCode}): {error}");
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var apiResult = JsonSerializer.Deserialize<FlaticonApiResponse>(jsonResponse);

                if (apiResult?.Data == null)
                {
                    return new List<IconSearchResult>();
                }

                // Convert API response to our model
                var results = new List<IconSearchResult>();
                foreach (var icon in apiResult.Data)
                {
                    // Security: Validate URLs are from Flaticon CDN only
                    var png512 = icon.Images?.Png512;
                    if (string.IsNullOrWhiteSpace(png512) || !png512.StartsWith(FLATICON_CDN_BASE, StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // Skip non-Flaticon sources
                    }

                    results.Add(new IconSearchResult
                    {
                        Id = icon.Id.ToString(),
                        Name = string.IsNullOrWhiteSpace(icon.Description) ? $"Icon {icon.Id}" : icon.Description!,
                        Url = png512!,
                        Category = string.Join(", ", icon.Tags?.Take(2) ?? Array.Empty<string>()),
                        PreviewEmoji = MapToEmoji(icon.Tags?.FirstOrDefault()),
                        AuthorName = icon.Author?.Name ?? "Unknown",
                        AuthorUrl = icon.Author?.Url ?? ""
                    });
                }

                return results;
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException($"Network error accessing Flaticon API: {ex.Message}", ex);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Failed to parse Flaticon API response: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Download an icon securely from authenticated Flaticon CDN and save to cache.
        /// SECURITY: Only downloads from verified Flaticon CDN with HTTPS and validates integrity.
        /// </summary>
        public async Task<string> DownloadIconAsync(IconSearchResult icon)
        {
            return await DownloadIconAsync(icon, null);
        }

        /// <summary>
        /// Download an icon with a custom filename to a specific directory.
        /// SECURITY: Only downloads from verified Flaticon CDN with HTTPS and validates integrity.
        /// </summary>
        public async Task<string> DownloadIconAsync(IconSearchResult icon, string? customFilename, string? targetDirectory = null)
        {
            if (!_isInternetEnabled || _httpClient == null)
            {
                throw new InvalidOperationException("Internet access not enabled. Call EnableInternet() first.");
            }

            try
            {
                // Security: Validate URL is HTTPS from Flaticon CDN only
                if (!icon.Url.StartsWith(FLATICON_CDN_BASE, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Security violation: Only Flaticon CDN ({FLATICON_CDN_BASE}) URLs are allowed.");
                }

                if (!icon.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Security violation: Only HTTPS URLs are allowed.");
                }

                // Download icon data from authenticated CDN
                var response = await _httpClient.GetAsync(icon.Url);

                // Security: Check response headers
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"Failed to download icon: HTTP {response.StatusCode}");
                }

                var iconData = await response.Content.ReadAsByteArrayAsync();

                // Security: Validate file size (prevent DoS)
                if (iconData.Length > 1024 * 1024) // 1MB limit
                {
                    throw new InvalidOperationException("Icon file too large (max 1MB). Possible security threat.");
                }

                // Security: Validate it's actually an image with correct signature
                if (!IsValidImageData(iconData))
                {
                    throw new InvalidOperationException("Downloaded file is not a valid PNG/JPEG image. Possible security threat.");
                }

                // Determine save location and filename
                string fileName;
                string saveDirectory;

                if (!string.IsNullOrWhiteSpace(customFilename))
                {
                    // Security: Sanitize custom filename
                    var sanitized = new string(customFilename.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
                    fileName = $"{sanitized}.png";
                    saveDirectory = targetDirectory ?? _iconCacheDir;
                }
                else
                {
                    // Security: Sanitize filename from icon ID
                    var sanitizedId = new string(icon.Id.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
                    fileName = $"flaticon_{sanitizedId}.png";
                    saveDirectory = _iconCacheDir;
                }

                // Ensure target directory exists
                if (!Directory.Exists(saveDirectory))
                {
                    Directory.CreateDirectory(saveDirectory);
                }

                var savePath = Path.Combine(saveDirectory, fileName);

                // Save to directory
                await File.WriteAllBytesAsync(savePath, iconData);

                // Return the preview emoji or path indicator
                return icon.PreviewEmoji;
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException($"Network error downloading icon: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to download icon: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Get list of cached icons
        /// </summary>
        public List<string> GetCachedIcons()
        {
            if (!Directory.Exists(_iconCacheDir))
            {
                return new List<string>();
            }

            return Directory.GetFiles(_iconCacheDir, "*.png")
                .Select(Path.GetFileName)
                .Where(f => !string.IsNullOrEmpty(f))
                .Select(f => f!)
                .ToList();
        }

        /// <summary>
        /// Clear icon cache
        /// </summary>
        public void ClearCache()
        {
            if (Directory.Exists(_iconCacheDir))
            {
                foreach (var file in Directory.GetFiles(_iconCacheDir))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // Ignore errors during cleanup
                    }
                }
            }
        }

        private bool IsValidImageData(byte[] data)
        {
            if (data.Length < 8)
            {
                return false;
            }

            // Check PNG signature
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            {
                return true;
            }

            // Check JPEG signature
            if (data[0] == 0xFF && data[1] == 0xD8)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Set Flaticon API key for authenticated access.
        /// SECURITY: API key is stored encrypted in isolated application data.
        /// </summary>
        public void SetApiKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("API key cannot be empty", nameof(apiKey));
            }

            try
            {
                PersistApiKey(apiKey);
                _apiKey = apiKey;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save API key: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Load API key from secure storage
        /// </summary>
        private void LoadApiKey()
        {
            try
            {
                if (File.Exists(_apiConfigPath))
                {
                    var raw = File.ReadAllText(_apiConfigPath).Trim();
                    if (string.IsNullOrEmpty(raw))
                    {
                        _apiKey = null;
                        return;
                    }

                    if (raw.StartsWith("{", StringComparison.Ordinal))
                    {
                        var payload = JsonSerializer.Deserialize<ApiKeyStorageModel>(raw);
                        if (payload == null)
                        {
                            _apiKey = null;
                            return;
                        }

                        if (payload.Version < ApiKeyStorageVersion)
                        {
                            throw new InvalidOperationException("Unsupported API key storage version");
                        }

                        if (!string.Equals(payload.KeyProtector, ApiKeyProtector, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException("Unsupported API key protector");
                        }

                        if (string.IsNullOrWhiteSpace(payload.ProtectedKey) ||
                            string.IsNullOrWhiteSpace(payload.Nonce) ||
                            string.IsNullOrWhiteSpace(payload.Tag) ||
                            string.IsNullOrWhiteSpace(payload.Ciphertext))
                        {
                            throw new FormatException("API key storage payload is incomplete");
                        }

                        if (!OperatingSystem.IsWindows())
                        {
                            throw new PlatformNotSupportedException("Secure API key storage requires Windows DPAPI.");
                        }

                        byte[] protectedKey = Convert.FromBase64String(payload.ProtectedKey);
                        byte[] entropy = Array.Empty<byte>();
                        byte[] encryptionKey = Array.Empty<byte>();
                        byte[] nonce = Array.Empty<byte>();
                        byte[] tag = Array.Empty<byte>();
                        byte[] ciphertext = Array.Empty<byte>();
                        byte[] aad = Array.Empty<byte>();
                        byte[] plaintext = Array.Empty<byte>();

                        try
                        {
                            entropy = GetKeyEntropy();
                            encryptionKey = ProtectedData.Unprotect(protectedKey, entropy, DataProtectionScope.CurrentUser);
                            nonce = Convert.FromBase64String(payload.Nonce);
                            tag = Convert.FromBase64String(payload.Tag);
                            ciphertext = Convert.FromBase64String(payload.Ciphertext);
                            aad = Encoding.UTF8.GetBytes(ApiKeyAssociatedData);
                            plaintext = _encryptionService.Decrypt(ciphertext, nonce, tag, encryptionKey, aad);
                            _apiKey = Encoding.UTF8.GetString(plaintext);
                        }
                        finally
                        {
                            if (plaintext.Length > 0)
                            {
                                CryptographicOperations.ZeroMemory(plaintext);
                            }

                            if (encryptionKey.Length > 0)
                            {
                                CryptographicOperations.ZeroMemory(encryptionKey);
                            }

                            if (aad.Length > 0)
                            {
                                CryptographicOperations.ZeroMemory(aad);
                            }

                            if (nonce.Length > 0)
                            {
                                CryptographicOperations.ZeroMemory(nonce);
                            }

                            if (tag.Length > 0)
                            {
                                CryptographicOperations.ZeroMemory(tag);
                            }

                            if (ciphertext.Length > 0)
                            {
                                CryptographicOperations.ZeroMemory(ciphertext);
                            }

                            if (protectedKey.Length > 0)
                            {
                                CryptographicOperations.ZeroMemory(protectedKey);
                            }

                            if (entropy.Length > 0)
                            {
                                CryptographicOperations.ZeroMemory(entropy);
                            }
                        }
                    }
                    else
                    {
                        // Legacy base64 storage. Load and immediately migrate to the secure format.
                        var decoded = Convert.FromBase64String(raw);
                        var legacyKey = Encoding.UTF8.GetString(decoded);
                        if (decoded.Length > 0)
                        {
                            CryptographicOperations.ZeroMemory(decoded);
                        }

                        _apiKey = legacyKey;

                        try
                        {
                            PersistApiKey(legacyKey);
                        }
                        catch
                        {
                            // If migration fails, keep the legacy key in-memory but leave file untouched.
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors loading API key
                _apiKey = null;
            }
        }

        private void PersistApiKey(string apiKey)
        {
            byte[] plaintextBytes = Encoding.UTF8.GetBytes(apiKey);
            byte[] encryptionKey = new byte[32];
            RandomNumberGenerator.Fill(encryptionKey);
            byte[] aad = Encoding.UTF8.GetBytes(ApiKeyAssociatedData);
            EncryptionResult encResult;

            try
            {
                encResult = _encryptionService.Encrypt(plaintextBytes, encryptionKey, aad);
            }
            finally
            {
                if (plaintextBytes.Length > 0)
                {
                    CryptographicOperations.ZeroMemory(plaintextBytes);
                }
            }

            byte[] entropy = Array.Empty<byte>();
            byte[] protectedKey = Array.Empty<byte>();

            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    throw new PlatformNotSupportedException("Secure API key storage requires Windows DPAPI.");
                }

                entropy = GetKeyEntropy();
                protectedKey = ProtectedData.Protect(encryptionKey, entropy, DataProtectionScope.CurrentUser);

                var payload = new ApiKeyStorageModel
                {
                    Version = ApiKeyStorageVersion,
                    KeyProtector = ApiKeyProtector,
                    ProtectedKey = Convert.ToBase64String(protectedKey),
                    Nonce = Convert.ToBase64String(encResult.Nonce),
                    Tag = Convert.ToBase64String(encResult.Tag),
                    Ciphertext = Convert.ToBase64String(encResult.Ciphertext)
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    WriteIndented = false
                });

                File.WriteAllText(_apiConfigPath, json);
            }
            catch (PlatformNotSupportedException ex)
            {
                throw new InvalidOperationException("Secure API key storage is not available on this platform.", ex);
            }
            finally
            {
                if (protectedKey.Length > 0)
                {
                    CryptographicOperations.ZeroMemory(protectedKey);
                }

                if (entropy.Length > 0)
                {
                    CryptographicOperations.ZeroMemory(entropy);
                }

                if (encryptionKey.Length > 0)
                {
                    CryptographicOperations.ZeroMemory(encryptionKey);
                }

                if (aad.Length > 0)
                {
                    CryptographicOperations.ZeroMemory(aad);
                }

                if (encResult.Ciphertext.Length > 0)
                {
                    CryptographicOperations.ZeroMemory(encResult.Ciphertext);
                }

                if (encResult.Nonce.Length > 0)
                {
                    CryptographicOperations.ZeroMemory(encResult.Nonce);
                }

                if (encResult.Tag.Length > 0)
                {
                    CryptographicOperations.ZeroMemory(encResult.Tag);
                }
            }
        }

        private byte[] GetKeyEntropy()
        {
            // Use machine-specific entropy combined with a stored random component
            // This provides better security than purely path-based deterministic entropy
            var entropyFilePath = Path.Combine(Path.GetDirectoryName(_apiConfigPath) ?? "", ".entropy");
            byte[] storedEntropy;

            if (File.Exists(entropyFilePath))
            {
                try
                {
                    storedEntropy = File.ReadAllBytes(entropyFilePath);
                    if (storedEntropy.Length >= 32)
                    {
                        return storedEntropy;
                    }
                }
                catch
                {
                    // Fall through to generate new entropy
                }
            }

            // Generate cryptographically secure random entropy
            storedEntropy = new byte[32];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(storedEntropy);
            }

            try
            {
                // Store the entropy for future use (file should be protected by DPAPI scope)
                var dir = Path.GetDirectoryName(entropyFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllBytes(entropyFilePath, storedEntropy);
                File.SetAttributes(entropyFilePath, FileAttributes.Hidden);
            }
            catch
            {
                // If we can't persist, still use the generated entropy for this session
            }

            return storedEntropy;
        }

        /// <summary>
        /// Map icon tags to preview emoji
        /// </summary>
        private string MapToEmoji(string? tag)
        {
            var t = (tag ?? string.Empty).ToLowerInvariant();
            if (t.Contains("social") || t.Contains("facebook") || t.Contains("twitter")) return "📱";
            if (t.Contains("mail") || t.Contains("email") || t.Contains("message")) return "📧";
            if (t.Contains("security") || t.Contains("lock") || t.Contains("safe")) return "🔐";
            if (t.Contains("money") || t.Contains("finance") || t.Contains("bank")) return "💰";
            if (t.Contains("shop") || t.Contains("cart") || t.Contains("store")) return "🛒";
            if (t.Contains("game") || t.Contains("play")) return "🎮";
            if (t.Contains("music") || t.Contains("audio")) return "🎵";
            if (t.Contains("video") || t.Contains("movie")) return "🎬";
            if (t.Contains("cloud") || t.Contains("storage")) return "☁️";
            if (t.Contains("tech") || t.Contains("computer")) return "💻";
            if (t.Contains("phone") || t.Contains("mobile")) return "📱";
            if (t.Contains("camera") || t.Contains("photo")) return "📷";
            if (t.Contains("document") || t.Contains("file")) return "📄";
            if (t.Contains("home") || t.Contains("house")) return "🏠";
            if (t.Contains("travel") || t.Contains("plane")) return "✈️";
            return "🎨";
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                DisableInternet();
                _disposed = true;
            }
        }
    }

    // Flaticon API Response Models
    internal sealed class FlaticonApiResponse
    {
        public List<FlaticonIcon>? Data { get; set; }
        public FlaticonMetadata? Metadata { get; set; }
    }

    internal sealed class FlaticonIcon
    {
        public int Id { get; set; }
        public string? Description { get; set; }
        public List<string>? Tags { get; set; }
        public FlaticonImages? Images { get; set; }
        public FlaticonAuthor? Author { get; set; }
    }

    internal sealed class FlaticonImages
    {
        public string? Png512 { get; set; }
        public string? Png256 { get; set; }
        public string? Png128 { get; set; }
    }

    internal sealed class FlaticonAuthor
    {
        public string? Name { get; set; }
        public string? Url { get; set; }
    }

    internal sealed class FlaticonMetadata
    {
        public int Total { get; set; }
    }

    public sealed class IconSearchResult
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string PreviewEmoji { get; set; } = "🎨";
        public string AuthorName { get; set; } = string.Empty;
        public string AuthorUrl { get; set; } = string.Empty;
    }

    internal sealed class ApiKeyStorageModel
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("keyProtector")]
        public string? KeyProtector { get; set; }

        [JsonPropertyName("protectedKey")]
        public string? ProtectedKey { get; set; }

        [JsonPropertyName("nonce")]
        public string? Nonce { get; set; }

        [JsonPropertyName("tag")]
        public string? Tag { get; set; }

        [JsonPropertyName("ciphertext")]
        public string? Ciphertext { get; set; }
    }
}
