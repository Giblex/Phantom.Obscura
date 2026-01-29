using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia;

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
                    // If absolute path provided and file exists
                    if (File.Exists(s))
                    {
                        // Check cache first
                        lock (_lock)
                        {
                            if (_cache.TryGetValue(s, out var cachedBitmap))
                            {
                                return cachedBitmap;
                            }
                        }

                        // Load bitmap
                        var bitmap = new Bitmap(s);

                        // Cache it
                        lock (_lock)
                        {
                            _cache[s] = bitmap;

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
            throw new NotImplementedException();
        }
    }
}
