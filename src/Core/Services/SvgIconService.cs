using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Services;

/// <summary>
/// Service for fetching and caching SVG icons from SVGAPI.
/// Replaces emoji icons with professional vector icons.
/// </summary>
public sealed class SvgIconService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _cacheDirectory;
    private readonly Dictionary<string, string> _memoryCache;
    private const string BaseUrl = "https://api.svgapi.com/v1";

    public SvgIconService(string apiKey)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PhantomVault",
            "IconCache"
        );
        _memoryCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Directory.CreateDirectory(_cacheDirectory);
    }

    /// <summary>
    /// Search for icons by keyword and return list of available icons.
    /// </summary>
    public async Task<List<SvgIconMetadata>> SearchIconsAsync(string query, int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<SvgIconMetadata>();

        try
        {
            var url = $"{BaseUrl}/{_apiKey}/list/?search={Uri.EscapeDataString(query)}&limit={limit}";
            var response = await _httpClient.GetStringAsync(url);
            var result = JsonSerializer.Deserialize<SvgApiSearchResponse>(response);

            return result?.Icons ?? new List<SvgIconMetadata>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SvgIconService] Search failed: {ex.Message}");
            return new List<SvgIconMetadata>();
        }
    }

    /// <summary>
    /// Get SVG icon content by icon name/ID.
    /// Returns cached version if available, otherwise fetches from API.
    /// </summary>
    public async Task<string?> GetIconAsync(string iconName, int size = 24, string color = "currentColor")
    {
        if (string.IsNullOrWhiteSpace(iconName))
            return null;

        var cacheKey = $"{iconName}_{size}_{color}";

        // Check memory cache first
        if (_memoryCache.TryGetValue(cacheKey, out var cachedSvg))
            return cachedSvg;

        // Check disk cache
        var cacheFilePath = Path.Combine(_cacheDirectory, $"{cacheKey}.svg");
        if (File.Exists(cacheFilePath))
        {
            try
            {
                var svg = await File.ReadAllTextAsync(cacheFilePath);
                _memoryCache[cacheKey] = svg;
                return svg;
            }
            catch
            {
                // Cache file corrupted, will re-fetch
            }
        }

        // Fetch from API
        try
        {
            var url = $"{BaseUrl}/{_apiKey}/icon/{Uri.EscapeDataString(iconName)}?size={size}&color={Uri.EscapeDataString(color)}";
            var svg = await _httpClient.GetStringAsync(url);

            // Cache to memory and disk
            _memoryCache[cacheKey] = svg;
            await File.WriteAllTextAsync(cacheFilePath, svg);

            return svg;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SvgIconService] Failed to fetch icon '{iconName}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get pre-defined icon for common UI elements.
    /// </summary>
    public Task<string?> GetPresetIconAsync(IconPreset preset, int size = 24, string color = "currentColor")
    {
        var iconName = preset switch
        {
            IconPreset.Search => "search",
            IconPreset.Lock => "lock",
            IconPreset.Unlock => "unlock",
            IconPreset.Eye => "eye",
            IconPreset.EyeOff => "eye-off",
            IconPreset.Copy => "copy",
            IconPreset.Check => "check",
            IconPreset.X => "x",
            IconPreset.AlertCircle => "alert-circle",
            IconPreset.AlertTriangle => "alert-triangle",
            IconPreset.Info => "info",
            IconPreset.Plus => "plus",
            IconPreset.Minus => "minus",
            IconPreset.Edit => "edit",
            IconPreset.Trash => "trash",
            IconPreset.Download => "download",
            IconPreset.Upload => "upload",
            IconPreset.Settings => "settings",
            IconPreset.User => "user",
            IconPreset.Key => "key",
            IconPreset.Shield => "shield",
            IconPreset.Star => "star",
            IconPreset.Heart => "heart",
            IconPreset.Folder => "folder",
            IconPreset.File => "file",
            IconPreset.CreditCard => "credit-card",
            IconPreset.Globe => "globe",
            IconPreset.Mail => "mail",
            IconPreset.Phone => "phone",
            IconPreset.Calendar => "calendar",
            IconPreset.Clock => "clock",
            IconPreset.Database => "database",
            IconPreset.Cloud => "cloud",
            IconPreset.Wifi => "wifi",
            IconPreset.Bluetooth => "bluetooth",
            IconPreset.Usb => "usb",
            IconPreset.Refresh => "refresh-cw",
            IconPreset.ChevronLeft => "chevron-left",
            IconPreset.ChevronRight => "chevron-right",
            IconPreset.ChevronDown => "chevron-down",
            IconPreset.ChevronUp => "chevron-up",
            IconPreset.Menu => "menu",
            IconPreset.MoreVertical => "more-vertical",
            IconPreset.MoreHorizontal => "more-horizontal",
            _ => "help-circle"
        };

        return GetIconAsync(iconName, size, color);
    }

    /// <summary>
    /// Clear all cached icons (memory and disk).
    /// </summary>
    public void ClearCache()
    {
        _memoryCache.Clear();

        try
        {
            if (Directory.Exists(_cacheDirectory))
            {
                foreach (var file in Directory.GetFiles(_cacheDirectory, "*.svg"))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SvgIconService] Failed to clear cache: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

/// <summary>
/// Pre-defined icon presets for common UI elements.
/// </summary>
public enum IconPreset
{
    Search,
    Lock,
    Unlock,
    Eye,
    EyeOff,
    Copy,
    Check,
    X,
    AlertCircle,
    AlertTriangle,
    Info,
    Plus,
    Minus,
    Edit,
    Trash,
    Download,
    Upload,
    Settings,
    User,
    Key,
    Shield,
    Star,
    Heart,
    Folder,
    File,
    CreditCard,
    Globe,
    Mail,
    Phone,
    Calendar,
    Clock,
    Database,
    Cloud,
    Wifi,
    Bluetooth,
    Usb,
    Refresh,
    ChevronLeft,
    ChevronRight,
    ChevronDown,
    ChevronUp,
    Menu,
    MoreVertical,
    MoreHorizontal
}

/// <summary>
/// Metadata for an SVG icon from the API.
/// </summary>
public sealed class SvgIconMetadata
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// Response from SVGAPI search endpoint.
/// </summary>
internal sealed class SvgApiSearchResponse
{
    [JsonPropertyName("icons")]
    public List<SvgIconMetadata> Icons { get; set; } = new();

    [JsonPropertyName("total")]
    public int Total { get; set; }
}
