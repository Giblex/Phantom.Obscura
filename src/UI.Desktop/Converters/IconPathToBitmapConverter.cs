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

                    var resolved = ResolveIconPath(s);
                    if (resolved != null && File.Exists(resolved))
                    {

                        lock (_lock)
                        {
                            if (_cache.TryGetValue(resolved, out var cachedBitmap))
                            {
                                return cachedBitmap;
                            }
                        }

                        var bitmap = new Bitmap(resolved);

                        lock (_lock)
                        {
                            _cache[resolved] = bitmap;

                            if (_cache.Count > 500)
                            {

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

                }
            }
            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return AvaloniaProperty.UnsetValue;
        }

        private static string? ResolveIconPath(string path)
        {
            if (File.Exists(path))
                return path;

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

