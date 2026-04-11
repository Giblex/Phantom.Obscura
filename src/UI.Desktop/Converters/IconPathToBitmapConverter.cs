using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace PhantomVault.UI.Converters
{
    public class IconPathToBitmapConverter : IValueConverter
    {
        private static readonly Dictionary<string, Bitmap?> _cache = new();
        private static readonly object _lock = new object();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string s && !string.IsNullOrWhiteSpace(s))
            {
                try
                {
                    // Resolve relative paths (e.g. /Assets/Visuals/...) to absolute
                    var resolved = ResolveIconPath(s);
                    if (resolved != null && File.Exists(resolved))
                    {
                        // Check cache first
                        lock (_lock)
                        {
                            if (_cache.TryGetValue(resolved, out var cachedBitmap))
                            {
                                return cachedBitmap;
                            }
                        }

                        // Load bitmap
                        var bitmap = new Bitmap(resolved);

                        // Cache it
                        lock (_lock)
                        {
                            _cache[resolved] = bitmap;

                            // Limit cache size to prevent memory issues
                            if (_cache.Count > 500)
                            {
                                // Remove oldest entries (simple approach: clear half)
                                var toRemove = _cache.Keys.Take(_cache.Count / 2).ToList();
                                foreach (var key in toRemove)
                                {
                                    _cache[key]?.Dispose();
                                    _cache.Remove(key);
                                }
                            }
                        }

                        return bitmap;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[IconPathToBitmapConverter] Failed to load {s}: {ex.Message}");
                    // Return null to allow fallback icon display
                }
            }
            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return AvaloniaProperty.UnsetValue;
        }

        /// <summary>
        /// Resolves a relative icon path (starting with /) to an absolute path
        /// using AppContext.BaseDirectory. Returns the original path if already absolute.
        /// </summary>
        private static string? ResolveIconPath(string path)
        {
            if (File.Exists(path))
                return path;

            // Resolve paths like /Assets/Visuals/... relative to the app base directory
            if (path.StartsWith("/") || path.StartsWith("\\"))
            {
                var resolved = Path.Combine(AppContext.BaseDirectory, path.TrimStart('/', '\\'));
                if (File.Exists(resolved))
                    return resolved;
            }

            return null;
        }
    }
}
