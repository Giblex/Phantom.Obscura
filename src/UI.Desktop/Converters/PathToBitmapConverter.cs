using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace PhantomVault.UI.Converters
{
    public class PathToBitmapConverter : IValueConverter
    {
        // Simple cache to avoid reloading bitmaps repeatedly
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, WeakReference<Bitmap?>> _cache = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null) return null;
            var path = value as string;
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                System.Diagnostics.Debug.WriteLine($"[PathToBitmapConverter] Loading: {path}");
                if (_cache.TryGetValue(path, out var weakRef))
                {
                    if (weakRef.TryGetTarget(out var cached) && cached != null)
                    {
                        return cached;
                    }
                }

                var bitmap = new Bitmap(path);
                System.Diagnostics.Debug.WriteLine($"[PathToBitmapConverter] Loaded: {path} Bitmap size={bitmap.PixelSize}");
                _cache[path] = new WeakReference<Bitmap?>(bitmap);
                return bitmap;
            }
            catch
            {
                // If bitmap creation fails, return null to allow placeholder visuals
                return null;
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
