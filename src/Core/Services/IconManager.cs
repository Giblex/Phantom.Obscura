using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using Serilog;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Manages icon loading from the Assets/Visuals folder and provides
    /// automatic icon matching based on credential names and URLs.
    /// Optimized with caching and lazy directory indexing.
    /// </summary>
    public sealed class IconManager
    {
        private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg", ".svg", ".ico" };
        private readonly string _iconsDirectory;
        
        // Caching for icon path lookups
        private static readonly ConcurrentDictionary<string, string?> _iconPathCache = new();
        private static readonly SemaphoreSlim _cacheLock = new(1, 1);
        
        // Lazy directory index: built once, reused for all lookups
        private readonly Lazy<Dictionary<string, string>> _iconIndex;

        public IconManager(string iconsDirectory)
        {
            try
            {
                _iconsDirectory = iconsDirectory;
                
                // Initialize lazy icon index builder
                _iconIndex = new Lazy<Dictionary<string, string>>(BuildIconIndex, LazyThreadSafetyMode.ExecutionAndPublication);
                
                // Diagnostic logging
                Log.Debug("IconManager constructor - iconsDirectory: {IconsDirectory} (optimized with caching)", _iconsDirectory);

                EnsureIconsDirectoryExists();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "CRITICAL ERROR in IconManager constructor");
                throw;
            }
        }
        
        /// <summary>
        /// Builds an in-memory index of all icon files (filename -> full path).
        /// This is called lazily on first icon lookup and cached for subsequent calls.
        /// </summary>
        private Dictionary<string, string> BuildIconIndex()
        {
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var sw = Stopwatch.StartNew();
            
            try
            {
                if (!Directory.Exists(_iconsDirectory))
                {
                    Debug.WriteLine($"[ICON-MGR-INDEX] Icons directory does not exist: '{_iconsDirectory}'");
                    return index;
                }

                foreach (var searchPath in EnumerateSearchPaths())
                {
                    try
                    {
                        var files = Directory.EnumerateFiles(searchPath, "*.*", SearchOption.AllDirectories);
                        foreach (var file in files)
                        {
                            if (!IsSupportedExtension(file))
                                continue;

                            var nameWithoutExt = Path.GetFileNameWithoutExtension(file);
                            if (string.IsNullOrWhiteSpace(nameWithoutExt))
                                continue;

                            // Store first match only (priority to earlier search paths)
                            if (!index.ContainsKey(nameWithoutExt))
                            {
                                index[nameWithoutExt] = file;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ICON-MGR-INDEX] Error indexing '{searchPath}': {ex.Message}");
                    }
                }

                sw.Stop();
                Log.Information("Built icon index: {IconCount} files in {ElapsedMs}ms", index.Count, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ICON-MGR-INDEX] Failed to build index: {ex.Message}");
            }

            return index;
        }

        private void EnsureIconsDirectoryExists()
        {
            try
            {
                if (!Directory.Exists(_iconsDirectory))
                {
                    Directory.CreateDirectory(_iconsDirectory);
                    Log.Information("Created icons directory: {IconsDirectory}", _iconsDirectory);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to create icons directory");
            }
        }

        /// <summary>
        /// Gets the root directory where icon assets are stored.
        /// </summary>
        public string IconsDirectory => _iconsDirectory;

        /// <summary>
        /// Returns the list of supported icon file extensions.
        /// </summary>
        public static IReadOnlyList<string> SupportedFileExtensions => Array.AsReadOnly(SupportedExtensions);

        private IEnumerable<string> EnumerateSearchPaths()
        {
            // Search paths for the Assets/Visuals folder structure
            var candidates = new[]
            {
                Path.Combine(_iconsDirectory, "Entry Logos"),
                Path.Combine(_iconsDirectory, "App Icons"),
                Path.Combine(_iconsDirectory, "Cat Icons"),
                Path.Combine(_iconsDirectory, "Giblex Icons"),
                _iconsDirectory
            };

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Log.Debug("EnumerateSearchPaths - root: {IconsDirectory}", _iconsDirectory);

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                string? normalized = null;
                try
                {
                    normalized = Path.GetFullPath(candidate);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ICON-MGR] ⚠️ Unable to normalize search path '{candidate}': {ex.Message}");
                }

                if (normalized != null)
                {
                    var exists = Directory.Exists(normalized);
                    Log.Debug("Search path candidate: {Path} Exists: {Exists}", normalized, exists);
                    if (seen.Add(normalized) && exists)
                    {
                        yield return normalized;
                    }
                }
            }
        }

        private (string? Path, string? Term) TryMatchIcon(IEnumerable<string?> searchTerms)
        {
            var termArray = searchTerms
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Select(term => term!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (termArray.Length == 0)
            {
                return (null, null);
            }

            try
            {
                Debug.WriteLine($"[ICON-MGR] TryMatchIcon - terms: {string.Join(", ", termArray)}");
            }
            catch { }

            // Use the cached icon index for lookups
            var index = _iconIndex.Value;
            
            foreach (var term in termArray)
            {
                // Check cache first
                var cacheKey = term.ToLowerInvariant();
                if (_iconPathCache.TryGetValue(cacheKey, out var cachedPath))
                {
                    if (cachedPath != null)
                    {
                        Debug.WriteLine($"[ICON-MGR] Cache hit for term: '{term}' -> '{cachedPath}'");
                        return (cachedPath, term);
                    }
                }
                
                // Check index
                if (index.TryGetValue(term, out var matchedPath))
                {
                    _iconPathCache[cacheKey] = matchedPath;
                    Debug.WriteLine($"[ICON-MGR] Index match for term: '{term}' -> '{matchedPath}'");
                    return (matchedPath, term);
                }
            }

            // Cache negative result for first term to avoid repeated lookups
            if (termArray.Length > 0)
            {
                _iconPathCache[termArray[0].ToLowerInvariant()] = null;
            }

            return (null, null);
        }

        /// <summary>
        /// Attempts to find an icon file matching the credential's title or URL domain
        /// Returns the icon filename (without extension) if found, null otherwise
        /// </summary>
        public string? FindIconForCredential(Credential credential)
        {
            if (string.IsNullOrWhiteSpace(credential.Title) && string.IsNullOrWhiteSpace(credential.Url))
                return null;

            // Try to extract domain from URL
            string? domain = null;
            if (!string.IsNullOrWhiteSpace(credential.Url))
            {
                domain = ExtractDomainFromUrl(credential.Url);
            }

            // Search for icon files matching title or domain
            var searchTerms = new[]
            {
                credential.Title?.ToLowerInvariant()?.Trim(),
                domain?.ToLowerInvariant()?.Trim(),
                // Also try first word of title (e.g., "Netflix Account" -> "netflix")
                credential.Title?.Split(' ').FirstOrDefault()?.ToLowerInvariant()?.Trim()
            }.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct();

            var (matchedPath, matchedTerm) = TryMatchIcon(searchTerms);
            return matchedPath != null && matchedTerm != null
                ? GetIconEmoji(matchedTerm)
                : null;
        }

        /// <summary>
        /// Finds the actual file path for an icon matching the credential
        /// Returns the full path to the icon file if found, null otherwise
        /// </summary>
        public string? FindIconPathForCredential(Credential credential)
        {
            Debug.WriteLine($"[ICON-MGR] FindIconPathForCredential - Title: '{credential.Title}', Url: '{credential.Url}'");
            Debug.WriteLine($"[ICON-MGR] Icons directory: {_iconsDirectory}");

            if (string.IsNullOrWhiteSpace(credential.Title) && string.IsNullOrWhiteSpace(credential.Url))
            {
                Debug.WriteLine("[ICON-MGR] Both title and URL are empty - returning null");
                return null;
            }

            // Try to extract domain from URL
            string? domain = null;
            if (!string.IsNullOrWhiteSpace(credential.Url))
            {
                domain = ExtractDomainFromUrl(credential.Url);
                Debug.WriteLine($"[ICON-MGR] Extracted domain from URL: '{domain}'");
            }

            // Search for icon files matching title or domain
            var searchTerms = new[]
            {
                credential.Title?.ToLowerInvariant()?.Trim(),
                domain?.ToLowerInvariant()?.Trim(),
                // Also try first word of title (e.g., "Netflix Account" -> "netflix")
                credential.Title?.Split(' ').FirstOrDefault()?.ToLowerInvariant()?.Trim()
            }.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToArray();

            Debug.WriteLine($"[ICON-MGR] Search terms: {string.Join(", ", searchTerms)}");

            var (matchedPath, matchedTerm) = TryMatchIcon(searchTerms);

            if (matchedPath != null)
            {
                Debug.WriteLine($"[ICON-MGR] ✅ FOUND '{matchedPath}' for term '{matchedTerm}'");
                return matchedPath;
            }

            Debug.WriteLine("[ICON-MGR] ❌ No icon found for this credential");
            return null;
        }

        /// <summary>
        /// Extracts the domain name from a URL
        /// Example: "https://www.netflix.com/browse" -> "netflix"
        /// </summary>
        private string? ExtractDomainFromUrl(string url)
        {
            try
            {
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    url = "https://" + url;
                }

                var uri = new Uri(url);
                var host = uri.Host.ToLowerInvariant();

                // Remove www. prefix
                if (host.StartsWith("www."))
                {
                    host = host.Substring(4);
                }

                // Extract main domain (remove TLD)
                var parts = host.Split('.');
                if (parts.Length > 0)
                {
                    return parts[0]; // Return the main domain part (e.g., "netflix" from "netflix.com")
                }

                return host;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets an emoji representation for common services
        /// This is a fallback - in the future, this could load actual image files
        /// </summary>
        private string GetIconEmoji(string name)
        {
            var iconMap = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Social Media
                { "facebook", "📘" },
                { "twitter", "🐦" },
                { "instagram", "📷" },
                { "linkedin", "💼" },
                { "reddit", "🤖" },
                { "tiktok", "🎵" },
                { "snapchat", "👻" },
                { "youtube", "▶️" },
                
                // Tech/Cloud
                { "google", "🔍" },
                { "gmail", "📧" },
                { "microsoft", "🪟" },
                { "apple", "🍎" },
                { "amazon", "📦" },
                { "github", "🐙" },
                { "dropbox", "📦" },
                { "onedrive", "☁️" },
                
                // Entertainment
                { "netflix", "🎬" },
                { "spotify", "🎵" },
                { "hulu", "📺" },
                { "disney", "🏰" },
                { "twitch", "🎮" },
                { "steam", "🎮" },
                
                // Finance
                { "paypal", "💰" },
                { "bank", "🏦" },
                { "venmo", "💵" },
                { "stripe", "💳" },
                
                // Communication
                { "slack", "💬" },
                { "discord", "💬" },
                { "zoom", "📹" },
                { "teams", "👥" },
                
                // Default
                { "default", "🔑" }
            };

            return iconMap.TryGetValue(name, out var emoji) ? emoji : iconMap["default"];
        }

        /// <summary>
        /// Generates a search query for the icon downloader based on credential info
        /// </summary>
        public string GenerateSearchQuery(Credential credential)
        {
            // Priority 1: Use domain from URL
            if (!string.IsNullOrWhiteSpace(credential.Url))
            {
                var domain = ExtractDomainFromUrl(credential.Url);
                if (!string.IsNullOrWhiteSpace(domain))
                {
                    return $"{domain} logo icon";
                }
            }

            // Priority 2: Use title (first word)
            if (!string.IsNullOrWhiteSpace(credential.Title))
            {
                var firstWord = credential.Title.Split(' ').FirstOrDefault()?.Trim();
                if (!string.IsNullOrWhiteSpace(firstWord))
                {
                    return $"{firstWord} logo icon";
                }
            }

            // Fallback
            return "generic account icon";
        }

        /// <summary>
        /// Gets the suggested filename for saving an icon for this credential
        /// </summary>
        public string GetSuggestedIconFilename(Credential credential)
        {
            // Try domain first
            if (!string.IsNullOrWhiteSpace(credential.Url))
            {
                var domain = ExtractDomainFromUrl(credential.Url);
                if (!string.IsNullOrWhiteSpace(domain))
                {
                    return SanitizeFilename(domain);
                }
            }

            // Fall back to title
            if (!string.IsNullOrWhiteSpace(credential.Title))
            {
                var firstWord = credential.Title.Split(' ').FirstOrDefault()?.Trim();
                if (!string.IsNullOrWhiteSpace(firstWord))
                {
                    return SanitizeFilename(firstWord);
                }
            }

            return "icon_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        private string SanitizeFilename(string filename)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Join("_", filename.Split(invalid, StringSplitOptions.RemoveEmptyEntries))
                         .ToLowerInvariant()
                         .Trim();
        }

        /// <summary>
        /// Checks if an icon exists for the given credential
        /// </summary>
        public bool HasIconForCredential(Credential credential)
        {
            return FindIconForCredential(credential) != null;
        }

        /// <summary>
        /// Lists all available icon files in the Icons directory.
        /// </summary>
        public string[] GetAvailableIcons()
        {
            if (!Directory.Exists(_iconsDirectory))
                return Array.Empty<string>();

            var files = Directory.GetFiles(_iconsDirectory, "*.*", SearchOption.AllDirectories)
                           .Where(f => IsSupportedExtension(f))
                           .Select(f => Path.GetFileNameWithoutExtension(f) ?? string.Empty)
                           .Where(n => !string.IsNullOrEmpty(n))
                           .ToArray();
            Log.Debug("GetAvailableIcons - found {IconCount} icons under {IconsDirectory}", files.Length, _iconsDirectory);
            return files;
        }

        /// <summary>
        /// Returns detailed metadata for all icon files.
        /// </summary>
        public IconFileInfo[] GetIconFiles()
        {
            try
            {
                if (!Directory.Exists(_iconsDirectory))
                {
                    return Array.Empty<IconFileInfo>();
                }

                var icons = new System.Collections.Generic.List<IconFileInfo>();
                var enumerated = Directory.EnumerateFiles(_iconsDirectory, "*", SearchOption.AllDirectories);
                int enumeratedCount = 0;
                foreach (var file in enumerated)
                {
                    if (!IsSupportedExtension(file))
                    {
                        continue;
                    }

                    try
                    {
                        var info = new FileInfo(file);
                        var relativePath = Path.GetRelativePath(_iconsDirectory, file);
                        var name = Path.GetFileNameWithoutExtension(file) ?? string.Empty;
                        icons.Add(new IconFileInfo(
                            name,
                            relativePath,
                            file,
                            info.Length,
                            info.LastWriteTimeUtc));
                        enumeratedCount++;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to process icon file: {FilePath}", file);
                    }
                }

                Log.Debug("GetIconFiles - enumerated {EnumeratedCount} supported files under {IconsDirectory}, returning {IconCount} files",
                    enumeratedCount, _iconsDirectory, icons.Count);

                return icons
                    .OrderBy(icon => icon.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(icon => icon.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "GetIconFiles failed");
                return Array.Empty<IconFileInfo>();
            }
        }

        /// <summary>
        /// Returns information about sub-folders that contain icon files.
        /// Scans two levels deep (category → subfolder) for fast section discovery.
        /// </summary>
        public IconFolderInfo[] GetIconFolders()
        {
            try
            {
                if (!Directory.Exists(_iconsDirectory))
                    return Array.Empty<IconFolderInfo>();

                var result = new List<IconFolderInfo>();

                foreach (var topDir in Directory.GetDirectories(_iconsDirectory))
                {
                    var topName = Path.GetFileName(topDir);
                    var subDirs = Directory.GetDirectories(topDir);

                    foreach (var subDir in subDirs)
                    {
                        var subName = Path.GetFileName(subDir);
                        var relativePath = Path.GetRelativePath(_iconsDirectory, subDir);
                        int count = 0;
                        try
                        {
                            count = Directory.EnumerateFiles(subDir, "*.*", SearchOption.AllDirectories)
                                             .Count(IsSupportedExtension);
                        }
                        catch { /* skip inaccessible folders */ }

                        if (count > 0)
                        {
                            result.Add(new IconFolderInfo(
                                $"{topName} / {subName}",
                                relativePath,
                                subDir,
                                count));
                        }
                    }

                    // Count files directly in the top-level category folder (not in subfolders)
                    int directCount = 0;
                    try
                    {
                        directCount = Directory.EnumerateFiles(topDir, "*.*", SearchOption.TopDirectoryOnly)
                                               .Count(IsSupportedExtension);
                    }
                    catch { }

                    if (directCount > 0)
                    {
                        result.Add(new IconFolderInfo(
                            topName,
                            Path.GetRelativePath(_iconsDirectory, topDir),
                            topDir,
                            directCount));
                    }
                }

                return result.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "GetIconFolders failed");
                return Array.Empty<IconFolderInfo>();
            }
        }

        /// <summary>
        /// Returns icon file metadata for files within a specific folder (recursively).
        /// Used for lazy-loading a single section of the icon library.
        /// </summary>
        public IconFileInfo[] GetIconFilesInFolder(string folderFullPath)
        {
            try
            {
                if (!Directory.Exists(folderFullPath))
                    return Array.Empty<IconFileInfo>();

                var icons = new List<IconFileInfo>();
                foreach (var file in Directory.EnumerateFiles(folderFullPath, "*.*", SearchOption.AllDirectories))
                {
                    if (!IsSupportedExtension(file))
                        continue;

                    try
                    {
                        var info = new FileInfo(file);
                        var relativePath = Path.GetRelativePath(_iconsDirectory, file);
                        var name = Path.GetFileNameWithoutExtension(file) ?? string.Empty;
                        icons.Add(new IconFileInfo(name, relativePath, file, info.Length, info.LastWriteTimeUtc));
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to process icon file: {FilePath}", file);
                    }
                }

                return icons
                    .OrderBy(icon => icon.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "GetIconFilesInFolder failed for {FolderPath}", folderFullPath);
                return Array.Empty<IconFileInfo>();
            }
        }

        /// <summary>
        /// Returns top-level icon categories (e.g. Cat Icons, Entry Logos).
        /// Each category reports its total file count and whether it contains variant subfolders.
        /// </summary>
        public IconCategoryInfo[] GetIconCategories()
        {
            try
            {
                if (!Directory.Exists(_iconsDirectory))
                    return Array.Empty<IconCategoryInfo>();

                var result = new List<IconCategoryInfo>();

                foreach (var topDir in Directory.GetDirectories(_iconsDirectory))
                {
                    var name = Path.GetFileName(topDir);
                    var subDirs = Directory.GetDirectories(topDir);
                    int fileCount = 0;
                    try
                    {
                        fileCount = Directory.EnumerateFiles(topDir, "*.*", SearchOption.AllDirectories)
                                             .Count(IsSupportedExtension);
                    }
                    catch { }

                    if (fileCount == 0) continue;

                    // Heuristic: variant category has many subfolders each with a small number of files
                    bool isVariantCategory = false;
                    if (subDirs.Length > 10)
                    {
                        var sampleCounts = subDirs.Take(10).Select(d =>
                        {
                            try { return Directory.EnumerateFiles(d, "*.*", SearchOption.TopDirectoryOnly).Count(IsSupportedExtension); }
                            catch { return 0; }
                        }).ToArray();
                        isVariantCategory = sampleCounts.All(c => c > 0 && c <= 30);
                    }

                    result.Add(new IconCategoryInfo(
                        name,
                        Path.GetRelativePath(_iconsDirectory, topDir),
                        topDir,
                        fileCount,
                        subDirs.Length,
                        isVariantCategory));
                }

                return result.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "GetIconCategories failed");
                return Array.Empty<IconCategoryInfo>();
            }
        }

        /// <summary>
        /// Returns subfolders within a category path, with file counts.
        /// Used for variant-mode categories to enumerate icon groups.
        /// </summary>
        public IconSubfolderInfo[] GetCategorySubfolders(string categoryPath)
        {
            try
            {
                if (!Directory.Exists(categoryPath))
                    return Array.Empty<IconSubfolderInfo>();

                var result = new List<IconSubfolderInfo>();
                foreach (var subDir in Directory.GetDirectories(categoryPath))
                {
                    var name = Path.GetFileName(subDir);
                    int count = 0;
                    try { count = Directory.EnumerateFiles(subDir, "*.*", SearchOption.AllDirectories).Count(IsSupportedExtension); }
                    catch { }
                    if (count > 0)
                        result.Add(new IconSubfolderInfo(name, subDir, count));
                }
                return result.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "GetCategorySubfolders failed for {Path}", categoryPath);
                return Array.Empty<IconSubfolderInfo>();
            }
        }

        private static bool IsSupportedExtension(string path)
        {
            var extension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(extension))
            {
                return false;
            }

            return SupportedExtensions.Contains(extension.ToLowerInvariant());
        }
    }

    public sealed record IconFileInfo(string Name, string RelativePath, string FullPath, long SizeBytes, DateTime LastModifiedUtc);
    public sealed record IconFolderInfo(string Name, string RelativePath, string FullPath, int FileCount);
    public sealed record IconCategoryInfo(string Name, string RelativePath, string FullPath, int FileCount, int SubfolderCount, bool IsVariantCategory);
    public sealed record IconSubfolderInfo(string Name, string FullPath, int FileCount);
}
