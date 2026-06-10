using System;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Data.Converters;

namespace PhantomVault.UI.Converters
{
    public class IconPathExistsConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool exists = false;

            if (value is string s && !string.IsNullOrWhiteSpace(s))
            {
                var resolved = ResolveIconPath(s);
                exists = resolved != null && File.Exists(resolved);
            }

            if (parameter is string param && param.Equals("invert", StringComparison.OrdinalIgnoreCase))
            {
                return !exists;
            }

            return exists;
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

