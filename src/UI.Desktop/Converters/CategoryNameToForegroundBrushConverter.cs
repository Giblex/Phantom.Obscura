using System;
using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Converters
{
    /// <summary>
    /// Given a category name and a categories collection, returns a contrasting
    /// foreground brush (white/black) for the category's TileColor.
    /// </summary>
    public sealed class CategoryNameToForegroundBrushConverter : IMultiValueConverter
    {
        public static readonly CategoryNameToForegroundBrushConverter Instance = new CategoryNameToForegroundBrushConverter();

        public object? Convert(System.Collections.Generic.IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                if (values == null || values.Count < 2)
                    return Brushes.White;

                var name = values[0] as string;
                var categories = values[1] as IEnumerable;
                if (string.IsNullOrWhiteSpace(name) || categories == null)
                    return Brushes.White;

                foreach (var item in categories)
                {
                    if (item is CategoryViewModel vm && string.Equals(vm.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(vm.TileColor))
                        {
                            try
                            {
                                var c = Color.Parse(vm.TileColor);
                                double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
                                var luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                                return luminance < 0.55 ? Brushes.White : Brushes.Black;
                            }
                            catch { return Brushes.White; }
                        }
                        break;
                    }
                }
            }
            catch { }
            return Brushes.White;
        }
    }
}

