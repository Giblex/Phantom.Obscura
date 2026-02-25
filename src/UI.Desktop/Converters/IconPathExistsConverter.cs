using System;
using System.Globalization;
using System.IO;
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

            // Check if we should invert the result
            if (parameter is string param && param.Equals("invert", StringComparison.OrdinalIgnoreCase))
            {
                return !exists;
            }

            return exists;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
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
