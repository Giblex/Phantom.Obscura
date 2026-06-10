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
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Services.Network;

namespace PhantomVault.Core.Services
{
    public enum IconFormat
    {
        Png,
        Svg,
    }

    /// <summary>
    /// Flaticon icon search + download. Every outbound HTTP call is funnelled
    /// through <see cref="IInternetGateway"/> — no ambient HttpClient, no
    /// connection without an active user-granted, SPKI-pinned access grant.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class SecureIconDownloaderService : IDisposable
    {
        private readonly IInternetGateway _gateway;
        private readonly EncryptionService _encryptionService;
        private readonly string _iconCacheDir;
        private readonly string _apiConfigPath;

        private InternetAccessGrant? _activeGrant;
        private string? _apiKey;
        private bool _disposed;

        private const int ApiKeyStorageVersion = 2;
        private const string ApiKeyProtector = "dpapi-user";
        private const string ApiKeyAssociatedData = "PhantomVault.Flaticon.API";

        private const string FlaticonApiBase = "https://api.flaticon.com/v3";
        private const string FlaticonCdnPngBase = "https://cdn-icons-png.flaticon.com";
        private const string FlaticonCdnSvgBase = "https://cdn-icons-svg.flaticon.com";

        public SecureIconDownloaderService(IInternetGateway gateway)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _encryptionService = new EncryptionService();

            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhantomVault",
                "IconCache");

            _iconCacheDir = appDataDir;
            _apiConfigPath = Path.Combine(appDataDir, "flaticon_api.config");

            if (!Directory.Exists(_iconCacheDir))
                Directory.CreateDirectory(_iconCacheDir);

