using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PhantomVault.UI.Converters
{
    public sealed class CategoryRemovableToEnabledConverter : IValueConverter
    {
        public static readonly CategoryRemovableToEnabledConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            // Enable Remove when the category is NOT Trash (by flag or by name)
            if (value is PhantomVault.UI.ViewModels.CategoryItem item)
            {
                var isDeleted = string.Equals(item.Name, "Deleted", StringComparison.OrdinalIgnoreCase);
                return !isDeleted;
            }
            return true;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
