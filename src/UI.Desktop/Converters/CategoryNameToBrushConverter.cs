using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using Avalonia.Media;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Converters
{
    /// <summary>
    /// Given a category name (value[0]) and a categories collection (value[1]),
    /// returns a SolidColorBrush from the category's TileColor hex.
    /// Falls back to White when not found or invalid.
    /// </summary>
    public sealed class CategoryNameToBrushConverter : IMultiValueConverter
    {
        public static readonly CategoryNameToBrushConverter Instance = new CategoryNameToBrushConverter();

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
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
                                var color = Color.Parse(vm.TileColor);
                                return new SolidColorBrush(color);
                            }
                            catch { /* ignore parse errors */ }
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