            try { LoadApiKey(); }
            catch { _apiKey = null; }
        }

        public bool IsInternetEnabled => _activeGrant?.IsLive ?? false;

        public bool HasApiKeyConfigured => !string.IsNullOrWhiteSpace(_apiKey);

        /// <summary>
        /// Request a time-scoped internet-access grant from the gateway. The user
        /// will be prompted to allow or deny. Returns true if a live grant was issued.
        /// </summary>
        public async Task<bool> RequestInternetAccessAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException(
                    "Flaticon API key not configured. Use SetApiKey() first. " +
                    "Get a free key from https://www.flaticon.com/api.");

            if (_activeGrant is not null && _activeGrant.IsLive)
                return true;

            var grant = await _gateway.RequestAccessAsync(
                FlaticonGatewayPolicy.CreateRequest(),
                cancellationToken).ConfigureAwait(false);

            if (grant is null) return false;
            _activeGrant = grant;
            return true;
        }

        /// <summary>Revoke the current grant. Idempotent.</summary>
        public void DisableInternet()
        {
            if (_activeGrant is null) return;
            _gateway.Revoke(_activeGrant, "Icon downloader: user disabled internet.");
            _activeGrant = null;
        }

        public async Task<List<IconSearchResult>> SearchIconsAsync(string query, CancellationToken ct = default)
        {
            var grant = RequireLiveGrant();

            using var client = _gateway.CreateClient(grant);
            var sanitizedQuery = Uri.EscapeDataString((query ?? string.Empty).Trim());
            var url = $"{FlaticonApiBase}/search/icons?q={sanitizedQuery}&limit=20&order_by=popular";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyDefaultHeaders(req);

            using var response = await client.SendAsync(req, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException($"Flaticon API error ({response.StatusCode}): {error}");
            }

            var jsonResponse = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            FlaticonApiResponse? apiResult;
            try
            {
                apiResult = JsonSerializer.Deserialize<FlaticonApiResponse>(jsonResponse);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Failed to parse Flaticon response: {ex.Message}", ex);
            }

            if (apiResult?.Data is null)
                return new List<IconSearchResult>();

            var results = new List<IconSearchResult>(apiResult.Data.Count);
            foreach (var icon in apiResult.Data)
            {
                var png512 = icon.Images?.Png512;
                if (string.IsNullOrWhiteSpace(png512) ||
                    !png512.StartsWith(FlaticonCdnPngBase, StringComparison.OrdinalIgnoreCase))
                    continue;

                var svgUrl = icon.Images?.Svg;
                if (!string.IsNullOrWhiteSpace(svgUrl) &&
                    !svgUrl.StartsWith(FlaticonCdnSvgBase, StringComparison.OrdinalIgnoreCase))
                {
                    svgUrl = null;
                }

                results.Add(new IconSearchResult
                {
                    Id = icon.Id.ToString(),
                    Name = string.IsNullOrWhiteSpace(icon.Description) ? $"Icon {icon.Id}" : icon.Description!,
                    Url = png512!,
                    SvgUrl = svgUrl,
                    Category = string.Join(", ", icon.Tags?.Take(2) ?? Array.Empty<string>()),
                    PreviewEmoji = MapToEmoji(icon.Tags?.FirstOrDefault()),
                    AuthorName = icon.Author?.Name ?? "Unknown",
                    AuthorUrl = icon.Author?.Url ?? string.Empty,
                });
            }

            return results;
        }

        public Task<string> DownloadIconAsync(IconSearchResult icon, CancellationToken ct = default)
            => DownloadIconAsync(icon, IconFormat.Png, null, null, ct);

        public Task<string> DownloadIconAsync(
            IconSearchResult icon,
            string? customFilename,
            string? targetDirectory = null,
            CancellationToken ct = default)
            => DownloadIconAsync(icon, IconFormat.Png, customFilename, targetDirectory, ct);

        public async Task<string> DownloadIconAsync(
            IconSearchResult icon,
            IconFormat format,
            string? customFilename,
            string? targetDirectory,
            CancellationToken ct = default)
        {
            if (icon is null) throw new ArgumentNullException(nameof(icon));
            var grant = RequireLiveGrant();

            string sourceUrl;
            string extension;
            string cdnBase;

            switch (format)
            {
                case IconFormat.Png:
                    sourceUrl = icon.Url;
                    extension = ".png";
                    cdnBase = FlaticonCdnPngBase;
                    break;

                case IconFormat.Svg:
                    if (string.IsNullOrWhiteSpace(icon.SvgUrl))
                        throw new InvalidOperationException(
                            "SVG variant is not available for this icon. Try a different icon or use PNG.");
                    sourceUrl = icon.SvgUrl!;
                    extension = ".svg";
                    cdnBase = FlaticonCdnSvgBase;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported icon format.");
            }

            if (!sourceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Security violation: only HTTPS download URLs are permitted.");

            if (!sourceUrl.StartsWith(cdnBase, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Security violation: download URL must be on the Flaticon CDN ({cdnBase}).");

            using var client = _gateway.CreateClient(grant);
            using var req = new HttpRequestMessage(HttpMethod.Get, sourceUrl);
            ApplyDefaultHeaders(req);

            using var response = await client.SendAsync(req, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Failed to download icon: HTTP {response.StatusCode}");

            var iconData = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (iconData.Length > 1024 * 1024)
                throw new InvalidOperationException("Icon file too large (max 1MB). Possible security threat.");

            ValidateDownloadedContent(iconData, format);

            string fileName;
            string saveDirectory;

            if (!string.IsNullOrWhiteSpace(customFilename))
            {
                var sanitized = new string(customFilename!.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
                fileName = $"{sanitized}{extension}";
                saveDirectory = targetDirectory ?? _iconCacheDir;
            }
            else
            {
                var sanitizedId = new string(icon.Id.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
                fileName = $"flaticon_{sanitizedId}{extension}";
                saveDirectory = _iconCacheDir;
            }

            if (!Directory.Exists(saveDirectory))
                Directory.CreateDirectory(saveDirectory);

            var savePath = Path.Combine(saveDirectory, fileName);
            await File.WriteAllBytesAsync(savePath, iconData, ct).ConfigureAwait(false);

            return savePath;
        }

        public List<string> GetCachedIcons()
        {
            if (!Directory.Exists(_iconCacheDir)) return new List<string>();
            return Directory.GetFiles(_iconCacheDir)
                .Where(p => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                         || p.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .Where(f => !string.IsNullOrEmpty(f))
                .Select(f => f!)
                .ToList();
        }

        public void ClearCache()
        {
            if (!Directory.Exists(_iconCacheDir)) return;
            foreach (var file in Directory.GetFiles(_iconCacheDir))
            {
                try { File.Delete(file); }
                catch { /* best-effort */ }
            }
        }

        public void SetApiKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key cannot be empty.", nameof(apiKey));

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

        private InternetAccessGrant RequireLiveGrant()
        {
            if (_activeGrant is null || !_activeGrant.IsLive)
                throw new InvalidOperationException(
                    "Internet access has not been granted. Call RequestInternetAccessAsync() first.");
            return _activeGrant;
        }

        private void ApplyDefaultHeaders(HttpRequestMessage req)
        {
            req.Headers.UserAgent.ParseAdd("PhantomVault/1.0");
            req.Headers.TryAddWithoutValidation("DNT", "1");
            req.Headers.Accept.ParseAdd("application/json");
            if (!string.IsNullOrEmpty(_apiKey))
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiKey}");
        }

        private static void ValidateDownloadedContent(byte[] data, IconFormat format)
        {
            if (data.Length < 8)
                throw new InvalidOperationException("Downloaded file is too small to be a valid icon.");

            switch (format)
            {
                case IconFormat.Png:
                    var isPng = data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;
                    var isJpeg = data[0] == 0xFF && data[1] == 0xD8;
                    if (!isPng && !isJpeg)
                        throw new InvalidOperationException(
                            "Downloaded file is not a valid PNG/JPEG image. Possible security threat.");
                    break;

                case IconFormat.Svg:
                    var head = Encoding.UTF8.GetString(data, 0, Math.Min(data.Length, 512)).TrimStart();
                    var looksLikeSvg =
                        head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) ||
                        head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase);
                    if (!looksLikeSvg)
                        throw new InvalidOperationException(
                            "Downloaded file is not a valid SVG. Possible security threat.");
                    // Reject obvious script injection — SVG can carry <script>. We
                    // refuse anything containing a script tag at the top level. The
                    // user-facing renderer must also sandbox SVG (no script eval).
                    var bodyScan = Encoding.UTF8.GetString(data);
                    if (bodyScan.Contains("<script", StringComparison.OrdinalIgnoreCase) ||
                        bodyScan.Contains("javascript:", StringComparison.OrdinalIgnoreCase) ||
                        bodyScan.Contains("onload=", StringComparison.OrdinalIgnoreCase) ||
                        bodyScan.Contains("onerror=", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            "Downloaded SVG contains active content (script/event handlers). Rejected.");
                    break;
            }
        }

        private void LoadApiKey()
        {
            if (!File.Exists(_apiConfigPath))
            {
                _apiKey = null;
                return;
            }

            var raw = File.ReadAllText(_apiConfigPath).Trim();
            if (string.IsNullOrEmpty(raw))
            {
                _apiKey = null;
                return;
            }

            if (!raw.StartsWith("{", StringComparison.Ordinal))
            {
                // Legacy base64 path — re-seal as DPAPI then return.
                var decoded = Convert.FromBase64String(raw);
                try
                {
                    var legacyKey = Encoding.UTF8.GetString(decoded);
                    _apiKey = legacyKey;
                    try { PersistApiKey(legacyKey); } catch { /* best-effort migration */ }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(decoded);
                }
                return;
            }

            var payload = JsonSerializer.Deserialize<ApiKeyStorageModel>(raw)
                ?? throw new InvalidOperationException("API key storage payload is malformed.");

            if (payload.Version < ApiKeyStorageVersion)
                throw new InvalidOperationException("Unsupported API key storage version.");
            if (!string.Equals(payload.KeyProtector, ApiKeyProtector, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsupported API key protector.");
            if (string.IsNullOrWhiteSpace(payload.ProtectedKey) ||
                string.IsNullOrWhiteSpace(payload.Nonce) ||
                string.IsNullOrWhiteSpace(payload.Tag) ||
                string.IsNullOrWhiteSpace(payload.Ciphertext))
                throw new FormatException("API key storage payload is incomplete.");
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Secure API key storage requires Windows DPAPI.");

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
                ZeroIfAny(plaintext, encryptionKey, aad, nonce, tag, ciphertext, protectedKey, entropy);
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
                CryptographicOperations.ZeroMemory(plaintextBytes);
            }

            byte[] entropy = Array.Empty<byte>();
            byte[] protectedKey = Array.Empty<byte>();

            try
            {
                if (!OperatingSystem.IsWindows())
                    throw new PlatformNotSupportedException("Secure API key storage requires Windows DPAPI.");

                entropy = GetKeyEntropy();
                protectedKey = ProtectedData.Protect(encryptionKey, entropy, DataProtectionScope.CurrentUser);

                var payload = new ApiKeyStorageModel
                {
                    Version = ApiKeyStorageVersion,
                    KeyProtector = ApiKeyProtector,
                    ProtectedKey = Convert.ToBase64String(protectedKey),
                    Nonce = Convert.ToBase64String(encResult.Nonce),
                    Tag = Convert.ToBase64String(encResult.Tag),
                    Ciphertext = Convert.ToBase64String(encResult.Ciphertext),
                };

                var json = JsonSerializer.Serialize(payload);
                File.WriteAllText(_apiConfigPath, json);
            }
            finally
            {
                ZeroIfAny(protectedKey, entropy, encryptionKey, aad,
                    encResult.Ciphertext, encResult.Nonce, encResult.Tag);
            }
        }

        private byte[] GetKeyEntropy()
        {
            var entropyFilePath = Path.Combine(Path.GetDirectoryName(_apiConfigPath) ?? "", ".entropy");
            byte[] storedEntropy;

            if (File.Exists(entropyFilePath))
            {
                try
                {
                    storedEntropy = File.ReadAllBytes(entropyFilePath);
                    if (storedEntropy.Length >= 32) return storedEntropy;
                }
                catch { /* fall through to regenerate */ }
            }

            storedEntropy = new byte[32];
            RandomNumberGenerator.Fill(storedEntropy);
            try
            {
                var dir = Path.GetDirectoryName(entropyFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllBytes(entropyFilePath, storedEntropy);
                File.SetAttributes(entropyFilePath, FileAttributes.Hidden);
            }
            catch { /* best-effort persist */ }
            return storedEntropy;
        }

        private static void ZeroIfAny(params byte[][] buffers)
        {
            foreach (var b in buffers)
            {
                if (b is { Length: > 0 })
                    CryptographicOperations.ZeroMemory(b);
            }
        }

        private static string MapToEmoji(string? tag)
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
            if (_disposed) return;
            DisableInternet();
            _disposed = true;
        }
    }

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
        public string? Svg { get; set; }
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
        public string? SvgUrl { get; set; }
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
