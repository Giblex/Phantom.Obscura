using System;
using System.Globalization;
using Avalonia.Data.Converters;
using PhantomVault.Core.Models;

namespace PhantomVault.UI.Converters
{

    public sealed class SectionKindDisplayConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is EntrySectionKind kind)
                return EntrySection.DefaultLabel(kind);

            return value?.ToString() ?? string.Empty;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
