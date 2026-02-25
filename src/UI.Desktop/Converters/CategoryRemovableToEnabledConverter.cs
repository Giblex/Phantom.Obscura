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
            // Disable Remove for trash categories (not removable)
            if (value is PhantomVault.UI.ViewModels.CategoryItem item)
            {
                if (item.IsTrash) return false;
                var isDeleted = string.Equals(item.Name, "Deleted", StringComparison.OrdinalIgnoreCase);
                var isRubbish = string.Equals(item.Name, "Secure rubbish bin", StringComparison.OrdinalIgnoreCase);
                return !isDeleted && !isRubbish;
            }
            return true;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
