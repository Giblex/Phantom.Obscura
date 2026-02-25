using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PhantomVault.UI.Converters
{
    public sealed class CategoryDeletedNameToReadOnlyConverter : IValueConverter
    {
        public static readonly CategoryDeletedNameToReadOnlyConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is PhantomVault.UI.ViewModels.CategoryItem item)
            {
                if (item.IsTrash) return true;
                return string.Equals(item.Name, "Deleted", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.Name, "Secure rubbish bin", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}

